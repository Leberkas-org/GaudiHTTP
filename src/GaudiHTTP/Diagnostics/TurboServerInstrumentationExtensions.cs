using System.Diagnostics;
using Servus.Diagnostics;
using GaudiHTTP.Protocol;

namespace GaudiHTTP.Diagnostics;

internal static class TurboServerInstrumentationExtensions
{
    private static readonly HashSet<string> StandardMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Get, WellKnownHeaders.Head, WellKnownHeaders.Post, WellKnownHeaders.Put, WellKnownHeaders.Delete, WellKnownHeaders.Connect, WellKnownHeaders.Options, WellKnownHeaders.Trace, WellKnownHeaders.Patch
    };

    public static bool IsServerTracingActive(this ServusTrace trace)
    {
        return trace.Source.HasListeners()
            || Servus.Senf.Metrics.ActiveConnections().Enabled
            || Servus.Senf.Metrics.ServerActiveRequests().Enabled
            || Servus.Senf.Metrics.ServerRequestDuration().Enabled;
    }

    public static Activity? StartConnectionActivity(this ServusTrace trace, string serverAddress, int serverPort, string networkTransport)
    {
        if (!trace.Source.HasListeners())
        {
            return null;
        }

        var activity = trace.Source.StartActivity(
            "GaudiHTTP.Connection",
            ActivityKind.Server);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", serverAddress);
        activity.SetTag("server.port", serverPort);
        activity.SetTag("network.transport", networkTransport);

        return activity;
    }

    public static void StopConnectionActivity(this ServusTrace _, Activity activity, Exception? error)
    {
        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Message);
            activity.SetTag("error.type", error.GetType().FullName);
        }

        activity.Stop();
    }

    public static Activity? StartRequestActivity(this ServusTrace trace, string method, string path, string scheme,
        string? traceParent = null, string? traceState = null)
    {
        if (!trace.Source.HasListeners())
        {
            return null;
        }

        ActivityContext parentContext = default;
        if (traceParent is not null && ActivityContext.TryParse(traceParent, traceState, out var parsed))
        {
            parentContext = parsed;
        }

        var activity = parentContext != default
            ? trace.Source.StartActivity("GaudiHTTP.ServerRequest", ActivityKind.Server, parentContext)
            : trace.Source.StartActivity("GaudiHTTP.ServerRequest", ActivityKind.Server);

        if (activity is null)
        {
            return null;
        }

        var normalizedMethod = NormalizeMethod(method);
        activity.SetTag("http.request.method", normalizedMethod);
        if (normalizedMethod == "_OTHER")
        {
            activity.SetTag("http.request.method_original", method);
        }

        activity.SetTag("url.path", path);
        activity.SetTag("url.scheme", scheme);

        return activity;
    }

    public static void SetServerResponse(this ServusTrace _, Activity activity, int statusCode)
    {
        activity.SetTag("http.response.status_code", statusCode);

        if (statusCode >= 400)
        {
            activity.SetTag("error.type", statusCode.ToString());
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    public static void SetServerError(this ServusTrace _, Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }

    public static void AddBackpressureEvent(this ServusTrace _, Activity activity, int inflight, int max)
    {
        activity.AddEvent(new ActivityEvent("turbo.backpressure",
            tags: new ActivityTagsCollection
            {
                { "turbo.pipeline.inflight", inflight },
                { "turbo.pipeline.max", max }
            }));
    }

    public static void InjectConnectionTags(ref TagList tags, string serverAddress, int serverPort)
    {
        tags.Add("server.address", serverAddress);
        tags.Add("server.port", serverPort);
    }

    private static string NormalizeMethod(string method)
    {
        return StandardMethods.Contains(method) ? method.ToUpperInvariant() : "_OTHER";
    }
}
