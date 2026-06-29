using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3CriticalStreamsSpec
{
    private static Http3ConnectionOptions DefaultConnectionOptions() => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: 240,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MinResponseDataRate: 240,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5)),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        MaxResponseBufferSize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
    };

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        return new Http3ServerSessionManager(DefaultConnectionOptions(), ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_control_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.ControlId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_encoder_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackEncoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void PreStart_should_open_qpack_decoder_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var opens = ops.Outbound.OfType<OpenStream>().ToList();
        Assert.Contains(opens, o => o.StreamId.Value == CriticalStreamId.QpackDecoderId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_settings_on_control_stream()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var settingsData = ops.Outbound.OfType<MultiplexedData>()
            .Where(m => m.StreamId == CriticalStreamId.ControlId)
            .ToList();

        Assert.NotEmpty(settingsData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    public void PreStart_should_advertise_max_field_section_size()
    {
        // Regression: the server enforced MaxFieldSectionSize locally but never advertised
        // SETTINGS_MAX_FIELD_SECTION_SIZE, so peers could not pre-trim oversized header blocks.
        // DefaultConnectionOptions maps MaxHeaderListSize (32 KiB) → decoder MaxFieldSectionSize.
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        var settings = ExtractControlSettings(ops);
        Assert.NotNull(settings);
        Assert.Equal(32L * 1024, settings!.MaxFieldSectionSize);
    }

    private static Settings? ExtractControlSettings(FakeServerOps ops)
    {
        var control = ops.Outbound.OfType<MultiplexedData>()
            .FirstOrDefault(m => m.StreamId == CriticalStreamId.ControlId);
        if (control is null)
        {
            return null;
        }

        var span = control.Buffer.Span;
        QuicVarInt.TryDecode(span, out _, out var streamTypeBytes);
        span = span[streamTypeBytes..];
        if (!QuicVarInt.TryDecode(span, out _, out var frameTypeBytes))
        {
            return null;
        }

        span = span[frameTypeBytes..];
        if (!QuicVarInt.TryDecode(span, out var payloadLength, out var payloadLenBytes))
        {
            return null;
        }

        span = span[payloadLenBytes..];
        return Settings.Deserialize(span[..(int)payloadLength]);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_all_streams_and_reset()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        sm.PreStart();

        // Cleanup should not crash and should reset stream count
        sm.Cleanup();

        Assert.Equal(0, sm.ActiveStreamCount);
    }
}