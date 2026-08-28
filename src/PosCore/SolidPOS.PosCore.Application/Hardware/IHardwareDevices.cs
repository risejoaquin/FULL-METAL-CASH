using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Hardware;

public interface IReceiptPrinter
{
    Task PrintAsync(LocalReceiptPrintJob job, CancellationToken cancellationToken = default);
}

public interface ICashDrawerDevice
{
    Task<LocalCashDrawerCommandResult> OpenAsync(string reason, CancellationToken cancellationToken = default);
}

public interface IBarcodeScannerDevice
{
    Task<LocalBarcodeScanResult> ScanAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentTerminalDevice
{
    Task<LocalPaymentTerminalResult> AuthorizeAsync(int amountCents, string currency, CancellationToken cancellationToken = default);
}
