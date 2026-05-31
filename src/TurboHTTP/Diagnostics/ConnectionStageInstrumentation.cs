using System.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Diagnostics;

internal static class ConnectionStageInstrumentation
{
    public static void RecordConnectionAccepted(in TagList tags)
    {
        if (Metrics.ActiveConnections().Enabled)
        {
            Metrics.ActiveConnections().Add(1, in tags);
        }
    }

    public static void RecordConnectionRejected(in TagList tags)
    {
        if (Metrics.RejectedConnections().Enabled)
        {
            Metrics.RejectedConnections().Add(1, in tags);
        }
    }

    public static void RecordConnectionCompleted(in TagList tags, long startTimestamp)
    {
        if (Metrics.ActiveConnections().Enabled)
        {
            Metrics.ActiveConnections().Add(-1, in tags);
        }

        if (Metrics.ConnectionDuration().Enabled && startTimestamp > 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            Metrics.ConnectionDuration().Record(elapsed.TotalSeconds, in tags);
        }
    }

    public static void RecordProtocolNegotiation(in TagList tags, long startTimestamp, Version protocolVersion)
    {
        if (Metrics.ProtocolNegotiationDuration().Enabled)
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            Metrics.ProtocolNegotiationDuration().Record(elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("network.protocol.version",
                    TurboHttpInstrumentationExtensions.FormatProtocolVersion(protocolVersion)));
        }
    }

    public static Activity? StartConnectionActivity(in TagList tags, string host, int port, string transport)
    {
        return Tracing.StartConnectionActivity(host, port, transport);
    }

    public static void StopConnectionActivity(Activity? activity, Exception? error)
    {
        if (activity is not null)
        {
            Tracing.StopConnectionActivity(activity, error);
        }
    }

    public static TagList BuildListenerTags(string host, int port, string transport)
    {
        var tags = new TagList();
        TurboServerInstrumentationExtensions.InjectConnectionTags(ref tags, host, port);
        tags.Add("network.transport", transport);
        return tags;
    }
}
