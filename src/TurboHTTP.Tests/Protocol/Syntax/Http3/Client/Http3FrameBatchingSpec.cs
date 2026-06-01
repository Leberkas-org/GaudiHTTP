using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Tests.TestSupport;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3FrameBatchingSpec
{
    [Fact(Timeout = 5000)]
    public void EncodeRequest_should_emit_single_MultiplexedData_for_headeronly_request()
    {
        var ops = new FakeClientOps();
        var encoderOpts = ClientOptionDefaults.Http3Encoder();
        var decoderOpts = ClientOptionDefaults.Http3Decoder();
        var clientOpts = new TurboClientOptions { DangerousAcceptAnyServerCertificate = true };

        var session = new Http3ClientSessionManager(encoderOpts, decoderOpts, clientOpts, ops);
        session.OnTransportConnected();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/test");
        session.EncodeRequest(request);

        var requestDataItems = ops.Outbound.OfType<MultiplexedData>().Where(md => md.StreamId >= 0).ToList();
        Assert.Single(requestDataItems);
    }
}