using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Discounts;
using SolidPOS.PosServer.Contracts.Discounts;

namespace SolidPOS.PosServer.Infrastructure.Discounts;

public sealed class DiscountsService : IDiscountsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITenantContext _tenantContext;
    private readonly IDiscountsRepository _repository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IClock _clock;
    private readonly ILogger<DiscountsService> _logger;

    public DiscountsService(
        ITenantContext tenantContext,
        IDiscountsRepository repository,
        IAuditEventWriter auditEventWriter,
        IClock clock,
        ILogger<DiscountsService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _auditEventWriter = auditEventWriter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<DiscountListItemResponse>?> ListAsync(DiscountListFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !FiltersAreValid(filters))
        {
            return null;
        }

        DiscountListFilters normalized = filters with
        {
            Search = NormalizeNullable(filters.Search),
            Status = NormalizeNullable(filters.Status),
            Limit = filters.Limit <= 0 ? 50 : filters.Limit
        };

        return await _repository.ListAsync(_tenantContext.TenantId.Value, normalized, cancellationToken);
    }

    public async Task<DiscountResponse?> CreateAsync(CreateDiscountRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !CreateRequestIsValid(request))
        {
            _logger.LogWarning("Discount create rejected by validation");
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        if (!await ScopeExistsAsync(tenantId, request.StoreId, request.CategoryId, request.ProductId, cancellationToken))
        {
            _logger.LogWarning("Discount create rejected because scope does not exist for tenant {TenantId}", tenantId);
            return null;
        }

        CreateDiscountRequest normalized = request with
        {
            Code = NormalizeCode(request.Code),
            Name = request.Name.Trim(),
            DiscountType = NormalizeRequired(request.DiscountType),
            Status = NormalizeNullable(request.Status) ?? "active"
        };

        DiscountResponse? created = await _repository.CreateAsync(tenantId, normalized, cancellationToken);
        if (created is null)
        {
            return null;
        }

        await _auditEventWriter.AppendAsync(
            tenantId,
            "discount.created",
            "discount",
            created.Id,
            null,
            JsonSerializer.SerializeToElement(created, JsonOptions),
            cancellationToken);

        return created;
    }

    public async Task<DiscountResponse?> UpdateAsync(Guid discountId, UpdateDiscountRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || discountId == Guid.Empty || !UpdateRequestIsValid(request))
        {
            _logger.LogWarning("Discount update rejected by validation for discount {DiscountId}", discountId);
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        if (!await ScopeExistsAsync(tenantId, request.StoreId, request.CategoryId, request.ProductId, cancellationToken))
        {
            return null;
        }

        DiscountResponse? before = await _repository.GetByIdAsync(tenantId, discountId, cancellationToken);
        if (before is null)
        {
            return null;
        }

        UpdateDiscountRequest normalized = request with
        {
            Code = NormalizeCode(request.Code),
            Name = NormalizeNullable(request.Name),
            DiscountType = NormalizeNullable(request.DiscountType),
            Status = NormalizeNullable(request.Status)
        };

        DiscountResponse? updated = await _repository.UpdateAsync(tenantId, discountId, normalized, cancellationToken);
        if (updated is null)
        {
            return null;
        }

        await _auditEventWriter.AppendAsync(
            tenantId,
            "discount.updated",
            "discount",
            updated.Id,
            JsonSerializer.SerializeToElement(before, JsonOptions),
            JsonSerializer.SerializeToElement(updated, JsonOptions),
            cancellationToken);

        return updated;
    }

    public async Task<ValidateDiscountResponse?> ValidateAsync(ValidateDiscountRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || !ValidateRequestIsValid(request))
        {
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        DiscountResponse? discount = await _repository.GetByIdAsync(tenantId, request.DiscountId, cancellationToken);
        if (discount is null)
        {
            return new ValidateDiscountResponse(request.DiscountId, false, "DISCOUNT_NOT_FOUND", 0, 0, 0, Formula);
        }

        DateTimeOffset occurredAt = request.OccurredAt ?? _clock.UtcNow;
        decimal quantity = decimal.Parse(request.Quantity, CultureInfo.InvariantCulture);
        long grossCents = ToCents(quantity * request.UnitPriceCents);
        string? rejection = ValidateApplicability(discount, request, occurredAt, grossCents);
        if (rejection is not null)
        {
            return new ValidateDiscountResponse(discount.Id, false, rejection, grossCents, 0, grossCents, Formula);
        }

        long discountCents = CalculateDiscount(discount, grossCents);
        long netCents = Math.Max(0, grossCents - discountCents);
        return new ValidateDiscountResponse(discount.Id, true, null, grossCents, discountCents, netCents, Formula);
    }

    private const string Formula = "grossSales - discounts = netSales + taxes/tips = totalSales";

    private static string? ValidateApplicability(DiscountResponse discount, ValidateDiscountRequest request, DateTimeOffset occurredAt, long grossCents)
    {
        if (grossCents <= 0)
        {
            return "INVALID_GROSS_AMOUNT";
        }

        if (discount.Status != "active")
        {
            return "DISCOUNT_NOT_ACTIVE";
        }

        if (discount.StartsAt.HasValue && occurredAt < discount.StartsAt.Value)
        {
            return "DISCOUNT_NOT_STARTED";
        }

        if (discount.EndsAt.HasValue && occurredAt > discount.EndsAt.Value)
        {
            return "DISCOUNT_EXPIRED";
        }

        if (discount.StoreId.HasValue && request.StoreId != discount.StoreId)
        {
            return "STORE_NOT_ELIGIBLE";
        }

        if (discount.ProductId.HasValue && request.ProductId != discount.ProductId)
        {
            return "PRODUCT_NOT_ELIGIBLE";
        }

        if (discount.CategoryId.HasValue && request.CategoryId != discount.CategoryId)
        {
            return "CATEGORY_NOT_ELIGIBLE";
        }

        return null;
    }

    private static long CalculateDiscount(DiscountResponse discount, long grossCents)
    {
        if (discount.DiscountType == "percentage")
        {
            return Math.Min(grossCents, ToCents(grossCents * discount.Value / 100m));
        }

        return Math.Min(grossCents, ToCents(discount.Value));
    }

    private async Task<bool> ScopeExistsAsync(Guid tenantId, Guid? storeId, Guid? categoryId, Guid? productId, CancellationToken cancellationToken)
    {
        if (storeId.HasValue && !await _repository.StoreExistsAsync(tenantId, storeId.Value, cancellationToken))
        {
            return false;
        }

        if (categoryId.HasValue && !await _repository.CategoryExistsAsync(tenantId, categoryId.Value, cancellationToken))
        {
            return false;
        }

        return !productId.HasValue || await _repository.ProductExistsAsync(tenantId, productId.Value, cancellationToken);
    }

    private static bool CreateRequestIsValid(CreateDiscountRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Name)
            && request.Name.Trim().Length <= 200
            && NullableStringLengthIsValid(request.Code, 100)
            && DiscountTypeIsValid(request.DiscountType)
            && StatusIsValid(request.Status)
            && DiscountValueIsValid(request.DiscountType, request.Value)
            && ScopeIsValid(request.StoreId, request.CategoryId, request.ProductId)
            && DateRangeIsValid(request.StartsAt, request.EndsAt);
    }

    private static bool UpdateRequestIsValid(UpdateDiscountRequest request)
    {
        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return false;
        }

        string? discountType = request.DiscountType;
        decimal? value = request.Value;
        return NullableStringLengthIsValid(request.Code, 100)
            && NullableStringLengthIsValid(request.Name, 200)
            && (discountType is null || DiscountTypeIsValid(discountType))
            && StatusIsValid(request.Status)
            && (!value.HasValue || (discountType is null ? value.Value > 0 : DiscountValueIsValid(discountType, value.Value)))
            && ScopeIsValid(request.StoreId, request.CategoryId, request.ProductId)
            && DateRangeIsValid(request.StartsAt, request.EndsAt);
    }

    private static bool ValidateRequestIsValid(ValidateDiscountRequest request)
    {
        if (request.DiscountId == Guid.Empty || request.ProductId == Guid.Empty || request.UnitPriceCents <= 0)
        {
            return false;
        }

        return decimal.TryParse(request.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) && quantity > 0;
    }

    private static bool FiltersAreValid(DiscountListFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Status) && !StatusIsValid(filters.Status))
        {
            return false;
        }

        return filters.Limit is >= 0 and <= 200;
    }

    private static bool DiscountTypeIsValid(string? value)
    {
        return value?.Trim() is "percentage" or "fixed_amount";
    }

    private static bool StatusIsValid(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() is "active" or "inactive" or "archived";
    }

    private static bool DiscountValueIsValid(string? type, decimal value)
    {
        if (type?.Trim() == "percentage")
        {
            return value is > 0 and <= 100;
        }

        return type?.Trim() == "fixed_amount" && value > 0;
    }

    private static bool ScopeIsValid(Guid? storeId, Guid? categoryId, Guid? productId)
    {
        return storeId != Guid.Empty && categoryId != Guid.Empty && productId != Guid.Empty;
    }

    private static bool DateRangeIsValid(DateTimeOffset? startsAt, DateTimeOffset? endsAt)
    {
        return !startsAt.HasValue || !endsAt.HasValue || startsAt <= endsAt;
    }

    private static bool NullableStringLengthIsValid(string? value, int maxLength)
    {
        return value is null || value.Trim().Length <= maxLength;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeRequired(string value)
    {
        return value.Trim();
    }

    private static long ToCents(decimal value)
    {
        return (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
    }
}
