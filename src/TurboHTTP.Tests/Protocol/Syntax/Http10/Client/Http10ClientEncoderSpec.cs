using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10.Client;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientEncoderSpec : TestKit
{
    private static Http10ClientEncoder MakeEncoder() => new();

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Encoder_should_emit_request_line_and_no_body_for_GET()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo");
        request.Headers.TryAddWithoutValidation("User-Agent", "test/1.0");

        var buf = new byte[256];
        var written = MakeEncoder().Encode(buf, request, ActorRefs.Nobody);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith("GET /foo HTTP/1.0\r\n", text);
        Assert.Contains("User-Agent: test/1.0\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Encoder_should_omit_Host_header_on_HTTP10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var buf = new byte[256];
        var written = MakeEncoder().Encode(buf, request, ActorRefs.Nobody);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.DoesNotContain("Host:", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void Encode_should_return_zero_for_request_with_body()
    {
        var probe = CreateTestProbe();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        var buf = new byte[4096];

        var written = MakeEncoder().Encode(buf, request, probe.Ref);

        Assert.Equal(0, written);
        probe.ExpectMsg<OutboundBodyChunk>(TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void EncodeDeferred_should_write_headers_and_body_with_content_length()
    {
        var probe = CreateTestProbe();
        var encoder = MakeEncoder();
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        var buf = new byte[4096];
        encoder.Encode(buf, request, probe.Ref);

        var chunk = probe.ExpectMsg<OutboundBodyChunk>(TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);

        var deferredBuf = new byte[4096];
        var written = encoder.EncodeDeferred(deferredBuf, request, chunk.Owner.Memory.Span[..chunk.Length]);
        chunk.Owner.Dispose();

        var result = Encoding.ASCII.GetString(deferredBuf, 0, written);
        Assert.StartsWith("POST /", result);
        Assert.Contains("Content-Length: 5", result);
        Assert.Contains("hello", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-10.15")]
    public void Encode_should_include_user_agent_when_set()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("User-Agent", "TurboHTTP/1.0");

        var buf = new byte[256];
        var written = MakeEncoder().Encode(buf, request, ActorRefs.Nobody);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.Contains("User-Agent: TurboHTTP/1.0", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-10.13")]
    public void Encode_should_strip_fragment_from_referer()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Referrer = new Uri("http://example.com/page#section");

        var buf = new byte[512];
        var written = MakeEncoder().Encode(buf, request, ActorRefs.Nobody);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        if (text.Contains("Referer:"))
        {
            Assert.DoesNotContain("#section", text);
        }
    }
}