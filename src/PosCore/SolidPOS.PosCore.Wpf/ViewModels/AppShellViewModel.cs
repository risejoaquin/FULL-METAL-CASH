namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class AppShellViewModel : ViewModelBase
{
    private string shellStatus = "Inicializando shell WPF.";

    public AppShellViewModel(string databasePath)
    {
        DatabasePath = databasePath;
        Login = new LoginViewModel();
        TerminalStatus = new TerminalStatusViewModel();
        Sales = new SalesViewModel();
        SyncStatus = new SyncStatusViewModel();
        CashShift = new CashShiftViewModel();
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

    public void InitializeForShell()
    {
        ShellStatus = "Shell MVVM listo para flujo QSR.";
        TerminalStatus.Status = "TerminalStatusViewModel listo para leer binding local.";
        TerminalStatus.BindingSummary = "Binding local sera provisto por PosCore.Infrastructure.SQLite.";
        Sales.LoadLocalCatalogSummary();
        SyncStatus.Status = "SyncStatusViewModel listo para estado visual de outbox/pull state.";
        SyncStatus.QueueSummary = "Outbox local disponible desde runtime PosCore.";
        CashShift.Status = "CashShiftViewModel listo para apertura/cierre de caja local.";
        CashShift.ExpectedCashSummary = "Expected cash sera calculado por servicios de aplicacion.";
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
