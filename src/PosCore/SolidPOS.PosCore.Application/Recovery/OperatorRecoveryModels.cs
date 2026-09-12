namespace SolidPOS.PosCore.Application.Recovery;

public enum OperatorFailureKind
{
    Retryable = 0,
    NonRetryable = 1,
    Offline = 2,
    Hardware = 3,
    Validation = 4,
    AuthenticationAuthorization = 5,
    Conflict = 6,
    Unknown = 7
}

public enum OperatorRecoveryAction
{
    Retry = 0,
    ContinueOffline = 1,
    RetryPrint = 2,
    RetryDrawer = 3,
    Reconnect = 4,
    Dismiss = 5
}

public enum PosCoreConnectivityState
{
    Online = 0,
    Offline = 1,
    Reconnecting = 2
}

public sealed record OperatorFailureContext(
    string Operation,
    Guid? LocalSaleId = null,
    Guid? LocalPaymentId = null,
    Guid? OutboxEventId = null,
    string? DeviceType = null,
    string? CorrelationId = null);

public sealed record OperatorRecoveryResult(
    OperatorFailureKind Kind,
    string OperatorMessage,
    IReadOnlyList<OperatorRecoveryAction> Actions,
    string TechnicalSummary,
    string Operation,
    Guid? LocalSaleId,
    Guid? LocalPaymentId,
    Guid? OutboxEventId,
    string? DeviceType,
    string? CorrelationId)
{
    public bool CanRetry => Actions.Contains(OperatorRecoveryAction.Retry);
    public bool CanContinueOffline => Actions.Contains(OperatorRecoveryAction.ContinueOffline);
    public bool CanRetryPrint => Actions.Contains(OperatorRecoveryAction.RetryPrint);
    public bool CanRetryDrawer => Actions.Contains(OperatorRecoveryAction.RetryDrawer);
}
