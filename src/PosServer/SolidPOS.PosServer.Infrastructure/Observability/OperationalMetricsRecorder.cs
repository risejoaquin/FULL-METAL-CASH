using System.Collections.Concurrent;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class OperationalMetricsRecorder
{
    private readonly ConcurrentDictionary<string, RouteMetricBucket> _routes = new(StringComparer.OrdinalIgnoreCase);

    public void Record(string method, string route, int statusCode, double elapsedMs)
    {
        string key = $"{method.ToUpperInvariant()} {route}";
        RouteMetricBucket bucket = _routes.GetOrAdd(key, _ => new RouteMetricBucket(method.ToUpperInvariant(), route));
        bucket.Record(statusCode, elapsedMs);
    }

    public RequestMetricsSnapshot Snapshot()
    {
        RouteMetricSnapshot[] routes = _routes.Values.Select(static x => x.Snapshot()).ToArray();
        long total = routes.Sum(static x => x.Count);
        long failed = routes.Sum(static x => x.FailedCount);
        double average = total == 0 ? 0 : routes.Sum(static x => x.AverageLatencyMs * x.Count) / total;
        double p95 = routes.Length == 0 ? 0 : routes.Max(static x => x.P95LatencyMs);

        return new RequestMetricsSnapshot(total, failed, average, p95, routes);
    }

    private sealed class RouteMetricBucket
    {
        private readonly object _sync = new();
        private readonly Queue<double> _samples = new();
        private long _count;
        private long _failedCount;
        private double _totalLatencyMs;

        public RouteMetricBucket(string method, string route)
        {
            Method = method;
            Route = route;
        }

        public string Method { get; }
        public string Route { get; }

        public void Record(int statusCode, double elapsedMs)
        {
            lock (_sync)
            {
                _count++;
                if (statusCode >= 400)
                {
                    _failedCount++;
                }

                _totalLatencyMs += Math.Max(0, elapsedMs);
                _samples.Enqueue(Math.Max(0, elapsedMs));
                while (_samples.Count > 512)
                {
                    _samples.Dequeue();
                }
            }
        }

        public RouteMetricSnapshot Snapshot()
        {
            lock (_sync)
            {
                double[] samples = _samples.OrderBy(static x => x).ToArray();
                double p95 = Percentile(samples, 0.95);
                double average = _count == 0 ? 0 : _totalLatencyMs / _count;
                return new RouteMetricSnapshot(Method, Route, _count, _failedCount, average, p95);
            }
        }

        private static double Percentile(double[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            int index = (int)Math.Ceiling(percentile * values.Length) - 1;
            index = Math.Clamp(index, 0, values.Length - 1);
            return values[index];
        }
    }
}

public sealed record RequestMetricsSnapshot(
    long TotalRequests,
    long FailedRequests,
    double AverageLatencyMs,
    double P95LatencyMs,
    IReadOnlyList<RouteMetricSnapshot> Routes);

public sealed record RouteMetricSnapshot(
    string Method,
    string Route,
    long Count,
    long FailedCount,
    double AverageLatencyMs,
    double P95LatencyMs);
