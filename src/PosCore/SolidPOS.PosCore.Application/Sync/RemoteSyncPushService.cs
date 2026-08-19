using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public interface IRemoteSyncClient
{
    Task<RemoteSyncPushResult> PushAsync(RemoteSyncPushRequest request, string terminalAccessToken, CancellationToken cancellationToken = default);
}

public sealed class RemoteSyncPushService
{
    private readonly ILocalPosRepository _repository;
    private readonly IRemoteSyncClient _remoteClient;
    private readonly IClock _clock;

    public RemoteSyncPushService(ILocalPosRepository repository, IRemoteSyncClient remoteClient, IClock clock)
    {
        _repository = repository;
        _remoteClient = remoteClient;
        _clock = clock;
    }

    public async Task<RemoteSyncPushResult?> PushPendingAsync(int limit, Guid batchId, string terminalAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(terminalAccessToken)) throw new InvalidOperationException("Terminal access token is required for remote sync push.");
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        var pending = await _repository.GetPendingOutboxEventsAsync(limit, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0) return null;

        var batch = LocalOutboxBatchPlanner.CreateBatch(batchId, pending);
        var request = RemoteSyncPushMapper.Map(batch);
        var result = await _remoteClient.PushAsync(request, terminalAccessToken, cancellationToken).ConfigureAwait(false);

        if (result.FailedCount == 0)
        {
            await _repository.MarkOutboxSyncedAsync(result.AcknowledgedEventIds, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var failed in batch.Events.Where(item => !result.AcknowledgedEventIds.Contains(item.Id)))
            {
                await _repository.MarkOutboxFailedAsync(failed.Id, "Remote sync push returned failed events.", cancellationToken).ConfigureAwait(false);
            }

            if (result.AcknowledgedEventIds.Count > 0)
            {
                await _repository.MarkOutboxSyncedAsync(result.AcknowledgedEventIds, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
            }
        }

        await _repository.SaveSyncAcknowledgementsAsync(
            result.AcknowledgedEventIds.Select(eventId => new LocalSyncAcknowledgement(
                Guid.NewGuid(),
                result.BatchId,
                eventId,
                result.FailedCount == 0 ? "acknowledged" : "partial_acknowledged",
                result.RawResponseJson,
                _clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

        return result;
    }
}
