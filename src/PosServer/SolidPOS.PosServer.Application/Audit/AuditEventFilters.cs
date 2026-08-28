namespace SolidPOS.PosServer.Application.Audit;

public sealed record AuditEventFilters(
    string? Action,
    string? EntityType,
    Guid? EntityId,
    Guid? ActorUserId,
    Guid? TerminalId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);
