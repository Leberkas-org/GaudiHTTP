using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

/// <summary>
/// TurboServerLimits.MaxRequestBufferSize must actually bound the TCP read-pipe input buffer.
/// Previously it was declared and documented but never read by any production code (a dead knob).
/// It now drives the TCP InputPauseThreshold as a server-wide default; an explicit per-listener
/// TransportBufferOptions.InputPauseThreshold still takes precedence.
/// </summary>
public sealed class MaxRequestBufferSizeSpec
{
    [Fact(Timeout = 5000)]
    public void MaxRequestBufferSize_should_drive_tcp_input_pause_threshold()
    {
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestBufferSize = 256 * 1024
            }
        };
        options.Listen(IPAddress.Loopback, 5061);

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(256 * 1024, tcp.InputPauseThreshold);
        Assert.True(tcp.InputResumeThreshold <= tcp.InputPauseThreshold,
            "Resume threshold must stay at or below the pause threshold.");
    }

    [Fact(Timeout = 5000)]
    public void Explicit_per_listener_input_pause_should_override_max_request_buffer_size()
    {
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestBufferSize = 256 * 1024
            }
        };
        options.Listen(IPAddress.Loopback, 5062, listen =>
        {
            listen.Transport = new TransportBufferOptions { InputPauseThreshold = 2 * 1024 * 1024 };
        });

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(2 * 1024 * 1024, tcp.InputPauseThreshold);
    }

    [Fact(Timeout = 5000)]
    public void Null_max_request_buffer_size_should_fall_back_to_transport_default()
    {
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestBufferSize = null
            }
        };
        options.Listen(IPAddress.Loopback, 5063);

        var binding = Assert.Single(new EndpointResolver().Resolve(options));
        var tcp = Assert.IsType<TcpListenerOptions>(binding.Options);

        Assert.Equal(1024 * 1024, tcp.InputPauseThreshold); // TCP default
    }
}
