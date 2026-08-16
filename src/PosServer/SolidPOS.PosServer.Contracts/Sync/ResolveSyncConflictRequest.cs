using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record ResolveSyncConflictRequest(
    string ResolutionStrategy,
    JsonElement? ResolvedPayload,
    string? ResolutionNote);
