using SolidPOS.PosServer.Contracts.Receipts;
using SolidPOS.PosServer.Contracts.Sales;

namespace SolidPOS.PosServer.Application.Sales;

public interface ISalesRepository
{
    Task<SaleResponse?> CreateAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        CreateSaleRequest request,
        CancellationToken cancellationToken);

    Task<SaleResponse?> VoidAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid saleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken);

    Task<SaleResponse?> VoidByLocalSaleIdAsync(
        Guid tenantId,
        Guid storeId,
        Guid terminalId,
        Guid localSaleId,
        VoidSaleRequest request,
        CancellationToken cancellationToken);

    Task<SaleDetailResponse?> GetByIdAsync(
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SaleListItemResponse>?> ListAsync(
        Guid tenantId,
        SaleListFilters filters,
        CancellationToken cancellationToken);

    Task<ReceiptResponse?> GetReceiptAsync(
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken);
}
