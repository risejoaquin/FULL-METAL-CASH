namespace SolidPOS.PosServer.Application.Receipts;

public sealed record DigitalReceiptRecord(
    Guid Id,
    Guid TenantId,
    Guid SaleId,
    string ReceiptNumber,
    string PublicUrl,
    string Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSentAt,
    string? LastSentEmail,
    int SendCount);
