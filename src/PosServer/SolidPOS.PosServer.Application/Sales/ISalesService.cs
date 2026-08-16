using SolidPOS.PosServer.Contracts.Receipts;
using SolidPOS.PosServer.Contracts.Sales;

namespace SolidPOS.PosServer.Application.Sales;

public interface ISalesService
{
    Task<SaleResponse?> CreateAsync(CreateSaleRequest request, CancellationToken cancellationToken);

    Task<SaleResponse?> VoidAsync(Guid saleId, VoidSaleRequest request, CancellationToken cancellationToken);

    Task<SaleResponse?> VoidByLocalSaleIdAsync(Guid localSaleId, VoidSaleRequest request, CancellationToken cancellationToken);

    Task<SaleDetailResponse?> GetByIdAsync(Guid saleId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SaleListItemResponse>?> ListAsync(SaleListFilters filters, CancellationToken cancellationToken);

    Task<ReceiptResponse?> GetReceiptAsync(Guid saleId, CancellationToken cancellationToken);
}
