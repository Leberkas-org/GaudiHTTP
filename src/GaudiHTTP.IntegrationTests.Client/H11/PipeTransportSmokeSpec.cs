using System.Net;
using System.Text;
using System.Text.Json;
using TurboHTTP.IntegrationTests.Client.Shared;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Client.H11;

[Collection("H11")]
public sealed class PipeTransportSmokeSpec : IntegrationSpecBase
{
    public PipeTransportSmokeSpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
        : base(server, systemFixture)
    {
    }

    private static readonly ProtocolVariant H11Cleartext = new(TestHttpVersion.H11, tls: false);

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_200()
    {
        await using var helper = CreateClient(H11Cleartext, configureOptions: _ => { });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Get_should_return_json_body()
    {
        await using var helper = CreateClient(H11Cleartext, configureOptions: _ => { });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/get"),
            CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("url", out _));
    }

    [Fact(Timeout = 30000)]
    public async Task Post_should_echo_request_body()
    {
        await using var helper = CreateClient(H11Cleartext, configureOptions: _ => { });

        var payload = """{"key":"value"}""";
        var request = new HttpRequestMessage(HttpMethod.Post, "/post")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await helper.Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, json.RootElement.GetProperty("data").GetString());
    }

    [Fact(Timeout = 30000)]
    public async Task Status_endpoint_should_return_requested_status_code()
    {
        await using var helper = CreateClient(H11Cleartext, configureOptions: _ => { });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/status/418"),
            CancellationToken);

        Assert.Equal((HttpStatusCode)418, response.StatusCode);
    }

    [Fact(Timeout = 30000)]
    public async Task Bytes_endpoint_should_return_correct_length()
    {
        await using var helper = CreateClient(H11Cleartext, configureOptions: _ => { });

        var response = await helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/bytes/1024"),
            CancellationToken);

        var content = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, content.Length);
    }
}
