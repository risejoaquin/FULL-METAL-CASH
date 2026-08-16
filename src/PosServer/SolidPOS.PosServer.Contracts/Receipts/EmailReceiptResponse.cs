namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record EmailReceiptResponse(
    Guid DigitalReceiptId,
    Guid SaleId,
    string ReceiptNumber,
    string RecipientEmail,
    string Status,
    DateTimeOffset QueuedAt,
    string Message);
