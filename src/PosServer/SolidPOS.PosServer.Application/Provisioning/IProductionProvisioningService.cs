using SolidPOS.PosServer.Contracts.Provisioning;

namespace SolidPOS.PosServer.Application.Provisioning;

public interface IProductionProvisioningService
{
    Task<ProductionBootstrapStatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    Task<ProductionTenantBootstrapResponse?> BootstrapTenantAsync(
        ProductionTenantBootstrapRequest request,
        string? providedBootstrapKey,
        CancellationToken cancellationToken);
}
