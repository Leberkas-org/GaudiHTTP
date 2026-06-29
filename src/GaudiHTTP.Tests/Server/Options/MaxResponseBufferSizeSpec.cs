using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

/// <summary>
/// GaudiServerLimits.MaxResponseBufferSize must actually bound the transport write-pipe output buffer.
/// Previously it was declared and documented but never read by any production code (a dead knob).
/// It now drives the OutputPauseThreshold as a server-wide default; an explicit per-listener
/// TransportBufferOptions.OutputPauseThreshold still takes precedence.
/// </summary>
public sealed class MaxResponseBufferSizeSpec
{
    [Fact(Timeout = 5000)]
    public void MaxResponseBufferSize_should_drive_tcp_output_pause_threshold()
    {
        var options = new GaudiServerOptions
        {
            Limits =
            {
                MaxResponseBufferSize = 8 * 1024
            }
        };
        options.Listen(IPAddress.Loopback, 5071);

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(8 * 1024, tcp.OutputPauseThreshold);
        Assert.True(tcp.OutputResumeThreshold <= tcp.OutputPauseThreshold,
            "Resume threshold must stay at or below the pause threshold.");
    }

    [Fact(Timeout = 5000)]
    public void Explicit_per_listener_output_pause_should_override_max_response_buffer_size()
    {
        var options = new GaudiServerOptions
        {
            Limits =
            {
                MaxResponseBufferSize = 8 * 1024
            }
        };
        options.Listen(IPAddress.Loopback, 5072, listen =>
        {
            listen.Transport = new TransportBufferOptions { OutputPauseThreshold = 128 * 1024 };
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(128 * 1024, tcp.OutputPauseThreshold);
    }

    [Fact(Timeout = 5000)]
    public void Default_max_response_buffer_size_should_match_transport_default()
    {
        var options = new GaudiServerOptions();
        options.Listen(IPAddress.Loopback, 5073);

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(64 * 1024, tcp.OutputPauseThreshold); // default coincides with transport default
    }
}
