using System.Windows.Input;
using SolidPOS.PosCore.Application.Cash;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class CashShiftViewModel : ViewModelBase
{
    private int openingAmountCents = 10000;
    private int cashSalesCents = 0;
    private int cashInCents = 0;
    private int cashOutCents = 0;
    private int expectedCashCents = 10000;
    private string status = "Caja local pendiente de apertura.";
    private string expectedCashSummary = "Expected cash pendiente.";

    public CashShiftViewModel()
    {
        OpenLocalShiftCommand = new RelayCommand(OpenLocalShift);
        AddCashInCommand = new RelayCommand(AddCashIn);
        AddCashOutCommand = new RelayCommand(AddCashOut);
        CloseLocalShiftCommand = new RelayCommand(CloseLocalShift);
    }

    public int OpeningAmountCents
    {
        get => openingAmountCents;
        set
        {
            if (SetProperty(ref openingAmountCents, value))
            {
                RecalculateExpectedCash();
            }
        }
    }

    public int CashSalesCents
    {
        get => cashSalesCents;
        set
        {
            if (SetProperty(ref cashSalesCents, value))
            {
                RecalculateExpectedCash();
            }
        }
    }

    public int CashInCents
    {
        get => cashInCents;
        set
        {
            if (SetProperty(ref cashInCents, value))
            {
                RecalculateExpectedCash();
            }
        }
    }

    public int CashOutCents
    {
        get => cashOutCents;
        set
        {
            if (SetProperty(ref cashOutCents, value))
            {
                RecalculateExpectedCash();
            }
        }
    }

    public int ExpectedCashCents
    {
        get => expectedCashCents;
        private set => SetProperty(ref expectedCashCents, value);
    }

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

    public ICommand OpenLocalShiftCommand { get; }

    public ICommand AddCashInCommand { get; }

    public ICommand AddCashOutCommand { get; }

    public ICommand CloseLocalShiftCommand { get; }

    public void RegisterCashSale(int amountCents)
    {
        CashSalesCents += amountCents;
        Status = "Venta en efectivo agregada a caja local.";
    }

    public void ExecuteQsrSelfTest(int saleTotalCents)
    {
        OpenLocalShift();
        RegisterCashSale(saleTotalCents);
        CloseLocalShift();
    }

    private void OpenLocalShift()
    {
        Status = "Caja local abierta desde flujo WPF QSR.";
        RecalculateExpectedCash();
    }

    private void AddCashIn()
    {
        CashInCents += 1000;
        Status = "Cash in local capturado desde WPF.";
    }

    private void AddCashOut()
    {
        CashOutCents += 500;
        Status = "Cash out local capturado desde WPF.";
    }

    private void CloseLocalShift()
    {
        Status = "Caja local cerrada visualmente. Persistencia real usa PosCore runtime.";
        RecalculateExpectedCash();
    }

    private void RecalculateExpectedCash()
    {
        ExpectedCashCents = LocalCashCalculator.CalculateExpectedCashCents(OpeningAmountCents, CashSalesCents, CashInCents, CashOutCents);
        ExpectedCashSummary = $"Expected cash local: opening={OpeningAmountCents}; cashSales={CashSalesCents}; cashIn={CashInCents}; cashOut={CashOutCents}; expected={ExpectedCashCents}.";
    }
}
