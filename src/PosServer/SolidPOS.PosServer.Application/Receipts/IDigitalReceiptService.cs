using SolidPOS.PosServer.Contracts.Receipts;

namespace SolidPOS.PosServer.Application.Receipts;

public interface IDigitalReceiptService
{
    Task<DigitalReceiptResponse?> IssueAsync(
        Guid saleId,
        IssueDigitalReceiptRequest? request,
        string publicBaseUrl,
        CancellationToken cancellationToken);

    Task<DigitalReceiptResponse?> GetBySaleIdAsync(
        Guid saleId,
        CancellationToken cancellationToken);

    Task<DigitalReceiptResponse?> GetPublicAsync(
        string publicToken,
        CancellationToken cancellationToken);

    Task<EmailReceiptResponse?> EmailStubAsync(
        Guid saleId,
        EmailReceiptRequest request,
        string publicBaseUrl,
        CancellationToken cancellationToken);
}
