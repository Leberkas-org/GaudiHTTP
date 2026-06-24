using System.Net;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.AcceptanceTests.H2;

public sealed class SmokeSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Basic_get_request_should_succeed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var (response, _) = await SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }
}