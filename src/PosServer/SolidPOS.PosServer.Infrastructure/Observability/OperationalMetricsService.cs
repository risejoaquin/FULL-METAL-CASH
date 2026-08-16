using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Observability;
using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class OperationalMetricsService : IOperationalMetricsService
{
    private readonly ITenantContext _tenantContext;
    private readonly IOperationalMetricsRepository _repository;
    private readonly OperationalMetricsRecorder _recorder;
    private readonly IClock _clock;

    public OperationalMetricsService(
        ITenantContext tenantContext,
        IOperationalMetricsRepository repository,
        OperationalMetricsRecorder recorder,
        IClock clock)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _recorder = recorder;
        _clock = clock;
    }

    public async Task<OperationalMetricsResponse> GetMetricsAsync(CancellationToken cancellationToken)
    {
        RequestMetricsSnapshot snapshot = _recorder.Snapshot();
        RequestMetricsResponse requestMetrics = new(
            snapshot.TotalRequests,
            snapshot.FailedRequests,
            Math.Round(snapshot.AverageLatencyMs, 2),
            Math.Round(snapshot.P95LatencyMs, 2),
            snapshot.Routes
                .OrderByDescending(static x => x.Count)
                .Take(20)
                .Select(static x => new RequestRouteMetricResponse(
                    x.Method,
                    x.Route,
                    x.Count,
                    x.FailedCount,
                    Math.Round(x.AverageLatencyMs, 2),
                    Math.Round(x.P95LatencyMs, 2)))
                .ToArray());

        Guid tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("Tenant context is required.");
        RouteMetricSnapshot? salesRoute = snapshot.Routes.FirstOrDefault(static x =>
            x.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            x.Route.Contains("/api/v1/sales", StringComparison.OrdinalIgnoreCase));

        DatabaseMetricsResponse database = await _repository.GetDatabaseMetricsAsync(cancellationToken);
        SyncMetricsResponse sync = await _repository.GetSyncMetricsAsync(tenantId, cancellationToken);
        SalesLatencyMetricsResponse sales = await _repository.GetSalesMetricsAsync(
            tenantId,
            salesRoute?.AverageLatencyMs ?? 0,
            salesRoute?.P95LatencyMs ?? 0,
            cancellationToken);
        PaymentMetricsResponse payments = await _repository.GetPaymentMetricsAsync(tenantId, cancellationToken);
        InventoryRiskMetricsResponse inventory = await _repository.GetInventoryMetricsAsync(tenantId, cancellationToken);
        AuditTrailMetricsResponse audit = await _repository.GetAuditMetricsAsync(tenantId, cancellationToken);

        return new OperationalMetricsResponse(_clock.UtcNow, database, requestMetrics, sync, sales, payments, inventory, audit);
    }
}
