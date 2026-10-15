using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.Startup;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Wpf.ViewModels;

/// <summary>Real SQLite-backed login. Never enables checkout or reuses self-test session state.</summary>
public sealed class ProductionLoginViewModel : ViewModelBase
{
    private readonly LocalAuthService auth;
    private readonly PosCoreStartupInspection inspection;
    private LocalSession? session;
    private string email = string.Empty;
    private string status = "Sin sesión.";
    private bool busy;

    public ProductionLoginViewModel(LocalAuthService auth, PosCoreStartupInspection inspection)
    {
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        if (!inspection.CanBeginAuthentication)
            status = inspection.OperatorMessage;
    }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value);
    }

    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public bool CanLogin => inspection.CanBeginAuthentication && !busy && session is null;
    public bool CanLogout => !busy && session is not null;
    public string Operator => session is null ? "Sin operador autenticado" : session.DisplayName;
    public bool IsAuthenticated => session is not null && session.Status == "active"
        && session.ExpiresAtUtc > DateTimeOffset.UtcNow;
    public LocalSession? ActiveSession => IsAuthenticated ? session : null;

    public async Task LoginAsync(string password)
    {
        if (!CanLogin) return;
        busy = true;
        NotifyState();
        try
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(password))
            {
                Status = "Capture correo y contraseña.";
                return;
            }
            var authenticated = await auth.LoginForTerminalAsync(Email.Trim(), password, inspection.TenantId!.Value, inspection.StoreId!.Value);
            if (authenticated.TenantId != inspection.TenantId ||
                authenticated.StoreId != inspection.StoreId ||
                authenticated.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                await auth.LogoutAsync(authenticated.SessionId);
                Status = "Acceso rechazado: operador fuera de la tienda o sesión caducada.";
                return;
            }
            session = authenticated;
            Status = "Sesión local autenticada. Para cobrar, se requiere caja abierta y permiso sales.create.";
        }
        catch (Exception)
        {
            // Authentication details are not exposed to operators.
            Status = "No se pudo iniciar sesión. Compruebe credenciales y vigencia offline.";
        }
        finally
        {
            busy = false;
            NotifyState();
        }
    }

    public async Task LogoutAsync()
    {
        if (!CanLogout) return;
        busy = true;
        NotifyState();
        try
        {
            await auth.LogoutAsync(session!.SessionId);
            session = null;
            Status = "Sesión cerrada.";
        }
        catch (Exception)
        {
            Status = "No se pudo confirmar cierre de sesión. Suspenda operaciones y reinicie.";
            session = null; // fail closed on uncertain persistent state
        }
        finally
        {
            busy = false;
            NotifyState();
        }
    }

    private void NotifyState()
    {
        RaisePropertyChanged(nameof(CanLogin));
        RaisePropertyChanged(nameof(CanLogout));
        RaisePropertyChanged(nameof(Operator));
        RaisePropertyChanged(nameof(IsAuthenticated));
        RaisePropertyChanged(nameof(ActiveSession));
    }
}
