using System;
using System.Buffers;
using System.Linq;
using System.Net.Http;
using System.Text;
using TurboHttp.Protocol.RFC1945;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests for RFC 9110 §4.2.4 — "A sender MUST NOT generate the userinfo subcomponent."
/// Verifies that Http2RequestEncoder strips userinfo from the :authority pseudo-header.
/// </summary>
public sealed class UserinfoStrippingTests
{
    [Fact(DisplayName = "RFC9110-4.2.4-UI-001: H2 authority strips userinfo from http URI")]
    public void H2_Should_StripUserinfo_When_HttpUri()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("example.com", authority);
        Assert.DoesNotContain("user", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-002: H2 authority strips userinfo from https URI")]
    public void H2_Should_StripUserinfo_When_HttpsUri()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://user:pass@secure.example.com/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("secure.example.com", authority);
        Assert.DoesNotContain("@", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-003: H2 authority preserves port after stripping")]
    public void H2_Should_PreservePort_When_UserinfoPresent()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://u:p@host.example.com:8080/");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        Assert.Equal("host.example.com:8080", authority);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-004: H2 authority unchanged when no userinfo")]
    public void H2_Should_NotChange_When_NoUserinfo()
    {
        var encoder = new Http2RequestEncoder();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com:443/resource");

        var headerBlock = encoder.EncodeToHpackBlock(request);
        var headers = new HpackDecoder().Decode(headerBlock);

        var authority = headers.First(h => h.Name == ":authority").Value;
        // Port 443 is default for https — should be omitted
        Assert.Equal("example.com", authority);
    }

    // ── HTTP/1.1 Userinfo Stripping ────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-4.2.4-UI-005: H11 absolute-form strips userinfo")]
    public void H11_Should_StripUserinfo_When_AbsoluteForm()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com:8080/path?q=1");
        var result = EncodeHttp11Absolute(request);

        Assert.Contains("GET http://example.com:8080/path?q=1 HTTP/1.1\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-006: H11 origin-form unaffected by userinfo")]
    public void H11_Should_NotContainUserinfo_When_OriginForm()
    {
        // Origin-form only emits path+query, so userinfo in the URI never appears
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path?q=1");
        var result = EncodeHttp11Origin(request);

        Assert.Contains("GET /path?q=1 HTTP/1.1\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("@", result);
    }

    // ── HTTP/1.0 Userinfo Stripping ────────────────────────────────────────────

    [Fact(DisplayName = "RFC9110-4.2.4-UI-007: H10 absolute-form strips userinfo")]
    public void H10_Should_StripUserinfo_When_AbsoluteForm()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com:8080/path?q=1");
        var result = EncodeHttp10Absolute(request);

        Assert.Contains("GET http://example.com:8080/path?q=1 HTTP/1.0\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("pass", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-008: H10 origin-form unaffected by userinfo")]
    public void H10_Should_NotContainUserinfo_When_OriginForm()
    {
        // Origin-form only emits path+query, so userinfo in the URI never appears
        var request = new HttpRequestMessage(HttpMethod.Get, "http://user:pass@example.com/path?q=1");
        var result = EncodeHttp10Origin(request);

        Assert.Contains("GET /path?q=1 HTTP/1.0\r\n", result);
        Assert.DoesNotContain("user", result);
        Assert.DoesNotContain("@", result);
    }

    [Fact(DisplayName = "RFC9110-4.2.4-UI-009: H10 absolute-form unchanged when no userinfo")]
    public void H10_Should_NotChange_When_NoUserinfo()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var result = EncodeHttp10Absolute(request);

        Assert.Contains("GET http://example.com/resource HTTP/1.0\r\n", result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string EncodeHttp10Absolute(HttpRequestMessage request)
    {
        var buffer = new Memory<byte>(new byte[4096]);
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeHttp10Origin(HttpRequestMessage request)
    {
        var buffer = new Memory<byte>(new byte[4096]);
        var written = Http10Encoder.Encode(request, ref buffer, absoluteForm: false);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeHttp11Absolute(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span, absoluteForm: true);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static string EncodeHttp11Origin(HttpRequestMessage request)
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var buffer = owner.Memory;
        var span = buffer.Span;
        var written = Http11Encoder.Encode(request, ref span, absoluteForm: false);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }
}
