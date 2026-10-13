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
        if (batchId == Guid.Empty) throw new ArgumentException("Batch ID is required.", nameof(batchId));

        // SQLite implements an opt-in durable claim. Fake repositories keep the
        // existing API for compatibility with established unit tests.
        var claimedRepository = _repository as ILeasedLocalSyncRepository;
        var pending = claimedRepository is null
            ? await _repository.GetPendingOutboxEventsAsync(limit, cancellationToken).ConfigureAwait(false)
            : await claimedRepository.ClaimPendingOutboxEventsAsync(limit, batchId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0) return null;

        try
        {
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
                    var decision = _retryPolicy.EvaluateException(pendingEvent, exception);
                    await ApplyRetryDecisionAsync(pendingEvent, decision, claimedRepository, batchId, cancellationToken).ConfigureAwait(false);
                }
                throw;
            }

            // Malformed ACKs must not change claimed event state. Finally releases
            // the claim for safe replay using the same idempotent event IDs.
            ValidateResult(request, result);

            if (result.FailedCount > 0)
            {
                var acknowledgedIds = result.AcknowledgedEventIds.ToHashSet();
                foreach (var failed in batch.Events.Where(item => !acknowledgedIds.Contains(item.Id)))
                {
                    var decision = _retryPolicy.EvaluateRemoteFailure(failed, "Remote sync push returned rejected or failed events.");
                    await ApplyRetryDecisionAsync(failed, decision, claimedRepository, batchId, cancellationToken).ConfigureAwait(false);
                }
            }

            if (result.AcknowledgedEventIds.Count > 0)
            {
                DateTimeOffset acknowledgedAt = _clock.UtcNow;
                var acknowledgements = result.AcknowledgedEventIds.Select(eventId => new LocalSyncAcknowledgement(
                    Guid.NewGuid(),
                    result.BatchId,
                    eventId,
                    result.FailedCount == 0 ? "acknowledged" : "partial_acknowledged",
                    result.RawResponseJson,
                    acknowledgedAt)).ToArray();

                if (claimedRepository is null)
                {
                    await _repository.CommitSyncAcknowledgementsAsync(
                        acknowledgements, acknowledgedAt, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await claimedRepository.CommitClaimedSyncAcknowledgementsAsync(
                        batchId, acknowledgements, acknowledgedAt, cancellationToken).ConfigureAwait(false);
                }
            }
            return result;
        }
        finally
        {
            // Release only outstanding rows still owned by this batch. Never
            // requeue another worker's claim, even after a crash/recovery race.
            if (claimedRepository is not null)
                await claimedRepository.ReleaseClaimAsync(batchId, CancellationToken.None).ConfigureAwait(false);
        }
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

    private Task ApplyRetryDecisionAsync(
        LocalOutboxEvent outboxEvent, LocalSyncRetryDecision decision,
        ILeasedLocalSyncRepository? claimedRepository, Guid batchId, CancellationToken cancellationToken)
    {
        LocalOutboxStatus status = decision.ShouldDeadLetter
            ? LocalOutboxStatus.DeadLetter
            : decision.ShouldRetry ? LocalOutboxStatus.RetryPending : LocalOutboxStatus.Failed;

        if (claimedRepository is not null)
            return claimedRepository.MarkClaimedOutboxFailureAsync(
                batchId, outboxEvent.Id, status, decision.DiagnosticReason, cancellationToken);

        return status switch
        {
            LocalOutboxStatus.DeadLetter =>
                _repository.MarkOutboxDeadLetterAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken),
            LocalOutboxStatus.RetryPending =>
                _repository.MarkOutboxRetryPendingAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken),
            _ => _repository.MarkOutboxFailedAsync(outboxEvent.Id, decision.DiagnosticReason, cancellationToken)
        };
    }
}
