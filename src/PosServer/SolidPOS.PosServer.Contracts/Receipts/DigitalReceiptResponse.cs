namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record DigitalReceiptResponse(
    Guid Id,
    Guid TenantId,
    Guid SaleId,
    string ReceiptNumber,
    string PublicToken,
    string PublicUrl,
    string Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSentAt,
    string? LastSentEmail,
    int SendCount,
    ReceiptResponse Receipt);
