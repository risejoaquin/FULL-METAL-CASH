using SolidPOS.PosServer.Contracts.Returns;

namespace SolidPOS.PosServer.Application.Returns;

public interface IReturnsService
{
    Task<ReturnResponse?> CreateAsync(CreateReturnRequest request, CancellationToken cancellationToken);

    Task<ReturnResponse?> GetByIdAsync(Guid returnId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ReturnListItemResponse>?> ListAsync(ReturnListFilters filters, CancellationToken cancellationToken);
}
