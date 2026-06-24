using System.Net;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.AcceptanceTests.H11;

public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task Smoke_should_send_get_request_to_hello_and_receive_200_with_hello_world_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version11
        };

        var responseBytes = FakeResponse.Http11(200, "Hello World");

        var response = await SendClientAsync(
            HttpVersion.Version11, request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", responseBody);
    }
}
