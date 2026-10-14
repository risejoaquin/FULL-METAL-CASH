using System.Windows;
using SolidPOS.PosCore.Application.Startup;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class AppShellViewModel : ViewModelBase
{
    private string shellStatus = "Terminal sin verificar. Operaciones deshabilitadas.";
    private bool isSelfTestMode;

    public Visibility SimulationTabVisibility => isSelfTestMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProductionTabVisibility => isSelfTestMode ? Visibility.Collapsed : Visibility.Visible;

    public AppShellViewModel(string databasePath)
    {
        DatabasePath = databasePath;
        Login = new LoginViewModel();
        TerminalStatus = new TerminalStatusViewModel();
        Sales = new SalesViewModel();
        SyncStatus = new SyncStatusViewModel();
        CashShift = new CashShiftViewModel();
        Branding = new BrandingViewModel();
    }

    public string DatabasePath { get; }

    public string ShellStatus
    {
        get => shellStatus;
        set => SetProperty(ref shellStatus, value);
    }

    public LoginViewModel Login { get; }

    public TerminalStatusViewModel TerminalStatus { get; }

    public SalesViewModel Sales { get; }

    public SyncStatusViewModel SyncStatus { get; }

    public CashShiftViewModel CashShift { get; }

    public BrandingViewModel Branding { get; }

    /// <summary>
    /// Production shell is diagnostics-only until authorized login and
    /// atomic checkout are wired. No fabricated sale, cash or sync state.
    /// </summary>
    public void InitializeForProduction(PosCoreStartupInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        isSelfTestMode = false;
        OnPropertyChanged(nameof(SimulationTabVisibility));
        OnPropertyChanged(nameof(ProductionTabVisibility));
        ShellStatus = inspection.OperatorMessage;
        TerminalStatus.Status = inspection.TerminalBound
            ? "Identidad de terminal verificada en SQLite. Cobro deshabilitado."
            : "Activación de terminal requerida. Cobro deshabilitado.";
        TerminalStatus.BindingSummary = inspection.TerminalBound
            ? $"Tenant={inspection.TenantId}; tienda={inspection.StoreId}; terminal={inspection.TerminalId}; schema=4."
            : "No existe binding de terminal v4 válido; solicitar activación.";
        Login.Email = string.Empty;
        Login.Status = inspection.CanBeginAuthentication
            ? "Existen usuarios locales. Login WPF real todavía no integrado; sin sesión."
            : "No hay autenticación disponible hasta completar activación y aprovisionamiento.";
        Sales.CatalogSummary = $"Productos en catálogo SQLite: {inspection.CatalogProductCount}. Venta deshabilitada.";
        Sales.SaleStatus = "No se han registrado ventas. El cobro real está bloqueado.";
        Sales.ReceiptStatus = "Sin recibo confirmado.";
        Sales.SyncVisualStatus = "No se ha verificado ningún ACK remoto.";
        SyncStatus.Status = "Sync no ejecutado desde shell comercial.";
        SyncStatus.QueueSummary = "Estado de cola no verificado; nunca marcar una venta manualmente como sincronizada.";
        CashShift.Status = "Caja no verificada. No se han registrado movimientos.";
        CashShift.ExpectedCashSummary = "Saldo de caja no calculado desde el ledger; acciones bloqueadas.";
    }

    public void InitializeForShell()
    {
        isSelfTestMode = true;
        OnPropertyChanged(nameof(SimulationTabVisibility));
        OnPropertyChanged(nameof(ProductionTabVisibility));
        ShellStatus = $"Shell MVVM listo para flujo QSR con branding {Branding.AppName}.";
        TerminalStatus.Status = "TerminalStatusViewModel listo para leer binding local.";
        TerminalStatus.BindingSummary = "Binding local sera provisto por PosCore.Infrastructure.SQLite.";
        Sales.LoadLocalCatalogSummary();
        SyncStatus.Status = "SyncStatusViewModel listo para estado visual de outbox/pull state.";
        SyncStatus.QueueSummary = "Outbox local disponible desde runtime PosCore.";
        CashShift.Status = "CashShiftViewModel listo para apertura/cierre de caja local.";
        CashShift.ExpectedCashSummary = "Expected cash sera calculado por servicios de aplicacion.";
    }

    public void ApplyBranding(TenantBrandingPackage package)
    {
        Branding.Apply(package);
        ShellStatus = $"Shell MVVM listo para tenant {Branding.TenantName}.";
    }

    public void ExecuteQsrSelfTest()
    {
        InitializeForShell();
        Sales.ExecuteQsrSelfTest();
        CashShift.ExecuteQsrSelfTest(Sales.TotalCents);
        SyncStatus.ExecuteQsrSelfTest();
        ShellStatus = "Flujo WPF QSR validado sobre MVVM.";
    }
}
