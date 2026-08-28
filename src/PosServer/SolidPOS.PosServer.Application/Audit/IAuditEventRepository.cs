using SolidPOS.PosServer.Contracts.Audit;

namespace SolidPOS.PosServer.Application.Audit;

public interface IAuditEventRepository
{
    Task<AuditEventPageResponse> ListAsync(Guid tenantId, AuditEventFilters filters, CancellationToken cancellationToken);
}
