namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class CashShiftViewModel : ViewModelBase
{
    private string status = "Caja local pendiente de apertura.";
    private string expectedCashSummary = "Expected cash: MXN 0.00";

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public string ExpectedCashSummary
    {
        get => expectedCashSummary;
        set => SetProperty(ref expectedCashSummary, value);
    }
}
