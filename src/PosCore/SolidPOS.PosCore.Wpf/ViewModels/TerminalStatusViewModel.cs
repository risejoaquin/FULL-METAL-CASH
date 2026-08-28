namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class TerminalStatusViewModel : ViewModelBase
{
    private string status = "Terminal local pendiente de inicialización.";
    private string bindingSummary = "Sin binding cargado.";

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public string BindingSummary
    {
        get => bindingSummary;
        set => SetProperty(ref bindingSummary, value);
    }
}
