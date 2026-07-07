using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Regression spec for the HTTP/3 response-body-drain teardown safety. When a stream is torn
/// down (RST / connection cleanup) while a body drain read is in flight, the pump must handle
/// orphan cleanup correctly — no use-after-free, no double-dispose.
/// </summary>
public sealed class Http3ResponseBodyDrainTeardownSpec
{

    private static void SendRequest(Http3ServerSessionManager sm, long streamId)
    {
        var data = ServerOptionDefaults.BuildHttp3Request("GET", "/");
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = WireBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    /// <summary>
    /// Builds a response whose body is a started-but-empty pipe: OnResponse takes the streamed
    /// drain path and the first ReadAsync pends asynchronously (a genuine in-flight read).
    /// </summary>
    private static async Task<IFeatureCollection> StartedEmptyResponse(long streamId)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        await bodyFeature.StartAsync();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Stream_reset_while_response_read_in_flight_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(ServerOptionDefaults.Http3(), ops);

        const long streamId = 0;
        SendRequest(sm, streamId);

        sm.OnResponse(await StartedEmptyResponse(streamId));

        // Tear the stream down while the drain read is still in flight.
        // The pump marks the slot as orphaned; no crash, no use-after-free.
        sm.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);

        // When the in-flight read finally completes, the pump cleans up the orphaned slot.
        var ex = Record.Exception(() =>
            sm.OnBodyMessage(new BodyReadComplete<long>(streamId, 0)));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Connection_cleanup_while_response_read_in_flight_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(ServerOptionDefaults.Http3(), ops);

        const long streamId = 0;
        SendRequest(sm, streamId);
        sm.OnResponse(await StartedEmptyResponse(streamId));

        // Connection teardown with a read in flight must not crash.
        // The pump cancels via the connection CTS and handles orphan cleanup.
        var ex = Record.Exception(() => sm.Cleanup());

        Assert.Null(ex);
    }
}
