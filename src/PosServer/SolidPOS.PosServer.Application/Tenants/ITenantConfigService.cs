using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Application.Tenants;

public interface ITenantConfigService
{
    Task<TenantConfigResponse?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<TenantConfigResponse?> UpdateCurrentAsync(UpdateTenantConfigRequest request, CancellationToken cancellationToken);
}
