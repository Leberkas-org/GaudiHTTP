using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Tests.RFC9110;

/// <summary>
/// Tests 206 Partial Content response validation per RFC 9110 §15.3.7.
/// A 206 response MUST contain a Content-Range header (single part) or use
/// multipart/byteranges content type (multiple parts). Non-206 responses skip validation.
/// </summary>
/// <remarks>
/// Class under test: <see cref="PartialContentValidator"/>.
/// </remarks>
public sealed class PartialContentTests
{
    [Fact(DisplayName = "RFC9110-15.3.7-PR-001: 206 with Content-Range bytes is valid")]
    public void Should_BeValid_When_ContentRangePresent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[100]);
        response.Content.Headers.Add("Content-Range", "bytes 0-99/200");

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.False(result.IsMultipartByteRanges);
        Assert.False(result.Skipped);
        Assert.Null(result.ErrorMessage);
    }

    [Fact(DisplayName = "RFC9110-15.3.7-PR-002: 206 without Content-Range is invalid")]
    public void Should_BeInvalid_When_NoContentRange()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[100]);

        var result = PartialContentValidator.Validate(response);

        Assert.False(result.IsValid);
        Assert.False(result.Skipped);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Content-Range", result.ErrorMessage);
    }

    [Fact(DisplayName = "RFC9110-15.3.7-PR-003: 206 multipart/byteranges detected")]
    public void Should_Detect_When_MultipartByteranges()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[200]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/byteranges")
        {
            Parameters = { new NameValueHeaderValue("boundary", "\"example-boundary\"") }
        };

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.IsMultipartByteRanges);
        Assert.False(result.Skipped);
        Assert.Null(result.ErrorMessage);
    }

    [Fact(DisplayName = "RFC9110-15.3.7-PR-004: 200 response skips validation")]
    public void Should_Skip_When_Not206()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent(new byte[100]);

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.Skipped);
        Assert.False(result.IsMultipartByteRanges);
        Assert.Null(result.ErrorMessage);
    }

    [Fact(DisplayName = "RFC9110-15.3.7-PR-005: 304 response skips validation")]
    public void Should_Skip_When_304NotModified()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        response.Content = new ByteArrayContent([]);

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.Skipped);
    }

    [Fact(DisplayName = "RFC9110-15.3.7-PR-006: 206 with Content-Range and multipart prefers multipart")]
    public void Should_DetectMultipart_When_BothContentRangeAndMultipart()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.PartialContent);
        response.Content = new ByteArrayContent(new byte[200]);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("multipart/byteranges")
        {
            Parameters = { new NameValueHeaderValue("boundary", "\"sep\"") }
        };
        response.Content.Headers.Add("Content-Range", "bytes 0-99/200");

        var result = PartialContentValidator.Validate(response);

        Assert.True(result.IsValid);
        Assert.True(result.IsMultipartByteRanges);
    }
}
