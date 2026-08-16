using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Application.Observability;

public interface IOperationalMetricsRepository
{
    Task<DatabaseMetricsResponse> GetDatabaseMetricsAsync(CancellationToken cancellationToken);
    Task<SyncMetricsResponse> GetSyncMetricsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<SalesLatencyMetricsResponse> GetSalesMetricsAsync(Guid tenantId, double apiAverageLatencyMs, double apiP95LatencyMs, CancellationToken cancellationToken);
    Task<PaymentMetricsResponse> GetPaymentMetricsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<InventoryRiskMetricsResponse> GetInventoryMetricsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<AuditTrailMetricsResponse> GetAuditMetricsAsync(Guid tenantId, CancellationToken cancellationToken);
}
