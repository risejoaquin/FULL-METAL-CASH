using System.Windows.Input;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class SalesViewModel : ViewModelBase
{
    private string catalogSummary = "Catálogo local pendiente de sincronización.";
    private string cartSummary = "Carrito vacío.";

    public SalesViewModel()
    {
        CreateOfflineSaleCommand = new RelayCommand(() => CartSummary = "Venta offline demo preparada desde UI shell. La persistencia real usa PosCore.Application.");
    }

    public string CatalogSummary
    {
        get => catalogSummary;
        set => SetProperty(ref catalogSummary, value);
    }

    public string CartSummary
    {
        get => cartSummary;
        set => SetProperty(ref cartSummary, value);
    }

    public ICommand CreateOfflineSaleCommand { get; }
}
