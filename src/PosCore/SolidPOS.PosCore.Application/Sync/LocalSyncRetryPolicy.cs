using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Sync;

public enum LocalSyncFailureKind
{
    Recoverable,
    NonRecoverable
}

public sealed record LocalSyncRetryDecision(
    LocalSyncFailureKind FailureKind,
    bool ShouldRetry,
    bool ShouldDeadLetter,
    TimeSpan Delay,
    string DiagnosticReason);

public sealed class LocalSyncRetryPolicy
{
    public const int DefaultMaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; }

    public LocalSyncRetryPolicy(int maxAttempts = DefaultMaxAttempts)
    {
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        MaxAttempts = maxAttempts;
    }

    public LocalSyncRetryDecision EvaluateException(LocalOutboxEvent outboxEvent, Exception exception)
    {
        var recoverable = IsRecoverable(exception);
        if (!recoverable || outboxEvent.Attempts + 1 >= MaxAttempts)
        {
            return new LocalSyncRetryDecision(
                LocalSyncFailureKind.NonRecoverable,
                ShouldRetry: false,
                ShouldDeadLetter: true,
                TimeSpan.Zero,
                $"dead_letter:{SafeReason(exception.Message)}");
        }

        return Retry(outboxEvent, $"retry_pending:{SafeReason(exception.Message)}");
    }

    public LocalSyncRetryDecision EvaluateRemoteFailure(LocalOutboxEvent outboxEvent, string reason)
    {
        if (outboxEvent.Attempts + 1 >= MaxAttempts)
        {
            return new LocalSyncRetryDecision(
                LocalSyncFailureKind.NonRecoverable,
                ShouldRetry: false,
                ShouldDeadLetter: true,
                TimeSpan.Zero,
                $"dead_letter:{SafeReason(reason)}");
        }

        return Retry(outboxEvent, $"retry_pending:{SafeReason(reason)}");
    }

    private static LocalSyncRetryDecision Retry(LocalOutboxEvent outboxEvent, string reason)
    {
        var exponent = Math.Min(outboxEvent.Attempts, 7);
        var backoff = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << exponent));
        if (backoff > MaxDelay)
        {
            backoff = MaxDelay;
        }

        var jitter = TimeSpan.FromMilliseconds(StableJitterMilliseconds(outboxEvent.Id, outboxEvent.Attempts));
        var delay = backoff + jitter;
        return new LocalSyncRetryDecision(LocalSyncFailureKind.Recoverable, true, false, delay, $"{reason}; backoff_ms={(int)delay.TotalMilliseconds}");
    }

    private static bool IsRecoverable(Exception exception)
    {
        if (exception is TimeoutException || exception is HttpRequestException || exception is TaskCanceledException)
        {
            return true;
        }

        return exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("temporarily", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("transient", StringComparison.OrdinalIgnoreCase);
    }

    private static int StableJitterMilliseconds(Guid eventId, int attempts)
    {
        var bytes = eventId.ToByteArray();
        var seed = BitConverter.ToUInt32(bytes, 0) ^ (uint)attempts;
        return 100 + (int)(seed % 400);
    }

    private static string SafeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "unspecified";
        var sanitized = reason.Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length <= 160 ? sanitized : sanitized[..160];
    }
}
