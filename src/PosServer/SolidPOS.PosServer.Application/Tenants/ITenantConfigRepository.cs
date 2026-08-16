using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Application.Tenants;

public interface ITenantConfigRepository
{
    Task<TenantConfigResponse?> GetAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantConfigResponse?> UpsertAsync(Guid tenantId, UpdateTenantConfigRequest request, CancellationToken cancellationToken);
}
