using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class HandlerMiddlewareSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/ping", () => Results.Ok("pong"));

        app.MapGet("/echo-headers", (HttpContext ctx) =>
        {
            var injected = ctx.Request.Headers["X-Handler-Injected"].ToString();
            var response = new Dictionary<string, string>
            {
                ["x-handler-injected"] = injected
            };
            return Results.Ok(response);
        });
    }

    private sealed class HeaderInjectionHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("X-Handler-Injected", "success");
            return request;
        }
    }

    private sealed class FailingHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            throw new InvalidOperationException("Handler intentionally throwing");
        }
    }

    private sealed class ConditionalFailingHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
        {
            if (request.Headers.Contains("X-Fail"))
            {
                throw new InvalidOperationException("Conditional handler failure");
            }
            return request;
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Handler_should_inject_request_headers_that_reach_server()
    {
        // Create a separate client with header-injecting handler
        var system = await GetActorSystemAsync();
        var client = CreateClientWithHandler<HeaderInjectionHandler>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/echo-headers");
        var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("success", body);
    }

    [Fact(Timeout = 10000)]
    public async Task Handler_should_fail_per_request_when_throwing()
    {
        var client = CreateClientWithHandler<FailingHandler>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request, CancellationToken));
        Assert.Contains("Handler intentionally throwing", ex.Message);
    }

    [Fact(Timeout = 10000)]
    public async Task Handler_should_fail_only_faulted_request_while_others_succeed()
    {
        var client = CreateClientWithHandler<ConditionalFailingHandler>();

        // Send a failing request
        var failingRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");
        failingRequest.Headers.Add("X-Fail", "yes");

        // Send a good request
        var goodRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/ping");

        // Execute them sequentially to test per-request isolation
        var failTask = client.SendAsync(failingRequest, CancellationToken);

        // This should throw
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => failTask);

        // Now send the good request — it should succeed despite the handler
        var goodResponse = await client.SendAsync(goodRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);
    }

    private ITurboHttpClient CreateClientWithHandler<THandler>() where THandler : TurboHandler
    {
        var services = new ServiceCollection();
        services.AddSingleton(GetActorSystemAsync().Result);

        var clientOptions = new TurboClientOptions
        {
            BaseAddress = new Uri(BaseUri),
            DangerousAcceptAnyServerCertificate = true
        };

        services.AddTurboHttpClient()
            .AddHandler<THandler>();

        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(clientOptions)));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.DefaultRequestVersion = ProtocolVersion;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        client.Timeout = TimeSpan.FromSeconds(10);

        return client;
    }

    private async Task<ActorSystem> GetActorSystemAsync()
    {
        // Create a minimal ActorSystem for the test client
        var setup = BootstrapSetup.Create();
        return await Task.FromResult(ActorSystem.Create($"test-handler-{Guid.NewGuid():N}", setup));
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}
