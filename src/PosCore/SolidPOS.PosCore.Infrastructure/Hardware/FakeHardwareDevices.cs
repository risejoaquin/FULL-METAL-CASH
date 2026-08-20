using SolidPOS.PosCore.Application.Hardware;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.Hardware;

public sealed class FakeReceiptPrinter : IReceiptPrinter
{
    public List<Guid> PrintedJobIds { get; } = new();

    public Task PrintAsync(LocalReceiptPrintJob job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.Content)) throw new InvalidOperationException("Receipt content is empty.");
        PrintedJobIds.Add(job.Id);
        return Task.CompletedTask;
    }
}

public sealed class FakeCashDrawerDevice : ICashDrawerDevice
{
    public Task<LocalCashDrawerCommandResult> OpenAsync(string reason, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LocalCashDrawerCommandResult(true, Guid.NewGuid().ToString("N"), $"Fake cash drawer opened. reason={reason}"));
    }
}

public sealed class FakeBarcodeScannerDevice : IBarcodeScannerDevice
{
    private readonly string _barcode;

    public FakeBarcodeScannerDevice(string barcode = "QSR-AMERICANO")
    {
        _barcode = barcode;
    }

    public Task<LocalBarcodeScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LocalBarcodeScanResult(_barcode, DateTimeOffset.UtcNow));
    }
}

public sealed class FakePaymentTerminalDevice : IPaymentTerminalDevice
{
    public Task<LocalPaymentTerminalResult> AuthorizeAsync(int amountCents, string currency, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LocalPaymentTerminalResult(true, $"FAKE-{Guid.NewGuid():N}"[..16], amountCents, $"Fake payment approved. amountCents={amountCents}; currency={currency}"));
    }
}
