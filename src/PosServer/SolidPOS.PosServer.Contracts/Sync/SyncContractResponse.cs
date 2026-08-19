namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncContractResponse(
    int CurrentSchemaVersion,
    int MaxEventsPerBatch,
    int MaxPullLimit,
    IReadOnlyCollection<string> SupportedInboundEventTypes,
    IReadOnlyCollection<string> SupportedStatuses,
    IReadOnlyCollection<string> ConflictResolutionStrategies);
