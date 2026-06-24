using System.Net;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.AcceptanceTests.H10;

public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-4.1")]
    public async Task SmokeTest_should_return_200_hello_world()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version10
        };

        var responseBytes = FakeResponse.Http10(200, "Hello World");

        var response = await SendClientAsync(
            HttpVersion.Version10, request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", responseBody);
    }
}
