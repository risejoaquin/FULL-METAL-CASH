using SolidPOS.PosServer.Contracts.Returns;

namespace SolidPOS.PosServer.Application.Returns;

public interface IReturnsRepository
{
    Task<ReturnResponse?> CreateAsync(Guid tenantId, Guid storeId, Guid terminalId, CreateReturnRequest request, CancellationToken cancellationToken);

    Task<ReturnResponse?> GetByIdAsync(Guid tenantId, Guid returnId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ReturnListItemResponse>> ListAsync(Guid tenantId, ReturnListFilters filters, CancellationToken cancellationToken);
}
