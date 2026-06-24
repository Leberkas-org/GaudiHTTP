using System.Text;
using Microsoft.Extensions.Primitives;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Server.Context;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerTrailerEncodingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void WriteLastChunk_should_emit_zero_crlf()
    {
        var buffer = new byte[16];

        var written = ChunkedFramingHelper.WriteLastChunk(buffer);

        Assert.Equal(3, written);
        Assert.Equal("0\r\n", Encoding.ASCII.GetString(buffer, 0, written));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void WriteTrailerSection_should_emit_trailers_and_final_crlf()
    {
        var trailers = new GaudiHeaderDictionary
        {
            { "x-checksum", "abc123" },
            { "x-timing", "42ms" }
        };
        var buffer = new byte[256];

        var written = ChunkedFramingHelper.WriteTrailerSection(buffer, trailers);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("x-checksum: abc123\r\n", result);
        Assert.Contains("x-timing: 42ms\r\n", result);
        Assert.EndsWith("\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void WriteTrailerSection_should_filter_prohibited_fields()
    {
        var trailers = new GaudiHeaderDictionary
        {
            { "x-checksum", "abc123" },
            { "transfer-encoding", "chunked" },
            { "content-length", "42" }
        };
        var buffer = new byte[256];

        var written = ChunkedFramingHelper.WriteTrailerSection(buffer, trailers);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("x-checksum: abc123\r\n", result);
        Assert.DoesNotContain("transfer-encoding", result);
        Assert.DoesNotContain("content-length", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void WriteTrailerSection_should_emit_only_crlf_when_all_filtered()
    {
        var trailers = new GaudiHeaderDictionary
        {
            { "transfer-encoding", "chunked" },
            { "content-length", "42" }
        };
        var buffer = new byte[16];

        var written = ChunkedFramingHelper.WriteTrailerSection(buffer, trailers);

        Assert.Equal(2, written);
        Assert.Equal("\r\n", Encoding.ASCII.GetString(buffer, 0, written));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void WriteTrailerSection_should_handle_multi_value_trailers()
    {
        var trailers = new GaudiHeaderDictionary();
        trailers.Add("x-multi", new StringValues(new[] { "val1", "val2" }));
        var buffer = new byte[256];

        var written = ChunkedFramingHelper.WriteTrailerSection(buffer, trailers);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("x-multi: val1, val2\r\n", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void GetTrailerSectionSize_should_estimate_correctly()
    {
        var trailers = new GaudiHeaderDictionary
        {
            { "x-checksum", "abc123" }
        };

        var size = ChunkedFramingHelper.GetTrailerSectionSize(trailers);

        // "x-checksum: abc123\r\n" = 20 bytes + final "\r\n" = 2
        Assert.True(size >= 22, $"Expected >= 22, got {size}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void WriteTerminator_should_still_produce_five_bytes()
    {
        var buffer = new byte[16];

        var written = ChunkedFramingHelper.WriteTerminator(buffer);

        Assert.Equal(5, written);
        Assert.Equal("0\r\n\r\n", Encoding.ASCII.GetString(buffer, 0, written));
    }
}
