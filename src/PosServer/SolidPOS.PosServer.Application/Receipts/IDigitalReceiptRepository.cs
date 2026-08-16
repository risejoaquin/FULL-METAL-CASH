namespace SolidPOS.PosServer.Application.Receipts;

public interface IDigitalReceiptRepository
{
    Task<DigitalReceiptRecord?> IssueAsync(
        Guid tenantId,
        Guid saleId,
        string receiptNumber,
        string publicTokenHash,
        string publicUrl,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);

    Task<DigitalReceiptRecord?> GetBySaleIdAsync(
        Guid tenantId,
        Guid saleId,
        CancellationToken cancellationToken);

    Task<DigitalReceiptRecord?> GetByPublicTokenHashAsync(
        string publicTokenHash,
        CancellationToken cancellationToken);

    Task<DigitalReceiptRecord?> MarkEmailStubSentAsync(
        Guid tenantId,
        Guid saleId,
        string recipientEmail,
        CancellationToken cancellationToken);
}
