using SolidPOS.PosServer.Contracts.Audit;

namespace SolidPOS.PosServer.Application.Audit;

public interface IAuditEventService
{
    Task<AuditEventPageResponse?> ListAsync(AuditEventFilters filters, CancellationToken cancellationToken);
}
