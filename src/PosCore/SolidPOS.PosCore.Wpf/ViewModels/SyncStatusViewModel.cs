using System.Windows.Input;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class SyncStatusViewModel : ViewModelBase
{
    private int pendingOutboxCount = 0;
    private int syncedOutboxCount = 0;
    private int deadLetterCount = 0;
    private string status = "Sync visual pendiente.";
    private string queueSummary = "Outbox local pendiente de lectura.";

    public SyncStatusViewModel()
    {
        MarkPendingSaleCommand = new RelayCommand(MarkPendingSale);
        MarkSaleSyncedCommand = new RelayCommand(MarkSaleSynced);
    }

    public int PendingOutboxCount
    {
        get => pendingOutboxCount;
        set
        {
            if (SetProperty(ref pendingOutboxCount, value))
            {
                UpdateSummary();
            }
        }
    }

    public int SyncedOutboxCount
    {
        get => syncedOutboxCount;
        set
        {
            if (SetProperty(ref syncedOutboxCount, value))
            {
                UpdateSummary();
            }
        }
    }

    public int DeadLetterCount
    {
        get => deadLetterCount;
        set
        {
            if (SetProperty(ref deadLetterCount, value))
            {
                UpdateSummary();
            }
        }
    }

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

    public ICommand MarkPendingSaleCommand { get; }

    public ICommand MarkSaleSyncedCommand { get; }

    public void ExecuteQsrSelfTest()
    {
        MarkPendingSale();
        MarkSaleSynced();
    }

    private void MarkPendingSale()
    {
        PendingOutboxCount += 1;
        Status = "Venta offline encolada visualmente para sync.";
    }

    private void MarkSaleSynced()
    {
        if (PendingOutboxCount > 0)
        {
            PendingOutboxCount -= 1;
        }

        SyncedOutboxCount += 1;
        Status = "Venta offline marcada como sincronizada visualmente.";
    }

    private void UpdateSummary()
    {
        QueueSummary = $"Outbox local visual: pending={PendingOutboxCount}; synced={SyncedOutboxCount}; deadLetter={DeadLetterCount}.";
    }
}
