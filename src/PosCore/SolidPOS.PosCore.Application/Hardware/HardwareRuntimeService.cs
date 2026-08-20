using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Hardware;

public sealed class HardwareRuntimeService
{
    private readonly ILocalPosRepository _repository;
    private readonly IReceiptPrinter _receiptPrinter;
    private readonly ICashDrawerDevice _cashDrawer;
    private readonly IBarcodeScannerDevice _barcodeScanner;
    private readonly IPaymentTerminalDevice _paymentTerminal;
    private readonly IClock _clock;

    public HardwareRuntimeService(
        ILocalPosRepository repository,
        IReceiptPrinter receiptPrinter,
        ICashDrawerDevice cashDrawer,
        IBarcodeScannerDevice barcodeScanner,
        IPaymentTerminalDevice paymentTerminal,
        IClock clock)
    {
        _repository = repository;
        _receiptPrinter = receiptPrinter;
        _cashDrawer = cashDrawer;
        _barcodeScanner = barcodeScanner;
        _paymentTerminal = paymentTerminal;
        _clock = clock;
    }

    public async Task<LocalReceiptPrintJob> QueueReceiptAsync(Guid saleId, Guid receiptId, string receiptNumber, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber)) throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Receipt content is required.", nameof(content));

        TerminalBinding binding = await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before queueing receipt print jobs.");

        var job = new LocalReceiptPrintJob(
            Guid.NewGuid(),
            binding.TenantId,
            binding.StoreId,
            binding.TerminalId,
            saleId,
            receiptId,
            receiptNumber,
            content,
            LocalPrintJobStatuses.Pending,
            _clock.UtcNow,
            null,
            null,
            0);

        await _repository.SaveReceiptPrintJobAsync(job, cancellationToken).ConfigureAwait(false);
        await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), binding.TenantId, binding.StoreId, binding.TerminalId, LocalHardwareDeviceTypes.ReceiptPrinter, "print_job.queued", $"Receipt print job queued: {receiptNumber}", _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return job;
    }

    public async Task<LocalReceiptPrintJob?> ProcessNextReceiptPrintJobAsync(CancellationToken cancellationToken = default)
    {
        LocalReceiptPrintJob? job = await _repository.GetNextPendingReceiptPrintJobAsync(cancellationToken).ConfigureAwait(false);
        if (job is null) return null;

        try
        {
            await _receiptPrinter.PrintAsync(job, cancellationToken).ConfigureAwait(false);
            await _repository.MarkReceiptPrintJobPrintedAsync(job.Id, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
            await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), job.TenantId, job.StoreId, job.TerminalId, LocalHardwareDeviceTypes.ReceiptPrinter, "print_job.printed", $"Receipt printed: {job.ReceiptNumber}", _clock.UtcNow), cancellationToken).ConfigureAwait(false);
            return job with { Status = LocalPrintJobStatuses.Printed, PrintedAtUtc = _clock.UtcNow, Attempts = job.Attempts + 1 };
        }
        catch (Exception ex)
        {
            await _repository.MarkReceiptPrintJobFailedAsync(job.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), job.TenantId, job.StoreId, job.TerminalId, LocalHardwareDeviceTypes.ReceiptPrinter, "print_job.failed", ex.Message, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LocalCashDrawerCommandResult> OpenCashDrawerAsync(string reason, CancellationToken cancellationToken = default)
    {
        TerminalBinding binding = await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before opening the cash drawer.");

        LocalCashDrawerCommandResult result = await _cashDrawer.OpenAsync(reason, cancellationToken).ConfigureAwait(false);
        await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), binding.TenantId, binding.StoreId, binding.TerminalId, LocalHardwareDeviceTypes.CashDrawer, "cash_drawer.open", result.Message, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<LocalBarcodeScanResult> ScanBarcodeAsync(CancellationToken cancellationToken = default)
    {
        TerminalBinding binding = await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before scanning barcodes.");

        LocalBarcodeScanResult result = await _barcodeScanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), binding.TenantId, binding.StoreId, binding.TerminalId, LocalHardwareDeviceTypes.BarcodeScanner, "barcode.scanned", result.Barcode, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<LocalPaymentTerminalResult> AuthorizePaymentAsync(int amountCents, string currency, CancellationToken cancellationToken = default)
    {
        if (amountCents <= 0) throw new ArgumentOutOfRangeException(nameof(amountCents), "Payment amount must be greater than zero.");
        TerminalBinding binding = await _repository.GetTerminalBindingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Terminal must be bound before authorizing payments.");

        LocalPaymentTerminalResult result = await _paymentTerminal.AuthorizeAsync(amountCents, currency, cancellationToken).ConfigureAwait(false);
        await _repository.SaveHardwareEventAsync(new LocalHardwareEvent(Guid.NewGuid(), binding.TenantId, binding.StoreId, binding.TerminalId, LocalHardwareDeviceTypes.PaymentTerminal, result.Approved ? "payment.approved" : "payment.declined", result.Message, _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        return result;
    }
}
