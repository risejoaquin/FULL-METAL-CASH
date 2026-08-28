using SolidPOS.PosServer.Contracts.Observability;

namespace SolidPOS.PosServer.Application.Observability;

public interface IOperationalMetricsService
{
    Task<OperationalMetricsResponse> GetMetricsAsync(CancellationToken cancellationToken);
}
