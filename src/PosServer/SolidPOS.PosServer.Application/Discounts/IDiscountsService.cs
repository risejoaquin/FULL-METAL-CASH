using SolidPOS.PosServer.Contracts.Discounts;

namespace SolidPOS.PosServer.Application.Discounts;

public interface IDiscountsService
{
    Task<IReadOnlyCollection<DiscountListItemResponse>?> ListAsync(DiscountListFilters filters, CancellationToken cancellationToken);

    Task<DiscountResponse?> CreateAsync(CreateDiscountRequest request, CancellationToken cancellationToken);

    Task<DiscountResponse?> UpdateAsync(Guid discountId, UpdateDiscountRequest request, CancellationToken cancellationToken);

    Task<ValidateDiscountResponse?> ValidateAsync(ValidateDiscountRequest request, CancellationToken cancellationToken);
}
