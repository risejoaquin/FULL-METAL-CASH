using SolidPOS.PosServer.Contracts.Discounts;

namespace SolidPOS.PosServer.Application.Discounts;

public interface IDiscountsService
{
    Task<IReadOnlyCollection<DiscountListItemResponse>?> ListAsync(DiscountListFilters filters, CancellationToken cancellationToken);

    Task<DiscountResponse?> CreateAsync(CreateDiscountRequest request, CancellationToken cancellationToken);

    Task<DiscountResponse?> UpdateAsync(Guid discountId, UpdateDiscountRequest request, CancellationToken cancellationToken);

    Task<ValidateDiscountResponse?> ValidateAsync(ValidateDiscountRequest request, CancellationToken cancellationToken);
    /// <summary>Read-only preview; never authoritative for payment/sale settlement.</summary>
    Task<DiscountCartQuoteResponse?> QuoteCartAsync(
        DiscountCartQuoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<DiscountCartQuoteResponse?>(null);

}
