using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H3;

[Collection("H3")]
public sealed class DiagnosticSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version30;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/ping", () => Results.Text("pong"));
    }

    [Fact(Timeout = 15000)]
    public async Task A_first_test_turbo_client()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");
        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        await Console.Error.WriteLineAsync($"[DIAG-A] TurboClient: status={response.StatusCode} body='{body}'");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", body);
    }

    [Fact(Timeout = 15000)]
    public async Task B_second_test_turbo_client()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");
        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        await Console.Error.WriteLineAsync($"[DIAG-B] TurboClient: status={response.StatusCode} body='{body}'");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", body);
    }

    [Fact(Timeout = 15000)]
    public async Task C_third_test_dotnet_client()
    {
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        using var dotnetClient = new HttpClient(handler);
        dotnetClient.DefaultRequestVersion = HttpVersion.Version30;
        dotnetClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

        var response = await dotnetClient.GetAsync($"{BaseUri}/ping", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        await Console.Error.WriteLineAsync($"[DIAG-C] .NET Client: status={response.StatusCode} body='{body}'");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pong", body);
    }
}
