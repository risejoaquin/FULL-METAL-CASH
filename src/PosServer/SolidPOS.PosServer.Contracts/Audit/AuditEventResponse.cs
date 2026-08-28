using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Audit;

public sealed record AuditEventResponse(
    Guid Id,
    Guid TenantId,
    Guid? ActorUserId,
    Guid? TerminalId,
    string Action,
    string EntityType,
    Guid? EntityId,
    JsonElement? BeforeData,
    JsonElement? AfterData,
    string? IpAddress,
    string? UserAgent,
    string? TraceId,
    DateTimeOffset OccurredAt);
