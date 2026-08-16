using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncConflictResponse(
    Guid Id,
    Guid TenantId,
    Guid? TerminalId,
    string EntityType,
    Guid EntityId,
    Guid? LocalEventId,
    long? LocalVersion,
    long? ServerVersion,
    JsonElement LocalPayload,
    JsonElement ServerPayload,
    string? ResolutionStrategy,
    JsonElement? ResolvedPayload,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    string? ResolutionNote);
