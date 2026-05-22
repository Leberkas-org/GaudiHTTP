using System.Text;
using Akka.TestKit.Xunit;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerEncoderSpec : TestKit
{
    private static Http10ServerEncoder MakeEncoder(bool withDate = true) =>
        new(Http10ServerEncoderOptions.Default with { WriteDateHeader = withDate });

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Encode_should_always_return_zero()
    {
        var probe = CreateTestProbe();
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];

        var written = MakeEncoder(withDate: false).Encode(buf, ctx, probe.Ref);

        Assert.Equal(0, written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void EncodeDeferred_should_emit_status_line_and_body()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var body = "hi"u8.ToArray();

        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, body);

        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.StartsWith("HTTP/1.0 200 OK\r\n", text);
        Assert.Contains("Content-Length: 2\r\n", text);
        Assert.EndsWith("hi", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void EncodeDeferred_should_inject_Date_when_WriteDateHeader_true()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        Assert.Contains("Date: ", Encoding.ASCII.GetString(buf, 0, written));
    }

    [Fact(Timeout = 5000)]
    public void EncodeDeferred_should_omit_Date_when_WriteDateHeader_false()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        Assert.DoesNotContain("Date:", Encoding.ASCII.GetString(buf, 0, written));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public void EncodeDeferred_should_include_content_length_zero_for_empty_200()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: false).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.Contains("Content-Length: 0", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.3")]
    public void EncodeDeferred_should_use_rfc1123_date_format_in_gmt()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        var dateIdx = text.IndexOf("Date: ", StringComparison.Ordinal);
        Assert.True(dateIdx >= 0);
        var dateEnd = text.IndexOf("\r\n", dateIdx, StringComparison.Ordinal);
        var dateValue = text[(dateIdx + 6)..dateEnd];
        Assert.EndsWith("GMT", dateValue);
        Assert.Contains(",", dateValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-10.6")]
    public void EncodeDeferred_should_include_date_header_by_default()
    {
        var ctx = ServerTestContext.CreateResponse(200);
        var buf = new byte[256];
        var written = MakeEncoder(withDate: true).EncodeDeferred(buf, ctx, ReadOnlySpan<byte>.Empty);
        var text = Encoding.ASCII.GetString(buf, 0, written);

        Assert.Contains("Date:", text);
    }
}