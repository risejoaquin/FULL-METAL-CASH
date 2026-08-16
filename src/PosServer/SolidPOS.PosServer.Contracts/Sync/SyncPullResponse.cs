using SolidPOS.PosServer.Contracts.Terminals;

namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record SyncPullResponse(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset ServerTime,
    string? PreviousCursor,
    string NextCursor,
    bool HasMore,
    TerminalRuntimeContextResponse Terminal,
    IReadOnlyCollection<SyncPullChangeResponse> Changes);
