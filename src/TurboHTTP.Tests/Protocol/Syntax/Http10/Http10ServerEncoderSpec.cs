using System.Net;
using System.Text;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http10.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10;

public sealed class Http10ServerEncoderSpec : TestKit
{
    private static Http10ServerEncoder MakeEncoder(bool withDate = true) =>
        new(Http10ServerEncoderOptions.Default with { WriteDateHeader = withDate },
            Http10Profile.Default);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Encode_should_return_zero_and_send_body_to_stageActor()
    {
        var probe = CreateTestProbe();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hi"u8.ToArray()),
        };
        var buf = new byte[256];

        var written = MakeEncoder(withDate: false).Encode(buf, response, probe.Ref);

        Assert.Equal(0, written);
        probe.ExpectMsg<OutboundBodyChunk>(TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void EncodeDeferred_should_emit_status_line_and_body()
    {
        var probe = CreateTestProbe();
        var encoder = MakeEncoder(withDate: false);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hi"u8.ToArray()),
        };
        var buf = new byte[256];
        encoder.Encode(buf, response, probe.Ref);

        var chunk = probe.ExpectMsg<OutboundBodyChunk>(TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);

        var deferredBuf = new byte[256];
        var written = encoder.EncodeDeferred(deferredBuf, response, chunk.Owner.Memory.Span[..chunk.Length]);
        chunk.Owner.Dispose();

        var text = Encoding.ASCII.GetString(deferredBuf, 0, written);

        Assert.StartsWith("HTTP/1.0 200 OK\r\n", text);
        Assert.Contains("Content-Length: 2\r\n", text);
        Assert.EndsWith("hi", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void EncodeDeferred_should_inject_Date_when_WriteDateHeader_true()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        Assert.Contains("Date: ", Encoding.ASCII.GetString(buf, 0, written));
    }

    [Fact(Timeout = 5000)]
    public void EncodeDeferred_should_omit_Date_when_WriteDateHeader_false()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, response, ReadOnlySpan<byte>.Empty);
        Assert.DoesNotContain("Date:", Encoding.ASCII.GetString(buf, 0, written));
    }
}