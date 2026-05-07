using System.Net;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11BodyDecoderSpec
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
        Assert.Equal(expected, BodyDecoder.IsNoBodyResponse(statusCode));
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
        Assert.Equal(expected, BodyDecoder.IsContentHeader(name));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_null_when_header_missing()
    {
        var headers = new Dictionary<string, List<string>>();
        Assert.Null(BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_null_when_values_empty()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = []
        };

        Assert.Null(BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_value_when_single_valid()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["42"]
        };

        Assert.Equal(42, BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_null_when_non_numeric()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["abc"]
        };

        Assert.Null(BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_null_when_negative()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["-1"]
        };

        Assert.Null(BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_accept_duplicate_identical_values()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["100", "100"]
        };

        Assert.Equal(100, BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_throw_when_conflicting_values()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["100", "200"]
        };

        var ex = Assert.Throws<HttpDecoderException>(() => BodyDecoder.GetContentLengthHeader(headers));
        Assert.Equal(HttpDecoderError.MultipleContentLengthValues, ex.DecodeError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void GetContentLengthHeader_should_return_zero_for_zero()
    {
        var headers = new Dictionary<string, List<string>>
        {
            [WellKnownHeaders.Names.ContentLength] = ["0"]
        };

        Assert.Equal(0, BodyDecoder.GetContentLengthHeader(headers));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.4")]
    public void GetSingleHeader_should_return_first_value_when_present()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["X-Custom"] = ["value1", "value2"]
        };

        Assert.Equal("value1", BodyDecoder.GetSingleHeader(headers, "X-Custom"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.4")]
    public void GetSingleHeader_should_return_null_when_header_missing()
    {
        var headers = new Dictionary<string, List<string>>();
        Assert.Null(BodyDecoder.GetSingleHeader(headers, "X-Missing"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.4")]
    public void GetSingleHeader_should_return_null_when_values_empty()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["X-Empty"] = []
        };

        Assert.Null(BodyDecoder.GetSingleHeader(headers, "X-Empty"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task BuildResponseFromRemainder_should_build_response_with_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["text/plain"],
            ["x-custom"] = ["test"]
        };
        var body = "Hello"u8;

        var response = BodyDecoder.BuildResponseFromRemainder(200, "OK", headers, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.Equal(HttpVersion.Version11, response.Version);

        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(5, content.Length);
        Assert.Equal((byte)'H', content[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task BuildResponseFromRemainder_should_build_empty_response_when_no_body()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["x-custom"] = ["test"]
        };

        var response = BodyDecoder.BuildResponseFromRemainder(204, "No Content", headers, []);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void BuildResponseFromRemainder_should_set_content_headers_on_content()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["content-type"] = ["application/json"],
            ["content-length"] = ["42"],
            ["expires"] = ["Thu, 01 Dec 1994 16:00:00 GMT"],
            ["x-non-content"] = ["ignored-on-content"]
        };

        var response = BodyDecoder.BuildResponseFromRemainder(200, "OK", headers, "test"u8);

        Assert.True(response.Content.Headers.Contains("content-type"));
        Assert.True(response.Content.Headers.Contains("expires"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void BuildResponseFromRemainder_should_set_general_headers_on_response()
    {
        var headers = new Dictionary<string, List<string>>
        {
            ["x-custom"] = ["value"]
        };

        var response = BodyDecoder.BuildResponseFromRemainder(200, "OK", headers, []);

        Assert.True(response.Headers.Contains("x-custom"));
        Assert.Equal("value", response.Headers.GetValues("x-custom").First());
    }
}
