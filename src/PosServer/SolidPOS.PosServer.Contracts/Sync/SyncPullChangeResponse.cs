using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPullChangeResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Operation,
    long EntityVersion,
    DateTimeOffset ChangedAt,
    JsonElement Payload,
    Guid? StoreId,
    Guid? SourceTerminalId);
