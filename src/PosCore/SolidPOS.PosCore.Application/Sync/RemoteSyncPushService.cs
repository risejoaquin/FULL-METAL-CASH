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
    private readonly LocalSyncRetryPolicy _retryPolicy;

    public RemoteSyncPushService(ILocalPosRepository repository, IRemoteSyncClient remoteClient, IClock clock, LocalSyncRetryPolicy? retryPolicy = null)
    {
        _repository = repository;
        _remoteClient = remoteClient;
        _clock = clock;
        _retryPolicy = retryPolicy ?? new LocalSyncRetryPolicy();
    }

    public async Task<RemoteSyncPushResult?> PushPendingAsync(int limit, Guid batchId, string terminalAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(terminalAccessToken)) throw new InvalidOperationException("Terminal access token is required for remote sync push.");
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");

        var pending = await _repository.GetPendingOutboxEventsAsync(limit, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0) return null;

        var batch = LocalOutboxBatchPlanner.CreateBatch(batchId, pending);
        var request = RemoteSyncPushMapper.Map(batch);
        RemoteSyncPushResult result;
        try
        {
            result = await _remoteClient.PushAsync(request, terminalAccessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            foreach (var pendingEvent in batch.Events)
            {
                await ApplyRetryDecisionAsync(pendingEvent, _retryPolicy.EvaluateException(pendingEvent, exception), cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        // Fail closed on malformed or ambiguous acknowledgements. The remote server
        // may have committed a side effect before a malformed response; keeping
        // local events pending allows a later idempotent replay by event ID.
        ValidateResult(request, result);

        if (result.FailedCount == 0)
        {
            await _repository.MarkOutboxSyncedAsync(result.AcknowledgedEventIds, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var failed in batch.Events.Where(item => !result.AcknowledgedEventIds.Contains(item.Id)))
            {
                var decision = _retryPolicy.EvaluateRemoteFailure(failed, "Remote sync push returned rejected or failed events.");
                await ApplyRetryDecisionAsync(failed, decision, cancellationToken).ConfigureAwait(false);
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

    private static void ValidateResult(RemoteSyncPushRequest request, RemoteSyncPushResult result)
    {
        if (result.BatchId != request.BatchId || result.AttemptedCount != request.Events.Count)
        {
            throw new InvalidOperationException("Remote sync response batch identity/count mismatch.");
        }

        if (result.AcceptedCount < 0 || result.DuplicateCount < 0 || result.FailedCount < 0 ||
            (long)result.AcceptedCount + result.DuplicateCount + result.FailedCount != request.Events.Count)
        {
            throw new InvalidOperationException("Remote sync response counters do not reconcile.");
        }

        if (result.AcknowledgedEventIds is null)
        {
            throw new InvalidOperationException("Remote sync response is missing per-event acknowledgements.");
        }

        var requestedIds = request.Events.Select(item => item.EventId).ToHashSet();
        var acknowledgedIds = result.AcknowledgedEventIds.ToHashSet();
        if (requestedIds.Count != request.Events.Count ||
            acknowledgedIds.Count != result.AcknowledgedEventIds.Count ||
            acknowledgedIds.Count != (long)result.AcceptedCount + result.DuplicateCount ||
            !acknowledgedIds.IsSubsetOf(requestedIds))
        {
            throw new InvalidOperationException("Remote sync response has missing, duplicate, or foreign acknowledgements.");
        }
    }

    private Task ApplyRetryDecisionAsync(LocalOutboxEvent outboxEvent, LocalSyncRetryDecision decision, CancellationToken cancellationToken)
    {
        if (decision.ShouldDeadLetter)
        {
            return _repository.MarkOutboxDeadLetterAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken);
        }

        if (decision.ShouldRetry)
        {
            return _repository.MarkOutboxRetryPendingAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken);
        }

        return _repository.MarkOutboxFailedAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken);
    }
}
