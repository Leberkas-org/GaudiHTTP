using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Servus.Akka.Diagnostics;

public static class Extensions
{
    public static MeterProviderBuilder AddServusMetrics(this MeterProviderBuilder builder)
    {
        return builder.AddMeter("Servus.Akka");
    }

    public static TracerProviderBuilder AddServusTracing(this TracerProviderBuilder builder)
    {
        return builder.AddSource("Servus.Akka");
    }
}