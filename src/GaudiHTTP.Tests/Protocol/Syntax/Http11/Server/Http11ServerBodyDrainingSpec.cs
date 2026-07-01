using System.Text;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Syntax.Http11.Options;
using GaudiHTTP.Protocol.Syntax.Http11.Server;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerBodyDrainingSpec
{
    private static Http11ServerDecoderOptions DefaultDecoderOptions() => new()
    {
        MaxPipelinedRequests = 10,
        MaxChunkExtensionLength = 4 * 1024,
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 4 * 1024 * 1024,
        MaxStreamedBodySize = null,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
        HeaderLineMaxLength = 8 * 1024,
        RequestLineMaxLength = 8 * 1024,
        MaxRequestTargetLength = 8 * 1024,
        MaxChunkedControlLineLength = 64 * 1024,
        MaxChunkedTrailerSize = 32 * 1024,
        AllowObsFold = false
    };

    [Fact(Timeout = 5000)]
    public void Http11ServerStateMachine_should_expose_current_body_reader()
    {
        var decoder = new Http11ServerDecoder(DefaultDecoderOptions(), new ConnectionObjectPool());

        const string request = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(request);

        decoder.Feed(bytes, out _);

        Assert.NotNull(decoder.CurrentBodyReader);
        Assert.True(decoder.CurrentBodyReader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public void Http11ServerStateMachine_should_expose_null_body_reader_when_reset()
    {
        var decoder = new Http11ServerDecoder(DefaultDecoderOptions(), new ConnectionObjectPool());

        const string request = "POST / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 5\r\n\r\nhello";
        var bytes = Encoding.ASCII.GetBytes(request);

        decoder.Feed(bytes, out _);
        decoder.Reset();

        Assert.Null(decoder.CurrentBodyReader);
    }
}