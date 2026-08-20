namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class SyncStatusViewModel : ViewModelBase
{
    private string status = "Sync local pendiente.";
    private string queueSummary = "Outbox: 0 pendientes.";

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public string QueueSummary
    {
        get => queueSummary;
        set => SetProperty(ref queueSummary, value);
    }
}
