namespace SolidPOS.PosServer.Contracts.Audit;

public sealed record AuditEventPageResponse(
    IReadOnlyCollection<AuditEventResponse> Items,
    AuditEventPageMetaResponse Meta);
