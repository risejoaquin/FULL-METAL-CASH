using System.Windows.Input;
using SolidPOS.PosCore.Application.Cash;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class SalesViewModel : ViewModelBase
{
    private const int DefaultUnitPriceCents = 4500;
    private const string DefaultSku = "QSR-AMERICANO";
    private const string DefaultProductName = "Americano 12oz";

    private string catalogSummary = "Catalogo local pendiente de sincronizacion.";
    private string cartSummary = "Carrito vacio.";
    private string selectedSku = DefaultSku;
    private string selectedProductName = DefaultProductName;
    private int unitPriceCents = DefaultUnitPriceCents;
    private int quantity = 0;
    private int tenderedCents = 5000;
    private int totalCents = 0;
    private int changeCents = 0;
    private string paymentSummary = "Pago pendiente.";
    private string saleStatus = "Venta QSR lista para operar offline desde SQLite.";
    private string receiptStatus = "Recibo pendiente.";
    private string syncVisualStatus = "Sync pendiente.";
    private string printStatus = "Impresion pendiente.";

    public SalesViewModel()
    {
        AddAmericanoCommand = new RelayCommand(AddAmericano);
        IncreaseQuantityCommand = new RelayCommand(IncreaseQuantity, () => quantity > 0);
        ClearCartCommand = new RelayCommand(ClearCart, () => quantity > 0);
        TakeCashPaymentCommand = new RelayCommand(TakeCashPayment, () => quantity > 0 && tenderedCents >= totalCents);
        QueueFakeReceiptCommand = new RelayCommand(QueueFakeReceipt, () => quantity > 0 && changeCents >= 0);
        MarkSyncedCommand = new RelayCommand(MarkSynced, () => quantity > 0);
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

    public string SelectedSku
    {
        get => selectedSku;
        set => SetProperty(ref selectedSku, value);
    }

    public string SelectedProductName
    {
        get => selectedProductName;
        set => SetProperty(ref selectedProductName, value);
    }

    public int UnitPriceCents
    {
        get => unitPriceCents;
        set
        {
            if (SetProperty(ref unitPriceCents, value))
            {
                RecalculateCart();
                RefreshCommandStates();
            }
        }
    }

    public int Quantity
    {
        get => quantity;
        set
        {
            if (SetProperty(ref quantity, value))
            {
                RecalculateCart();
                RefreshCommandStates();
            }
        }
    }

    public int TenderedCents
    {
        get => tenderedCents;
        set
        {
            if (SetProperty(ref tenderedCents, value))
            {
                RecalculatePaymentPreview();
                RefreshCommandStates();
            }
        }
    }

    public int TotalCents
    {
        get => totalCents;
        private set => SetProperty(ref totalCents, value);
    }

    public int ChangeCents
    {
        get => changeCents;
        private set => SetProperty(ref changeCents, value);
    }

    public string PaymentSummary
    {
        get => paymentSummary;
        set => SetProperty(ref paymentSummary, value);
    }

    public string SaleStatus
    {
        get => saleStatus;
        set => SetProperty(ref saleStatus, value);
    }

    public string ReceiptStatus
    {
        get => receiptStatus;
        set => SetProperty(ref receiptStatus, value);
    }

    public string SyncVisualStatus
    {
        get => syncVisualStatus;
        set => SetProperty(ref syncVisualStatus, value);
    }

    public string PrintStatus
    {
        get => printStatus;
        set => SetProperty(ref printStatus, value);
    }

    public RelayCommand AddAmericanoCommand { get; }

    public RelayCommand IncreaseQuantityCommand { get; }

    public RelayCommand ClearCartCommand { get; }

    public RelayCommand TakeCashPaymentCommand { get; }

    public RelayCommand QueueFakeReceiptCommand { get; }

    public RelayCommand MarkSyncedCommand { get; }

    private void RefreshCommandStates()
    {
        IncreaseQuantityCommand.RaiseCanExecuteChanged();
        ClearCartCommand.RaiseCanExecuteChanged();
        TakeCashPaymentCommand.RaiseCanExecuteChanged();
        QueueFakeReceiptCommand.RaiseCanExecuteChanged();
        MarkSyncedCommand.RaiseCanExecuteChanged();
    }

    public void LoadLocalCatalogSummary()
    {
        CatalogSummary = $"Catalogo local QSR disponible: {DefaultSku} - {DefaultProductName} - {DefaultUnitPriceCents} cents.";
    }

    public void ExecuteQsrSelfTest()
    {
        LoadLocalCatalogSummary();
        AddAmericano();
        TakeCashPayment();
        QueueFakeReceipt();
        MarkSynced();
    }

    private void AddAmericano()
    {
        SelectedSku = DefaultSku;
        SelectedProductName = DefaultProductName;
        UnitPriceCents = DefaultUnitPriceCents;
        Quantity = 1;
        SaleStatus = "Producto QSR agregado desde catalogo local.";
        ReceiptStatus = "Recibo listo para encolarse despues del pago.";
        PrintStatus = "Impresion fake pendiente.";
        SyncVisualStatus = "Venta offline pendiente de sync.";
        RefreshCommandStates();
    }

    private void IncreaseQuantity()
    {
        Quantity += 1;
        SaleStatus = $"Cantidad actualizada para {SelectedSku}.";
        RefreshCommandStates();
    }

    private void ClearCart()
    {
        Quantity = 0;
        TotalCents = 0;
        ChangeCents = 0;
        CartSummary = "Carrito vacio.";
        PaymentSummary = "Pago pendiente.";
        SaleStatus = "Venta QSR limpia.";
        ReceiptStatus = "Recibo pendiente.";
        PrintStatus = "Impresion pendiente.";
        SyncVisualStatus = "Sync pendiente.";
        RefreshCommandStates();
    }

    private void TakeCashPayment()
    {
        if (Quantity <= 0)
        {
            throw new InvalidOperationException("No se puede pagar una venta sin productos.");
        }

        ChangeCents = LocalCashCalculator.CalculateChangeCents(TotalCents, TenderedCents);
        PaymentSummary = $"Pago efectivo capturado. totalCents={TotalCents}; tenderedCents={TenderedCents}; changeCents={ChangeCents}.";
        SaleStatus = "Venta offline QSR lista para ser encolada en outbox local.";
        RefreshCommandStates();
    }

    private void QueueFakeReceipt()
    {
        ReceiptStatus = "Recibo fake QSR encolado localmente.";
        PrintStatus = "Print job fake listo para procesarse.";
        RefreshCommandStates();
    }

    private void MarkSynced()
    {
        SyncVisualStatus = "Estado visual de sync actualizado: venta pendiente de push o ya procesada por runtime.";
        RefreshCommandStates();
    }

    private void RecalculateCart()
    {
        TotalCents = Quantity * UnitPriceCents;
        CartSummary = Quantity <= 0
            ? "Carrito vacio."
            : $"Carrito QSR: {Quantity} x {SelectedProductName} ({SelectedSku}) = {TotalCents} cents.";
        RecalculatePaymentPreview();
    }

    private void RecalculatePaymentPreview()
    {
        if (Quantity <= 0)
        {
            PaymentSummary = "Pago pendiente.";
            ChangeCents = 0;
            return;
        }

        if (TenderedCents < TotalCents)
        {
            PaymentSummary = $"Efectivo insuficiente. totalCents={TotalCents}; tenderedCents={TenderedCents}.";
            ChangeCents = 0;
            return;
        }

        ChangeCents = LocalCashCalculator.CalculateChangeCents(TotalCents, TenderedCents);
        PaymentSummary = $"Preview efectivo: totalCents={TotalCents}; tenderedCents={TenderedCents}; changeCents={ChangeCents}.";
    }
}
