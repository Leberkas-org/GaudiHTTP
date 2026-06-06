using System.Net;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic.Listener;
using Servus.Akka.Transport.Tcp.Listener;

namespace TurboHTTP.Server;

/// <summary>
/// Top-level configuration for <see cref="TurboServer"/>. Controls server-wide limits, timeouts,
/// protocol-specific sub-options, and endpoint bindings. Configure via
/// <see cref="TurboServerWebHostBuilderExtensions.UseTurboHttp"/> or DI options.
/// </summary>
public sealed class TurboServerOptions
{
    /// <summary>Gets the server-wide limits applied to all connections and requests.</summary>
    public TurboServerLimits Limits { get; } = new();

    /// <summary>Gets or sets the time allowed for in-flight requests to complete during shutdown. Default is 30 seconds.</summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum time a request handler may run before it is cancelled. Default is 30 seconds.</summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets additional time granted to handlers after the handler timeout fires to clean up. Default is 5 seconds.</summary>
    public TimeSpan HandlerGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the timeout for the application to consume the complete request body. Default is 30 seconds.</summary>
    public TimeSpan BodyConsumptionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the size of each chunk written to the response body stream. Default is 16 KiB.</summary>
    public int ResponseBodyChunkSize { get; set; } = 16 * 1024;

    ///<summary>Gets or sets the maximum number of consecutive outbound frames coalesced into a single transport write. Higher values reduce syscalls at the cost of latency. Default is 8.</summary>
    public int MaxOutboundCoalesceCount { get; set; } = 8;

    /// <summary>Gets or sets whether response headers may use Huffman compression (HPACK/QPACK). Disabling mitigates CRIME/BREACH-style side-channel attacks. Default is true.</summary>
    public bool AllowResponseHeaderCompression { get; set; } = true;

    /// <summary>Gets the HTTP/1.x-specific configuration options.</summary>
    public Http1ServerOptions Http1 { get; } = new();

    /// <summary>Gets the HTTP/2-specific configuration options.</summary>
    public Http2ServerOptions Http2 { get; } = new();

    /// <summary>Gets the HTTP/3-specific configuration options.</summary>
    public Http3ServerOptions Http3 { get; } = new();

    /// <summary>Gets the collection of pre-built listener bindings added via <see cref="Bind(TcpListenerOptions)"/> or similar overloads.</summary>
    public IList<ListenerBinding> Endpoints { get; } = new List<ListenerBinding>();

    /// <summary>Adds a TCP listener binding for the given <paramref name="options"/>.</summary>
    public void Bind(TcpListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new TcpListenerFactory() });
    }

    /// <summary>Adds a QUIC listener binding for the given <paramref name="options"/>.</summary>
    public void Bind(QuicListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new QuicListenerFactory() });
    }

    /// <summary>Adds a cleartext TCP listener on the specified <paramref name="host"/> and <paramref name="port"/>.</summary>
    public void BindTcp(string host, ushort port) => Bind(new TcpListenerOptions { Host = host, Port = port });

    internal IList<TurboListenOptions> ListenOptions { get; } = new List<TurboListenOptions>();
    internal Action<TurboHttpsOptions>? HttpsDefaultsCallback { get; private set; }
    internal Action<TurboListenOptions>? EndpointDefaultsCallback { get; private set; }

    /// <summary>Gets the collection of URL strings (e.g. <c>"https://0.0.0.0:443"</c>) resolved to listener bindings at startup.</summary>
    public IList<string> Urls { get; } = new List<string>();

    /// <summary>Registers a callback applied to the <see cref="TurboHttpsOptions"/> of every HTTPS endpoint before it is bound.</summary>
    public void ConfigureHttpsDefaults(Action<TurboHttpsOptions> configure)
    {
        HttpsDefaultsCallback = configure;
    }

    /// <summary>Registers a callback applied to every endpoint's <see cref="TurboListenOptions"/> before it is bound.</summary>
    public void ConfigureEndpointDefaults(Action<TurboListenOptions> configure)
    {
        EndpointDefaultsCallback = configure;
    }

    /// <summary>Adds a listen endpoint on the given <paramref name="address"/> and <paramref name="port"/> with default options.</summary>
    public void Listen(IPAddress address, ushort port)
    {
        var listenOptions = new TurboListenOptions(address, port);
        EndpointDefaultsCallback?.Invoke(listenOptions);
        ListenOptions.Add(listenOptions);
    }

    /// <summary>Adds a listen endpoint on the given <paramref name="address"/> and <paramref name="port"/>, applying <paramref name="configure"/> to the resulting options.</summary>
    public void Listen(IPAddress address, ushort port, Action<TurboListenOptions> configure)
    {
        var listenOptions = new TurboListenOptions(address, port);
        EndpointDefaultsCallback?.Invoke(listenOptions);
        configure(listenOptions);
        ListenOptions.Add(listenOptions);
    }

    /// <summary>Parses <paramref name="url"/> (e.g. <c>"http://0.0.0.0:80"</c>) and adds the resulting listen endpoint.</summary>
    public void Listen(string url)
    {
        try
        {
            var listenOptions = EndpointResolver.ParseUrl(url);
            EndpointDefaultsCallback?.Invoke(listenOptions);
            ListenOptions.Add(listenOptions);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(url), ex);
        }
    }

    /// <summary>Parses <paramref name="url"/> and adds the resulting listen endpoint, applying <paramref name="configure"/> to the options.</summary>
    public void Listen(string url, Action<TurboListenOptions> configure)
    {
        try
        {
            var listenOptions = EndpointResolver.ParseUrl(url);
            EndpointDefaultsCallback?.Invoke(listenOptions);
            configure(listenOptions);
            ListenOptions.Add(listenOptions);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(ex.Message, nameof(url), ex);
        }
    }

    /// <summary>Adds a listen endpoint on the loopback address (<c>127.0.0.1</c>) at <paramref name="port"/>.</summary>
    public void ListenLocalhost(ushort port)
    {
        Listen(IPAddress.Loopback, port);
    }

    /// <summary>Adds a listen endpoint on the loopback address at <paramref name="port"/>, applying <paramref name="configure"/> to the options.</summary>
    public void ListenLocalhost(ushort port, Action<TurboListenOptions> configure)
    {
        Listen(IPAddress.Loopback, port, configure);
    }

    /// <summary>Adds a listen endpoint on all network interfaces (<c>0.0.0.0</c>) at <paramref name="port"/>.</summary>
    public void ListenAnyIP(ushort port)
    {
        Listen(IPAddress.Any, port);
    }

    /// <summary>Adds a listen endpoint on all network interfaces at <paramref name="port"/>, applying <paramref name="configure"/> to the options.</summary>
    public void ListenAnyIP(ushort port, Action<TurboListenOptions> configure)
    {
        Listen(IPAddress.Any, port, configure);
    }

    /// <summary>Adds a listener binding for the given <paramref name="options"/> using the supplied <paramref name="factory"/>.</summary>
    public void Bind(ListenerOptions options, IListenerFactory factory)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = factory });
    }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Limits.MaxRequestBodySize);
        ArgumentOutOfRangeException.ThrowIfLessThan(Limits.MaxRequestHeadersTotalSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Limits.MaxRequestHeaderCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Limits.KeepAliveTimeout, TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(HandlerTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(HandlerGracePeriod, TimeSpan.Zero);

        ArgumentOutOfRangeException.ThrowIfLessThan(Http2.MaxConcurrentStreams, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Http2.MaxFrameSize, 16 * 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Http2.MaxFrameSize, 16 * 1024 * 1024 - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Http2.InitialStreamWindowSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(Http2.InitialConnectionWindowSize, 1);

        ArgumentOutOfRangeException.ThrowIfLessThan(Http3.MaxConcurrentStreams, 1);
    }
}