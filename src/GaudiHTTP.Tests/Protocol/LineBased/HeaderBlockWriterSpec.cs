using System.Text;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.LineBased;

public sealed class HeaderBlockWriterSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-5")]
    public void Writer_should_emit_all_headers_with_terminating_crlf()
    {
        var headers = new HeaderCollection
        {
            { "Host", "example.com" },
            { "User-Agent", "test/1.0" }
        };

        var buf = new byte[128];
        var writer = SpanWriter.Create(buf);
        HeaderBlockWriter.Write(ref writer, headers);

        const string expected = "Host: example.com\r\nUser-Agent: test/1.0\r\n\r\n";
        Assert.Equal(expected, Encoding.ASCII.GetString(buf, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    public void Writer_should_emit_empty_block_with_just_crlf()
    {
        var headers = new HeaderCollection();
        var buf = new byte[16];
        var writer = SpanWriter.Create(buf);
        HeaderBlockWriter.Write(ref writer, headers);
        Assert.Equal("\r\n", Encoding.ASCII.GetString(buf, 0, writer.BytesWritten));
    }

    [Fact(Timeout = 5000)]
    public void Writer_should_throw_when_buf_too_small()
    {
        var headers = new HeaderCollection { { "X-Long", new string('A', 100) } };

        Assert.Throws<ArgumentException>(() =>
        {
            var buf = new byte[10];
            var writer = SpanWriter.Create(buf);
            HeaderBlockWriter.Write(ref writer, headers);
        });
    }
}