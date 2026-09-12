using System.Net.Http;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Recovery;

public sealed class OperatorRecoveryService
{
    public OperatorRecoveryResult Describe(Exception exception, OperatorFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        OperatorFailureKind kind = Classify(exception, context);
        IReadOnlyList<OperatorRecoveryAction> actions = ActionsFor(kind, context);

        return new OperatorRecoveryResult(
            kind,
            OperatorMessageFor(kind, context),
            actions,
            $"{exception.GetType().FullName}: {exception.Message}",
            context.Operation,
            context.LocalSaleId,
            context.LocalPaymentId,
            context.OutboxEventId,
            context.DeviceType,
            context.CorrelationId);
    }

    public string DescribeConnectivity(PosCoreConnectivityState state) => state switch
    {
        PosCoreConnectivityState.Online => "ONLINE",
        PosCoreConnectivityState.Offline => "OFFLINE",
        PosCoreConnectivityState.Reconnecting => "RECONNECTING",
        _ => "UNKNOWN"
    };

    public bool RetryKeepsOriginalSaleIdentity(OperatorRecoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.LocalSaleId.HasValue && result.OutboxEventId.HasValue;
    }

    public bool RetryKeepsOriginalPaymentIdentity(OperatorRecoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.LocalPaymentId.HasValue && result.OutboxEventId.HasValue;
    }

    private static OperatorFailureKind Classify(Exception exception, OperatorFailureContext context)
    {
        if (IsHardware(context, LocalHardwareDeviceTypes.ReceiptPrinter) ||
            IsHardware(context, LocalHardwareDeviceTypes.CashDrawer) ||
            IsHardware(context, LocalHardwareDeviceTypes.PaymentTerminal) ||
            IsHardware(context, LocalHardwareDeviceTypes.BarcodeScanner))
        {
            return OperatorFailureKind.Hardware;
        }

        if (exception is TimeoutException || exception is HttpRequestException)
        {
            return OperatorFailureKind.Offline;
        }

        if (exception is UnauthorizedAccessException)
        {
            return OperatorFailureKind.AuthenticationAuthorization;
        }

        if (exception is ArgumentException || exception is FormatException)
        {
            return OperatorFailureKind.Validation;
        }

        if (exception is InvalidOperationException &&
            exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return OperatorFailureKind.Conflict;
        }

        if (exception is InvalidOperationException)
        {
            return OperatorFailureKind.NonRetryable;
        }

        return OperatorFailureKind.Unknown;
    }

    private static IReadOnlyList<OperatorRecoveryAction> ActionsFor(OperatorFailureKind kind, OperatorFailureContext context)
    {
        return kind switch
        {
            OperatorFailureKind.Offline => new[] { OperatorRecoveryAction.ContinueOffline, OperatorRecoveryAction.Reconnect, OperatorRecoveryAction.Dismiss },
            OperatorFailureKind.Retryable => new[] { OperatorRecoveryAction.Retry, OperatorRecoveryAction.Dismiss },
            OperatorFailureKind.Hardware when IsHardware(context, LocalHardwareDeviceTypes.ReceiptPrinter) => new[] { OperatorRecoveryAction.RetryPrint, OperatorRecoveryAction.Dismiss },
            OperatorFailureKind.Hardware when IsHardware(context, LocalHardwareDeviceTypes.CashDrawer) => new[] { OperatorRecoveryAction.RetryDrawer, OperatorRecoveryAction.Dismiss },
            OperatorFailureKind.Hardware => new[] { OperatorRecoveryAction.Retry, OperatorRecoveryAction.Dismiss },
            OperatorFailureKind.Unknown => new[] { OperatorRecoveryAction.Dismiss },
            _ => new[] { OperatorRecoveryAction.Dismiss }
        };
    }

    private static string OperatorMessageFor(OperatorFailureKind kind, OperatorFailureContext context)
    {
        return kind switch
        {
            OperatorFailureKind.Offline => "Sin conexion. Puedes continuar vendiendo offline y sincronizar cuando vuelva la red.",
            OperatorFailureKind.Hardware when IsHardware(context, LocalHardwareDeviceTypes.ReceiptPrinter) => "La venta quedo guardada, pero no se pudo imprimir el recibo. Revisa la impresora e intenta imprimir de nuevo.",
            OperatorFailureKind.Hardware when IsHardware(context, LocalHardwareDeviceTypes.CashDrawer) => "El pago quedo registrado, pero la caja no abrio. Revisa la caja e intenta abrirla de nuevo.",
            OperatorFailureKind.Hardware => "Un dispositivo no respondio. Revisa el equipo e intenta la accion permitida.",
            OperatorFailureKind.Validation => "Revisa los datos de la operacion antes de continuar.",
            OperatorFailureKind.AuthenticationAuthorization => "Tu sesion no permite esta accion. Inicia sesion de nuevo o pide apoyo de un encargado.",
            OperatorFailureKind.Conflict => "Esta operacion ya fue procesada. Actualiza el estado antes de intentar otra accion.",
            OperatorFailureKind.Retryable => "No se pudo completar la accion. Puedes intentarlo de nuevo.",
            OperatorFailureKind.NonRetryable => "No se pudo completar la accion. Revisa el estado antes de continuar.",
            _ => "No se pudo completar la accion. Revisa el estado y pide apoyo si el problema continua."
        };
    }

    private static bool IsHardware(OperatorFailureContext context, string deviceType)
    {
        return string.Equals(context.DeviceType, deviceType, StringComparison.OrdinalIgnoreCase);
    }
}
