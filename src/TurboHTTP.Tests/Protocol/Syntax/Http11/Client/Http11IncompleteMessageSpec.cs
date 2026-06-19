using System.Text;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Syntax;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Tests.TestSupport;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11IncompleteMessageSpec
{
    private readonly Http11ClientDecoder _decoder = new(ClientOptionDefaults.Http11Decoder(), new ConnectionPoolContext());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Feed_should_detect_truncated_content_length_response()
    {
        var header = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n"u8.ToArray();
        var partialBody = new byte[20];
        Array.Fill(partialBody, (byte)'x');

        var raw = new byte[header.Length + partialBody.Length];
        header.CopyTo(raw, 0);
        partialBody.CopyTo(raw, header.Length);

        var outcome = _decoder.Feed(raw, requestMethodWasHead: false, out var consumed);

        Assert.NotEqual(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-8")]
    public void Feed_should_detect_incomplete_chunked_response()
    {
        var raw = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nhello\r\n");

        var outcome = _decoder.Feed(raw, requestMethodWasHead: false, out _);

        Assert.NotEqual(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.2")]
    public void Feed_should_not_produce_response_from_unsolicited_data()
    {
        var raw = "This is not an HTTP response\r\n\r\n"u8.ToArray();

        var outcome = _decoder.Feed(raw, requestMethodWasHead: false, out _);

        Assert.NotEqual(DecodeOutcome.Complete, outcome);
    }
}
