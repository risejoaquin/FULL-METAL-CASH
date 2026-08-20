using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public sealed record RemoteSyncPullResponse(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    DateTimeOffset ServerTime,
    string? PreviousCursor,
    string NextCursor,
    bool HasMore,
    IReadOnlyList<RemoteSyncPullChange> Changes,
    string RawResponseJson);

public sealed record RemoteSyncPullChange(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Operation,
    long EntityVersion,
    DateTimeOffset ChangedAt,
    JsonElement Payload,
    Guid? StoreId,
    Guid? SourceTerminalId);

public sealed record LocalSyncPullApplyResult(
    string? PreviousCursor,
    string NextCursor,
    int ReceivedChangeCount,
    int AppliedChangeCount,
    int SkippedDuplicateCount,
    bool HasMore);

public interface IRemoteSyncPullClient
{
    Task<RemoteSyncPullResponse> PullAsync(string? cursor, int limit, string terminalAccessToken, CancellationToken cancellationToken = default);
}

public sealed class LocalSyncPullService
{
    private readonly ILocalPosRepository _repository;
    private readonly IRemoteSyncPullClient _remoteClient;
    private readonly IClock _clock;

    public LocalSyncPullService(ILocalPosRepository repository, IRemoteSyncPullClient remoteClient, IClock clock)
    {
        _repository = repository;
        _remoteClient = remoteClient;
        _clock = clock;
    }

    public async Task<LocalSyncPullApplyResult> PullAndApplyAsync(int limit, string terminalAccessToken, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
        if (string.IsNullOrWhiteSpace(terminalAccessToken)) throw new InvalidOperationException("Terminal access token is required for sync pull.");

        LocalSyncPullState state = await _repository.GetSyncPullStateAsync(cancellationToken).ConfigureAwait(false);
        RemoteSyncPullResponse response = await _remoteClient.PullAsync(state.Cursor, limit, terminalAccessToken, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LocalAppliedSyncChange> applied = response.Changes.Select(change => new LocalAppliedSyncChange(
            change.Id,
            change.EntityType,
            change.EntityId,
            change.Operation,
            change.EntityVersion,
            change.ChangedAt,
            change.Payload.GetRawText(),
            _clock.UtcNow)).ToArray();

        int appliedCount = await _repository.ApplySyncPullChangesAsync(applied, response.NextCursor, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new LocalSyncPullApplyResult(
            response.PreviousCursor,
            response.NextCursor,
            response.Changes.Count,
            appliedCount,
            response.Changes.Count - appliedCount,
            response.HasMore);
    }
}
