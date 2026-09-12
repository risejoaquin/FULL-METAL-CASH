using System.Net.Http;
using SolidPOS.PosCore.Application.Recovery;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class OperatorRecoveryServiceTests
{
    [Fact]
    public void Describe_turns_technical_exception_into_operator_message_and_keeps_diagnostics()
    {
        var service = new OperatorRecoveryService();

        OperatorRecoveryResult result = service.Describe(
            new InvalidOperationException("SQLite constraint failed at table sales"),
            new OperatorFailureContext("sale-create", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CorrelationId: "corr-1"));

        Assert.Equal(OperatorFailureKind.NonRetryable, result.Kind);
        Assert.DoesNotContain("SQLite", result.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("constraint", result.OperatorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InvalidOperationException", result.TechnicalSummary);
        Assert.Contains("SQLite constraint", result.TechnicalSummary);
        Assert.Equal("corr-1", result.CorrelationId);
    }

    [Fact]
    public void Describe_retryable_offline_error_exposes_continue_offline_and_reconnect()
    {
        var service = new OperatorRecoveryService();

        OperatorRecoveryResult result = service.Describe(
            new HttpRequestException("remote temporarily unavailable"),
            new OperatorFailureContext("sync-push"));

        Assert.Equal(OperatorFailureKind.Offline, result.Kind);
        Assert.Contains(OperatorRecoveryAction.ContinueOffline, result.Actions);
        Assert.Contains(OperatorRecoveryAction.Reconnect, result.Actions);
        Assert.DoesNotContain(OperatorRecoveryAction.Retry, result.Actions);
    }

    [Fact]
    public void Describe_non_retryable_validation_error_does_not_expose_unsafe_retry()
    {
        var service = new OperatorRecoveryService();

        OperatorRecoveryResult result = service.Describe(
            new ArgumentException("quantity must be positive"),
            new OperatorFailureContext("sale-create"));

        Assert.Equal(OperatorFailureKind.Validation, result.Kind);
        Assert.DoesNotContain(OperatorRecoveryAction.Retry, result.Actions);
        Assert.Contains(OperatorRecoveryAction.Dismiss, result.Actions);
    }

    [Theory]
    [InlineData(PosCoreConnectivityState.Online, "ONLINE")]
    [InlineData(PosCoreConnectivityState.Offline, "OFFLINE")]
    [InlineData(PosCoreConnectivityState.Reconnecting, "RECONNECTING")]
    public void DescribeConnectivity_returns_operator_visible_state(PosCoreConnectivityState state, string expected)
    {
        var service = new OperatorRecoveryService();

        Assert.Equal(expected, service.DescribeConnectivity(state));
    }

    [Fact]
    public void Describe_printer_failure_is_distinguishable_from_sale_failure()
    {
        var service = new OperatorRecoveryService();

        OperatorRecoveryResult result = service.Describe(
            new IOException("printer unavailable"),
            new OperatorFailureContext("receipt-print", LocalSaleId: Guid.NewGuid(), DeviceType: LocalHardwareDeviceTypes.ReceiptPrinter));

        Assert.Equal(OperatorFailureKind.Hardware, result.Kind);
        Assert.Contains(OperatorRecoveryAction.RetryPrint, result.Actions);
        Assert.DoesNotContain(OperatorRecoveryAction.RetryDrawer, result.Actions);
        Assert.Contains("venta quedo guardada", result.OperatorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_cash_drawer_failure_is_distinguishable_from_payment_failure()
    {
        var service = new OperatorRecoveryService();

        OperatorRecoveryResult result = service.Describe(
            new IOException("drawer jammed"),
            new OperatorFailureContext("cash-drawer", LocalSaleId: Guid.NewGuid(), LocalPaymentId: Guid.NewGuid(), DeviceType: LocalHardwareDeviceTypes.CashDrawer));

        Assert.Equal(OperatorFailureKind.Hardware, result.Kind);
        Assert.Contains(OperatorRecoveryAction.RetryDrawer, result.Actions);
        Assert.DoesNotContain(OperatorRecoveryAction.RetryPrint, result.Actions);
        Assert.Contains("pago quedo registrado", result.OperatorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retry_after_transient_sale_failure_requires_existing_outbox_identity()
    {
        var service = new OperatorRecoveryService();
        Guid localSaleId = Guid.NewGuid();
        Guid outboxEventId = Guid.NewGuid();

        OperatorRecoveryResult result = new(
            OperatorFailureKind.Retryable,
            "No se pudo completar la accion. Puedes intentarlo de nuevo.",
            new[] { OperatorRecoveryAction.Retry },
            "transient",
            "sale-create",
            localSaleId,
            null,
            outboxEventId,
            null,
            "corr-2");

        Assert.True(service.RetryKeepsOriginalSaleIdentity(result));
    }

    [Fact]
    public void Retry_after_payment_uncertainty_requires_existing_payment_identity()
    {
        var service = new OperatorRecoveryService();
        Guid localPaymentId = Guid.NewGuid();
        Guid outboxEventId = Guid.NewGuid();

        OperatorRecoveryResult result = new(
            OperatorFailureKind.Retryable,
            "No se pudo completar la accion. Puedes intentarlo de nuevo.",
            new[] { OperatorRecoveryAction.Retry },
            "payment timeout",
            "payment",
            Guid.NewGuid(),
            localPaymentId,
            outboxEventId,
            null,
            "corr-3");

        Assert.True(service.RetryKeepsOriginalPaymentIdentity(result));
    }
}
