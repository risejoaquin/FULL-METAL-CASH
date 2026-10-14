using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Startup;

/// <summary>
/// Actual local readiness, not a claim that the WPF point-of-sale or
/// payment workflow is implemented. No remote calls or seeded demo data.
/// </summary>
public sealed record PosCoreStartupInspection(
    bool DatabaseInitialized,
    bool TerminalBound,
    bool LocalUsersAvailable,
    bool CatalogAvailable,
    int CatalogProductCount,
    int LocalUserCount,
    Guid? TenantId,
    Guid? StoreId,
    Guid? TerminalId,
    string StatusCode,
    string OperatorMessage)
{
    public bool CanBeginAuthentication =>
        DatabaseInitialized && TerminalBound && LocalUsersAvailable;
    // Checkout remains disabled until the real transactional WPF flow ships.
    public bool CanCheckout => false;
}

public sealed class PosCoreStartupInspectionService
{
    private readonly ILocalPosRepository _repository;

    public PosCoreStartupInspectionService(ILocalPosRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<PosCoreStartupInspection> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        // Fail closed: a corrupt, locked or unwriteable database must fail
        // startup rather than opening a demo sale UI or reporting readiness.
        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        TerminalBinding? binding =
            await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false);
        bool terminalBound = binding is not null &&
            binding.TenantId != Guid.Empty && binding.StoreId != Guid.Empty &&
            binding.TerminalId != Guid.Empty &&
            binding.SchemaVersion == 4 &&
            !string.IsNullOrWhiteSpace(binding.TerminalFingerprint) &&
            !string.IsNullOrWhiteSpace(binding.TerminalToken);

        int users = terminalBound
            ? await _repository.CountActiveLocalUsersForScopeAsync(
                binding!.TenantId, binding.StoreId, cancellationToken).ConfigureAwait(false)
            : 0;
        int products = await _repository.CountCatalogProductsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (users < 0 || products < 0)
            throw new InvalidOperationException("Invalid local user or catalog counts.");

        string code, message;
        if (!terminalBound)
        {
            code = "terminal_unbound";
            message = "Terminal sin activación verificable. No se permiten ventas.";
        }
        else if (users == 0)
        {
            code = "users_missing";
            message = "No hay usuarios autorizados en esta terminal. No se permiten ventas.";
        }
        else if (products == 0)
        {
            code = "catalog_missing";
            message = "No existe catálogo local confirmado. No se permiten ventas.";
        }
        else
        {
            code = "checkout_not_connected";
            message = "Datos locales disponibles. Cobro bloqueado: falta integrar el flujo transaccional WPF.";
        }

        return new PosCoreStartupInspection(
            true, terminalBound, users > 0, products > 0,
            products, users,
            terminalBound ? binding!.TenantId : null,
            terminalBound ? binding!.StoreId : null,
            terminalBound ? binding!.TerminalId : null,
            code, message);
    }
}
