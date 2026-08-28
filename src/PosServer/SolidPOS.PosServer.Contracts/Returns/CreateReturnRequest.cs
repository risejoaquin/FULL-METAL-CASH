namespace SolidPOS.PosServer.Contracts.Returns;

public sealed record CreateReturnRequest(
    Guid LocalReturnId,
    Guid SaleId,
    Guid CreatedByUserId,
    string Reason,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<CreateReturnLineRequest> Lines,
    IReadOnlyCollection<CreateReturnRefundRequest> Refunds);
