using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Body;

public sealed class ContentLengthSemanticSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_parse_valid_content_length()
    {
        const string contentLengthValue = "1234";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out var length);

        Assert.True(success);
        Assert.Equal(1234, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_reject_negative_content_length()
    {
        const string contentLengthValue = "-1";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out _);

        Assert.False(success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_reject_non_numeric_content_length()
    {
        const string contentLengthValue = "abc";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out _);

        Assert.False(success);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    public void ContentLengthSemantics_should_reject_signed_or_spaced_content_length(string value)
    {
        // RFC 9112 §6.3 requires 1*DIGIT; a leading sign or surrounding whitespace is a
        // request-smuggling differential against strict peers.
        var success = ContentLengthSemantics.TryParse(value, out _);

        Assert.False(success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_reject_empty_content_length()
    {
        const string contentLengthValue = "";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out _);

        Assert.False(success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_accept_zero_content_length()
    {
        const string contentLengthValue = "0";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out var length);

        Assert.True(success);
        Assert.Equal(0, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_accept_large_content_length()
    {
        const string contentLengthValue = "9223372036854775807";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out var length);

        Assert.True(success);
        Assert.Equal(9223372036854775807, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_reject_content_length_exceeding_long_max()
    {
        const string contentLengthValue = "9223372036854775808";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out _);

        Assert.False(success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_no_body_required_for_1xx()
    {
        var statusCode = System.Net.HttpStatusCode.Continue;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.False(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_no_body_required_for_204()
    {
        var statusCode = System.Net.HttpStatusCode.NoContent;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.False(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_no_body_required_for_304()
    {
        var statusCode = System.Net.HttpStatusCode.NotModified;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.False(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_no_body_required_for_HEAD_response()
    {
        var statusCode = System.Net.HttpStatusCode.OK;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "HEAD");

        Assert.False(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_body_required_for_normal_2xx()
    {
        var statusCode = System.Net.HttpStatusCode.OK;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.True(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_body_required_for_3xx()
    {
        var statusCode = System.Net.HttpStatusCode.MovedPermanently;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.True(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_body_required_for_4xx()
    {
        var statusCode = System.Net.HttpStatusCode.BadRequest;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.True(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_classify_body_required_for_5xx()
    {
        var statusCode = System.Net.HttpStatusCode.InternalServerError;
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, "GET");

        Assert.True(requiresBody);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_reject_content_length_with_spaces()
    {
        const string contentLengthValue = "123 456";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out _);

        Assert.False(success);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_accept_leading_zeros()
    {
        const string contentLengthValue = "00123";
        var success = ContentLengthSemantics.TryParse(contentLengthValue, out var length);

        Assert.True(success);
        Assert.Equal(123, length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void ContentLengthSemantics_should_validate_connect_response_has_no_body()
    {
        var statusCode = System.Net.HttpStatusCode.OK;
        var method = "CONNECT";
        var requiresBody = ContentLengthSemantics.BodyRequired(statusCode, method);

        Assert.False(requiresBody);
    }

    [Theory(Timeout = 5000)]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("100")]
    [InlineData("1024")]
    [InlineData("65535")]
    [Trait("RFC", "RFC9112-6.2")]
    public void ContentLengthSemantics_should_parse_various_valid_lengths(string value)
    {
        var success = ContentLengthSemantics.TryParse(value, out var length);

        Assert.True(success);
        Assert.True(length >= 0);
    }
}