using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.TestSupport;

internal static class TestClientOptions
{
    internal static GaudiClientOptions Create(
        int? maxPipelineDepth = null,
        int? http1MaxReconnectAttempts = null,
        TimeSpan? http1ReconnectInitialBackoff = null,
        int? maxConcurrentStreams = null,
        int? http2MaxReconnectAttempts = null,
        TimeSpan? http2ReconnectInitialBackoff = null,
        int? initialStreamWindowSize = null,
        int? maxFrameSize = null,
        TimeSpan? keepAlivePingDelay = null,
        TimeSpan? keepAlivePingTimeout = null)
    {
        var options = new GaudiClientOptions();

        if (maxPipelineDepth.HasValue)
        {
            options.Http1.MaxPipelineDepth = maxPipelineDepth.Value;
        }

        if (http1MaxReconnectAttempts.HasValue)
        {
            options.Http1.MaxReconnectAttempts = http1MaxReconnectAttempts.Value;
        }

        if (http1ReconnectInitialBackoff.HasValue)
        {
            options.Http1.ReconnectInitialBackoff = http1ReconnectInitialBackoff.Value;
        }

        if (maxConcurrentStreams.HasValue)
        {
            options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        }

        if (http2MaxReconnectAttempts.HasValue)
        {
            options.Http2.MaxReconnectAttempts = http2MaxReconnectAttempts.Value;
        }

        if (http2ReconnectInitialBackoff.HasValue)
        {
            options.Http2.ReconnectInitialBackoff = http2ReconnectInitialBackoff.Value;
        }

        if (initialStreamWindowSize.HasValue)
        {
            options.Http2.InitialStreamWindowSize = initialStreamWindowSize.Value;
        }

        if (maxFrameSize.HasValue)
        {
            options.Http2.MaxFrameSize = maxFrameSize.Value;
        }

        if (keepAlivePingDelay.HasValue)
        {
            options.Http2.KeepAlivePingDelay = keepAlivePingDelay.Value;
        }

        if (keepAlivePingTimeout.HasValue)
        {
            options.Http2.KeepAlivePingTimeout = keepAlivePingTimeout.Value;
        }

        return options;
    }
}
