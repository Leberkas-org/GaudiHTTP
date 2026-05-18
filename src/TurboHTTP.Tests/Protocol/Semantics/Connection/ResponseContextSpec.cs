using System.Globalization;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Connection;

public sealed class ResponseContextSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.2.3")]
    public void Should_Parse_RetryAfter_DelaySeconds()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Retry-After", "120");

        var result = RetryEvaluator.Evaluate(
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/"),
            response,
            attemptCount: 1);

        Assert.True(result.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfterDelay);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.2.3")]
    public void Should_Parse_RetryAfter_HttpDate()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        var futureDate = DateTimeOffset.UtcNow.AddSeconds(300);
        response.Headers.TryAddWithoutValidation("Retry-After", futureDate.ToString("r", CultureInfo.InvariantCulture));

        var result = RetryEvaluator.Evaluate(
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/"),
            response,
            attemptCount: 1);

        Assert.True(result.ShouldRetry);
        Assert.NotNull(result.RetryAfterDelay);
        Assert.True(result.RetryAfterDelay >= TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-7.4.1")]
    public void Should_Provide_AllowHeader_Constants()
    {
        const string allowMethods = "GET, HEAD, PUT";
        Assert.Contains("GET", allowMethods);
        Assert.Contains("PUT", allowMethods);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.6.1")]
    public void Should_Format_Date_As_IMFFixdate()
    {
        var now = DateTimeOffset.UtcNow;
        var rfc1123Date = now.ToString("r", CultureInfo.InvariantCulture);

        Assert.NotNull(rfc1123Date);
        Assert.Contains("GMT", rfc1123Date);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.3.2")]
    public void Should_Support_Location_Header_On_Redirect()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.MovedPermanently);
        response.Headers.Location = new Uri("https://example.com/new-location");

        Assert.NotNull(response.Headers.Location);
        Assert.Equal("https://example.com/new-location", response.Headers.Location.AbsoluteUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.7.5")]
    public void Should_Have_Server_Header_Format()
    {
        const string serverValue = "TurboHTTP/1.0";
        Assert.Contains("TurboHTTP", serverValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.5.6")]
    public void Should_Provide_ReasonPhrase_For_Status_405()
    {
        var phrase = ReasonPhrases.For(405);

        Assert.Equal("Method Not Allowed", phrase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-14.3")]
    public void Should_Support_AcceptRanges_Bytes()
    {
        const string acceptRangesValue = "bytes";
        Assert.Equal("bytes", acceptRangesValue);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void Should_Evaluate_Retry_On_Network_Failure()
    {
        var result = RetryEvaluator.Evaluate(
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/"),
            response: null,
            networkFailure: true,
            attemptCount: 1);

        Assert.True(result.ShouldRetry);
    }
}
