using System.Buffers;
using System.Net;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11ResponseBuilderSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    [InlineData(100, true)]
    [InlineData(101, true)]
    [InlineData(199, true)]
    [InlineData(204, true)]
    [InlineData(304, true)]
    [InlineData(200, false)]
    [InlineData(201, false)]
    [InlineData(301, false)]
    [InlineData(400, false)]
    [InlineData(500, false)]
    public void IsNoBodyResponse_should_return_expected_for_status_code(int statusCode, bool expected)
    {
        Assert.Equal(expected, ResponseBuilder.IsNoBodyResponse(statusCode));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    [InlineData("content-length", true)]
    [InlineData("Content-Length", true)]
    [InlineData("content-type", true)]
    [InlineData("Content-Type", true)]
    [InlineData("content-encoding", true)]
    [InlineData("content-disposition", true)]
    [InlineData("allow", true)]
    [InlineData("Allow", true)]
    [InlineData("expires", true)]
    [InlineData("Expires", true)]
    [InlineData("last-modified", true)]
    [InlineData("Last-Modified", true)]
    [InlineData("host", false)]
    [InlineData("accept", false)]
    [InlineData("transfer-encoding", false)]
    [InlineData("connection", false)]
    public void IsContentHeader_should_classify_headers_correctly(string name, bool expected)
    {
        Assert.Equal(expected, ResponseBuilder.IsContentHeader(name));
    }

[Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void BuildNoBody_should_set_version_and_status()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["x-custom"] = ["value"]
        };

        var response = ResponseBuilder.BuildNoBody(204, "No Content", headers);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("No Content", response.ReasonPhrase);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void BuildNoBody_should_preserve_content_headers_on_empty_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["text/plain"],
            ["content-length"] = ["0"],
            ["x-custom"] = ["value"]
        };

        var response = ResponseBuilder.BuildNoBody(204, "No Content", headers);

        Assert.True(response.Content.Headers.Contains("content-type"));
        Assert.True(response.Content.Headers.Contains("content-length"));
        Assert.True(response.Headers.Contains("x-custom"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Build_should_create_response_with_pooled_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["text/plain"],
            ["x-custom"] = ["value"]
        };

        var bodyOwner = MemoryPool<byte>.Shared.Rent(5);
        "Hello"u8.CopyTo(bodyOwner.Memory.Span);

        var response = ResponseBuilder.Build(200, "OK", headers, bodyOwner, 5);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.True(response.Content.Headers.Contains("content-type"));
        Assert.True(response.Headers.Contains("x-custom"));

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello"u8.ToArray(), body);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Build_should_create_empty_body_when_owner_null()
    {
        var headers = new Dictionary<string, List<string>>();
        var response = ResponseBuilder.Build(200, "OK", headers, null, 0);

        Assert.NotNull(response.Content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Build_should_attach_trailer_headers()
    {
        var headers = new Dictionary<string, List<string>>();
        var trailers = new Dictionary<string, List<string>>
        {
            ["x-checksum"] = ["abc123"]
        };

        var response = ResponseBuilder.Build(200, "OK", headers, null, 0, trailers);

        Assert.True(response.TrailingHeaders.Contains("x-checksum"));
        Assert.Equal("abc123", response.TrailingHeaders.GetValues("x-checksum").First());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task BuildFromRemainder_should_build_response_with_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["text/plain"],
            ["x-custom"] = ["test"]
        };
        var body = "Hello"u8;

        var response = ResponseBuilder.BuildFromRemainder(200, "OK", headers, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.Equal(HttpVersion.Version11, response.Version);

        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, content.Length);
        Assert.Equal((byte)'H', content[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task BuildFromRemainder_should_build_empty_response_when_no_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["x-custom"] = ["test"]
        };

        var response = ResponseBuilder.BuildFromRemainder(204, "No Content", headers, []);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void BuildFromRemainder_should_set_content_headers_on_content()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["application/json"],
            ["content-length"] = ["42"],
            ["expires"] = ["Thu, 01 Dec 1994 16:00:00 GMT"],
            ["x-non-content"] = ["ignored-on-content"]
        };

        var response = ResponseBuilder.BuildFromRemainder(200, "OK", headers, "test"u8);

        Assert.True(response.Content.Headers.Contains("content-type"));
        Assert.True(response.Content.Headers.Contains("expires"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void BuildFromRemainder_should_set_general_headers_on_response()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["x-custom"] = ["value"]
        };

        var response = ResponseBuilder.BuildFromRemainder(200, "OK", headers, []);

        Assert.True(response.Headers.Contains("x-custom"));
        Assert.Equal("value", response.Headers.GetValues("x-custom").First());
    }
}
