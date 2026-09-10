using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public static class SolidPosTelemetry
{
    public const string ActivitySourceName = "SolidPOS.PosServer";
    public const string MeterName = "SolidPOS.PosServer";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> RequestCount = Meter.CreateCounter<long>("solidpos.http.server.requests", unit: "{request}");
    public static readonly Counter<long> RequestErrorCount = Meter.CreateCounter<long>("solidpos.http.server.errors", unit: "{error}");
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("solidpos.http.server.duration", unit: "ms");
    public static readonly Counter<long> SyncEventCount = Meter.CreateCounter<long>("solidpos.sync.events", unit: "{event}");
}
