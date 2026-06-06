using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3ConnectionErrorTeardownSpec
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.2")]
    public void Qpack_decode_error_should_request_connection_completion()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);

        const long streamId = 0;
        // HEADERS frame whose QPACK field section indexes a static-table entry far out of range:
        // 2-byte field-section prefix (RIC=0, Base=0) + indexed-static line 0xFF + varint(137) -> index 200.
        var headerBlock = new byte[] { 0x00, 0x00, 0xFF, 0x89, 0x01 };
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));
        var transport = TransportBuffer.Rent(buf.Length);
        buf.CopyTo(transport.FullMemory.Span);
        transport.Length = buf.Length;
        sm.DecodeClientData(new MultiplexedData(transport, streamId));

        Assert.True(sm.ShouldComplete);
    }
}
