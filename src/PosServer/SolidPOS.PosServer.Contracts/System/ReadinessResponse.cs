namespace SolidPOS.PosServer.Contracts.System;

public sealed record ReadinessResponse(
    string Status,
    string Database,
    DateTimeOffset ServerTimeUtc,
    string? Detail = null,
    string? ErrorCode = null,
    IReadOnlyCollection<string>? MissingTables = null,
    string? ConnectionStringSource = null,
    long? DatabaseLatencyMs = null,
    int? SchemaVersion = null,
    string? SyncContract = null,
    string? SchemaCompatibility = null,
    string? SyncReadiness = null,
    string? StorageReadiness = null,
    IReadOnlyCollection<ReadinessDependencyResponse>? Dependencies = null);

public sealed record ReadinessDependencyResponse(
    string Name,
    string Status,
    long? LatencyMs = null,
    string? Detail = null,
    string? ErrorCode = null);
