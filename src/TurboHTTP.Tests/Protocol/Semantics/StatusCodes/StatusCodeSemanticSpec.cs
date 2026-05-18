using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.StatusCodes;

public sealed class StatusCodeSemanticSpec
{
    [Theory(Timeout = 5000)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(103)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_1xx_as_informational(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(StatusCodeClass.Informational, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(203)]
    [InlineData(204)]
    [InlineData(205)]
    [InlineData(206)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_2xx_as_successful(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(StatusCodeClass.Successful, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(304)]
    [InlineData(305)]
    [InlineData(307)]
    [InlineData(308)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_3xx_as_redirection(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(StatusCodeClass.Redirection, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(406)]
    [InlineData(407)]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(410)]
    [InlineData(411)]
    [InlineData(412)]
    [InlineData(413)]
    [InlineData(414)]
    [InlineData(415)]
    [InlineData(416)]
    [InlineData(417)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_4xx_as_client_error(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(StatusCodeClass.ClientError, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(505)]
    [InlineData(506)]
    [InlineData(507)]
    [InlineData(508)]
    [InlineData(510)]
    [InlineData(511)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_5xx_as_server_error(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(StatusCodeClass.ServerError, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(100)]
    [InlineData(199)]
    [InlineData(200)]
    [InlineData(299)]
    [InlineData(300)]
    [InlineData(399)]
    [InlineData(400)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(599)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_all_valid_status_codes(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        var expectedClass = (code / 100) switch
        {
            1 => StatusCodeClass.Informational,
            2 => StatusCodeClass.Successful,
            3 => StatusCodeClass.Redirection,
            4 => StatusCodeClass.ClientError,
            5 => StatusCodeClass.ServerError,
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(expectedClass, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(200)]
    [InlineData(203)]
    [InlineData(204)]
    [InlineData(206)]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(308)]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(410)]
    [InlineData(414)]
    [InlineData(501)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_response_as_heuristically_cacheable(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var isCacheable = StatusCodeSemantics.IsHeuristicallyCacheable(statusCode);

        Assert.True(isCacheable);
    }

    [Theory(Timeout = 5000)]
    [InlineData(100)]
    [InlineData(201)]
    [InlineData(205)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(304)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(502)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_classify_response_as_not_heuristically_cacheable(int code)
    {
        var statusCode = (HttpStatusCode)code;
        var isCacheable = StatusCodeSemantics.IsHeuristicallyCacheable(statusCode);

        Assert.False(isCacheable);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.1")]
    public void StatusCodeSemantics_should_handle_unrecognized_status_code_as_x00_equivalent()
    {
        var unrecognized = (HttpStatusCode)418;
        var classification = StatusCodeSemantics.Classify(unrecognized);

        Assert.Equal(StatusCodeClass.ClientError, classification);
    }

    [Theory(Timeout = 5000)]
    [InlineData(100, "Informational")]
    [InlineData(200, "Successful")]
    [InlineData(300, "Redirection")]
    [InlineData(400, "ClientError")]
    [InlineData(500, "ServerError")]
    public void StatusCodeSemantics_should_provide_string_representation(int code, string expectedClass)
    {
        var statusCode = (HttpStatusCode)code;
        var classification = StatusCodeSemantics.Classify(statusCode);

        Assert.Equal(expectedClass, classification.ToString());
    }
}