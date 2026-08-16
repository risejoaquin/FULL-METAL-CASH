using SolidPOS.PosServer.Contracts.Discounts;

namespace SolidPOS.PosServer.Application.Discounts;

public interface IDiscountsRepository
{
    Task<IReadOnlyCollection<DiscountListItemResponse>> ListAsync(Guid tenantId, DiscountListFilters filters, CancellationToken cancellationToken);

    Task<DiscountResponse?> CreateAsync(Guid tenantId, CreateDiscountRequest request, CancellationToken cancellationToken);

    Task<DiscountResponse?> UpdateAsync(Guid tenantId, Guid discountId, UpdateDiscountRequest request, CancellationToken cancellationToken);

    Task<DiscountResponse?> GetByIdAsync(Guid tenantId, Guid discountId, CancellationToken cancellationToken);

    Task<bool> StoreExistsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);

    Task<bool> CategoryExistsAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken);

    Task<bool> ProductExistsAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken);
}
