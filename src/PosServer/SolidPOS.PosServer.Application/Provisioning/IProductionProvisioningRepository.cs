using SolidPOS.PosServer.Contracts.Provisioning;

namespace SolidPOS.PosServer.Application.Provisioning;

public interface IProductionProvisioningRepository
{
    Task<ProductionTenantBootstrapResponse?> BootstrapTenantAsync(
        ProductionTenantBootstrapRequest request,
        string adminPasswordHash,
        bool disableDemoUser,
        CancellationToken cancellationToken);
}
