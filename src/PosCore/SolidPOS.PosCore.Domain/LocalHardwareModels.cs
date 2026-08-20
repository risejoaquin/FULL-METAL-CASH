namespace SolidPOS.PosCore.Domain;

public static class LocalHardwareDeviceTypes
{
    public const string ReceiptPrinter = "receipt_printer";
    public const string CashDrawer = "cash_drawer";
    public const string BarcodeScanner = "barcode_scanner";
    public const string PaymentTerminal = "payment_terminal";
}

public static class LocalPrintJobStatuses
{
    public const string Pending = "pending";
    public const string Printed = "printed";
    public const string Failed = "failed";
}

public sealed record LocalReceiptPrintJob(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    Guid SaleId,
    Guid ReceiptId,
    string ReceiptNumber,
    string Content,
    string Status,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? PrintedAtUtc,
    string? LastError,
    int Attempts);

public sealed record LocalHardwareEvent(
    Guid Id,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string DeviceType,
    string EventType,
    string Message,
    DateTimeOffset OccurredAtUtc);

public sealed record LocalHardwareSummary(
    int PendingPrintJobs,
    int PrintedPrintJobs,
    int FailedPrintJobs,
    int HardwareEventCount,
    LocalHardwareEvent? LatestEvent);

public sealed record LocalCashDrawerCommandResult(bool Accepted, string CommandId, string Message);
public sealed record LocalBarcodeScanResult(string Barcode, DateTimeOffset ScannedAtUtc);
public sealed record LocalPaymentTerminalResult(bool Approved, string AuthorizationCode, int AmountCents, string Message);
