using System.Buffers;
using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Regression spec for the HTTP/3 response-body-drain use-after-free (mirror of the H2 fix in
/// commit 19b83c57). When a stream is torn down (RST / connection cleanup) while a pooled
/// drain buffer still has a ReadAsync in flight, the buffer MUST NOT be returned to the shared
/// pool — otherwise a concurrent stream re-rents it and the in-flight read corrupts that stream.
/// Disposal must be deferred until the read completes.
/// </summary>
public sealed class Http3ResponseBodyDrainTeardownSpec
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

    private static byte[] BuildRequest(string method, string path)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":path", path),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var headerBlock = tableSync.Encoder.Encode(headers);
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static void SendRequest(Http3ServerSessionManager sm, long streamId)
    {
        var data = BuildRequest("GET", "/");
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    /// <summary>
    /// Builds a response whose body is a started-but-empty pipe: OnResponse takes the streamed
    /// drain path and the first ReadAsync pends asynchronously (a genuine in-flight read).
    /// </summary>
    private static async Task<IFeatureCollection> StartedEmptyResponse(long streamId)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        await bodyFeature.StartAsync();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private static IDictionary ActiveBodyBuffers(Http3ServerSessionManager sm)
        => (IDictionary)typeof(Http3ServerSessionManager)
            .GetField("_activeBodyBuffers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(sm)!;

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Stream_reset_while_response_read_in_flight_should_defer_buffer_disposal()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);

        const long streamId = 0;
        SendRequest(sm, streamId);

        sm.OnResponse(await StartedEmptyResponse(streamId));

        var buffers = ActiveBodyBuffers(sm);
        Assert.True(buffers.Contains(streamId),
            "Drain buffer should be rented while the response body read is in flight.");

        // Tear the stream down while the drain read is still in flight. The buffer must NOT be
        // returned to the pool yet — a concurrent stream could re-rent and corrupt it.
        sm.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);

        Assert.True(buffers.Contains(streamId),
            "Buffer was returned to the pool while a ReadAsync was still in flight (UAF).");

        // When the in-flight read finally completes, the deferred buffer is released.
        sm.OnBodyMessage(new StreamBodyReadComplete(streamId, 0));

        Assert.False(buffers.Contains(streamId),
            "Deferred drain buffer should be released once the in-flight read completes.");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Connection_cleanup_while_response_read_in_flight_should_not_dispose_buffer()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);

        const long streamId = 0;
        SendRequest(sm, streamId);
        sm.OnResponse(await StartedEmptyResponse(streamId));

        var buffers = ActiveBodyBuffers(sm);
        var owner = (IMemoryOwner<byte>)buffers[streamId]!;

        // Connection teardown with a read in flight must abandon (not pool-return) the buffer:
        // returning it mid-read lets a re-rent overwrite the array the read is still writing to.
        sm.Cleanup();

        // The shared-pool rental throws ObjectDisposedException on Memory access once returned.
        // Pre-fix Cleanup disposed it mid-read; post-fix it is abandoned to GC, still readable.
        var ex = Record.Exception(() => _ = owner.Memory.Length);
        Assert.Null(ex);
    }
}
