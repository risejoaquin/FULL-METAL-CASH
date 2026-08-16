namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record IssueDigitalReceiptRequest(
    DateTimeOffset? ExpiresAt = null);
