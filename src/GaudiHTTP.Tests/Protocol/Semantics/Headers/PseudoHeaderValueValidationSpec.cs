using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Headers;

public sealed class PseudoHeaderValueValidationSpec
{
    private static readonly string Section = "RFC 9113 §8.3.1";

    private static List<(string Name, string Value)> Headers(params (string Name, string Value)[] h) => [..h];

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void ValidateRequestPseudoHeaders_should_reject_empty_path_for_non_CONNECT()
    {
        var headers = Headers(
            (":method", "GET"),
            (":path", ""),
            (":scheme", "https"),
            (":authority", "localhost"));

        var ex = Assert.Throws<HttpProtocolException>(() =>
            PseudoHeaderValidator.ValidateRequestPseudoHeaders(
                headers, h => h.Name, h => h.Value, Section));

        Assert.Contains(":path", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void ValidateRequestPseudoHeaders_should_accept_non_empty_path()
    {
        var headers = Headers(
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":authority", "localhost"));

        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers, h => h.Name, h => h.Value, Section);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void ValidateRequestPseudoHeaders_should_accept_asterisk_path_for_OPTIONS()
    {
        var headers = Headers(
            (":method", "OPTIONS"),
            (":path", "*"),
            (":scheme", "https"),
            (":authority", "localhost"));

        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers, h => h.Name, h => h.Value, Section);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3.1")]
    public void ValidateRequestPseudoHeaders_should_accept_CONNECT_without_path()
    {
        var headers = Headers(
            (":method", "CONNECT"),
            (":authority", "proxy.example.com:443"));

        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers, h => h.Name, h => h.Value, Section);
    }
}
