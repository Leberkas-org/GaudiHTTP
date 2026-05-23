using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ProtocolOptionsDefaultsSpec
{
    [Fact(Timeout = 5000)]
    public void Http1ServerOptions_should_have_correct_defaults()
    {
        var opts = new Http1ServerOptions();

        Assert.Equal(8192, opts.MaxRequestLineLength);
        Assert.Equal(8192, opts.MaxRequestTargetLength);
        Assert.Equal(16, opts.MaxPipelinedRequests);
        Assert.Equal(4096, opts.MaxChunkExtensionLength);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.BodyReadTimeout);
        Assert.Equal(30_000_000, opts.MaxRequestBodySize);
        Assert.Equal(32 * 1024, opts.MaxHeaderListSize);
        Assert.Null(opts.KeepAliveTimeout);
        Assert.Null(opts.RequestHeadersTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Http2ServerOptions_should_have_correct_defaults()
    {
        var opts = new Http2ServerOptions();

        Assert.Equal(100, opts.MaxConcurrentStreams);
        Assert.Equal(1 * 1024 * 1024, opts.InitialConnectionWindowSize);
        Assert.Equal(768 * 1024, opts.InitialStreamWindowSize);
        Assert.Equal(16 * 1024, opts.MaxFrameSize);
        Assert.Equal(32 * 1024, opts.MaxHeaderListSize);
        Assert.Equal(4 * 1024, opts.HeaderTableSize);
        Assert.Equal(30_000_000, opts.MaxRequestBodySize);
        Assert.Equal(64 * 1024, opts.MaxResponseBufferSize);
        Assert.Equal(TimeSpan.FromSeconds(130), opts.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.RequestHeadersTimeout);
        Assert.Equal(240, opts.MinRequestBodyDataRate);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.MinRequestBodyDataRateGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void Http3ServerOptions_should_have_correct_defaults()
    {
        var opts = new Http3ServerOptions();

        Assert.Equal(100, opts.MaxConcurrentStreams);
        Assert.Equal(32 * 1024, opts.MaxHeaderListSize);
        Assert.Equal(0, opts.QpackMaxTableCapacity);
        Assert.False(opts.EnableWebTransport);
        Assert.Equal(30_000_000, opts.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(130), opts.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.RequestHeadersTimeout);
        Assert.Equal(240, opts.MinRequestBodyDataRate);
        Assert.Equal(TimeSpan.FromSeconds(5), opts.MinRequestBodyDataRateGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_should_nest_protocol_options()
    {
        var opts = new TurboServerOptions();

        Assert.NotNull(opts.Http1);
        Assert.NotNull(opts.Http2);
        Assert.NotNull(opts.Http3);
        Assert.Equal(65536, opts.BodyBufferThreshold);
        Assert.Equal(16384, opts.ResponseBodyChunkSize);
        Assert.Equal(TimeSpan.FromSeconds(130), opts.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.RequestHeadersTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.GracefulShutdownTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.BodyConsumptionTimeout);
        Assert.Equal(0, opts.MaxConcurrentConnections);
    }
}
