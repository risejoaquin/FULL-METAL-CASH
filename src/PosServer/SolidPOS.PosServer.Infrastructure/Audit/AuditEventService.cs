using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Contracts.Audit;

namespace SolidPOS.PosServer.Infrastructure.Audit;

public sealed class AuditEventService : IAuditEventService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly ITenantContext _tenantContext;
    private readonly IAuditEventRepository _repository;
    private readonly ILogger<AuditEventService> _logger;

    public AuditEventService(
        ITenantContext tenantContext,
        IAuditEventRepository repository,
        ILogger<AuditEventService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task<AuditEventPageResponse?> ListAsync(AuditEventFilters filters, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
        {
            _logger.LogWarning("Audit event read rejected because tenant context is missing");
            return null;
        }

        if (filters.From.HasValue && filters.To.HasValue && filters.To.Value < filters.From.Value)
        {
            _logger.LogWarning("Audit event read rejected for tenant {TenantId}: invalid date range", _tenantContext.TenantId.Value);
            return null;
        }

        AuditEventFilters normalizedFilters = filters with
        {
            Action = NormalizeText(filters.Action),
            EntityType = NormalizeText(filters.EntityType),
            Page = filters.Page <= 0 ? DefaultPage : filters.Page,
            PageSize = filters.PageSize <= 0 ? DefaultPageSize : Math.Min(filters.PageSize, MaxPageSize)
        };

        AuditEventPageResponse response = await _repository.ListAsync(_tenantContext.TenantId.Value, normalizedFilters, cancellationToken);

        _logger.LogInformation(
            "Audit events read for tenant {TenantId}: page {Page}, pageSize {PageSize}, total {Total}",
            _tenantContext.TenantId.Value,
            response.Meta.Page,
            response.Meta.PageSize,
            response.Meta.Total);

        return response;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
