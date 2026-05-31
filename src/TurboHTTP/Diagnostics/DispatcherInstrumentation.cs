using System.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Diagnostics;

internal static class DispatcherInstrumentation
{
    public static void RecordRequestDispatched(in TagList tags)
    {
        if (Metrics.PipelineInFlight().Enabled)
        {
            Metrics.PipelineInFlight().Add(1, in tags);
        }
    }

    public static void RecordRequestCompleted(in TagList tags)
    {
        if (Metrics.PipelineInFlight().Enabled)
        {
            Metrics.PipelineInFlight().Add(-1, in tags);
        }
    }

    public static void RecordRequestPending(in TagList tags, int delta)
    {
        if (Metrics.PipelinePending().Enabled)
        {
            Metrics.PipelinePending().Add(delta, in tags);
        }
    }

    public static void RecordHandlerTimeout(in TagList tags)
    {
        if (Metrics.HandlerTimeouts().Enabled)
        {
            Metrics.HandlerTimeouts().Add(1, in tags);
        }
    }

    public static void RecordDrainStateChange(int delta)
    {
        if (Metrics.DrainActive().Enabled)
        {
            Metrics.DrainActive().Add(delta);
        }
    }
}
