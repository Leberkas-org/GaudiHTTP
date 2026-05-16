using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp.Listener;
using Servus.Akka.Transport.Quic.Listener;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboServerOptions
{
    public int MaxConcurrentConnections { get; set; }
    public int MaxConcurrentUpgradedConnections { get; set; }

    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int BodyBufferThreshold { get; set; } = 65536;
    public TimeSpan BodyConsumptionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int ResponseBodyChunkSize { get; set; } = 16384;

    public Http1ServerOptions Http1 { get; } = new();
    public Http2ServerOptions Http2 { get; } = new();
    public Http3ServerOptions Http3 { get; } = new();

    public IList<Func<IServiceProvider, IServerBidiStage>> MiddlewareFactories { get; } = new List<Func<IServiceProvider, IServerBidiStage>>();
    public IList<ListenerBinding> Endpoints { get; } = new List<ListenerBinding>();

    public void Use<T>() where T : IServerBidiStage, new()
    {
        MiddlewareFactories.Add(_ => new T());
    }

    public void Use(Func<IServiceProvider, IServerBidiStage> factory)
    {
        MiddlewareFactories.Add(factory);
    }

    public void Bind(TcpListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new TcpListenerFactory() });
    }

    public void Bind(QuicListenerOptions options)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = new QuicListenerFactory() });
    }

    public void Bind(ListenerOptions options, IListenerFactory factory)
    {
        Endpoints.Add(new ListenerBinding { Options = options, Factory = factory });
    }
}

public sealed class Http1ServerOptions
{
    public int MaxRequestLineLength { get; set; } = 8192;
    public int MaxRequestTargetLength { get; set; } = 8192;
    public int MaxPipelinedRequests { get; set; } = 16;
    public int MaxChunkExtensionLength { get; set; } = 4096;
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class Http2ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialWindowSize { get; set; } = 65535;
    public int MaxFrameSize { get; set; } = 16384;
    public int MaxHeaderListSize { get; set; } = 8192;
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public long MaxResponseBufferSize { get; set; } = 1024 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int MaxHeaderListSize { get; set; } = 8192;
    public bool EnableWebTransport { get; set; }
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
