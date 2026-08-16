namespace SolidPOS.PosServer.Contracts.Audit;

public sealed record AuditEventPageMetaResponse(
    int Page,
    int PageSize,
    long Total);
