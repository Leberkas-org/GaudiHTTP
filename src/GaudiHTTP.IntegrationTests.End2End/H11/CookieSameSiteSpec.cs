using System.Net;
using System.Text.Json;
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Server;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class CookieSameSiteSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private IGaudiHttpClient? _client;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _clientProvider;

    private string BaseUri { get; set; } = string.Empty;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IGaudiHttpClient Client => _client!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        // Bind port 0 and read the real port back after start — probing for a free
        // port and rebinding it races with parallel tests (and parallel test modules).
        builder.Host.UseGaudiHttp(options =>
        {
            options.Bind(new TcpListenerOptions
            {
                Host = "127.0.0.1",
                Port = 0
            });
        });

        _app = builder.Build();

        _app.MapGet("/cookie/set-strict", (HttpContext ctx) =>
        {
            ctx.Response.Headers.SetCookie = "stricttoken=secret123; Path=/; SameSite=Strict";
            return Results.Json(new { message = "Cookie set" });
        });

        _app.MapGet("/cookie/echo", (HttpContext ctx) =>
        {
            var cookieHeader = ctx.Request.Headers.Cookie.ToString();
            var cookies = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(cookieHeader))
            {
                foreach (var pair in cookieHeader.Split(';', StringSplitOptions.TrimEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        cookies[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
                    }
                }
            }

            return Results.Json(cookies);
        });

        await _app.StartAsync(CancellationToken);

        var address = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.First();
        BaseUri = $"http://127.0.0.1:{new Uri(address).Port}";

        var services = new ServiceCollection();

        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create();
        var system = ActorSystem.Create($"e2e-cookie-samesite-{Guid.NewGuid():N}", bootstrap.And(diSetup));
        services.AddSingleton(system);

        var clientBuilder = services.AddGaudiHttpClient(string.Empty, options =>
        {
            options.BaseAddress = new Uri(BaseUri);
            options.DangerousAcceptAnyServerCertificate = false;
        });
        clientBuilder.WithCookies();

        _clientProvider = services.BuildServiceProvider();

        var factory = _clientProvider.GetRequiredService<IGaudiHttpClientFactory>();
        _client = factory.CreateClient(string.Empty);
        _client.DefaultRequestVersion = HttpVersion.Version11;
        _client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        _client.Timeout = TimeSpan.FromSeconds(10);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken);
            await _app.DisposeAsync();
        }

        if (_clientProvider is not null)
        {
            var system = _clientProvider.GetService<ActorSystem>();
            if (system is not null)
            {
                await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
                await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
            }

            await _clientProvider.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task SameSiteStrict_should_be_sent_on_first_party_request()
    {
        var setCookieRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/cookie/set-strict");
        var setCookieResponse = await Client.SendAsync(setCookieRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, setCookieResponse.StatusCode);
        await Task.Delay(150, CancellationToken);
        var echoRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/cookie/echo");
        var echoResponse = await Client.SendAsync(echoRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        Assert.NotNull(cookies);
        Assert.True(cookies.ContainsKey("stricttoken"), "SameSite=Strict cookie should be sent on first-party request");
        Assert.Equal("secret123", cookies["stricttoken"]);
    }

    [Fact(Timeout = 15000)]
    public async Task SameSiteStrict_should_not_be_sent_on_cross_site_request()
    {
        var setCookieRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/cookie/set-strict");
        var setCookieResponse = await Client.SendAsync(setCookieRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, setCookieResponse.StatusCode);

        var crossSiteUri = new Uri("http://other.example.test:9999");
        var echoRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/cookie/echo")
            .WithFirstPartyContext(crossSiteUri);

        var echoResponse = await Client.SendAsync(echoRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, echoResponse.StatusCode);

        var json = await echoResponse.Content.ReadAsStringAsync(CancellationToken);
        var cookies = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        Assert.NotNull(cookies);
        Assert.False(cookies.ContainsKey("stricttoken"),
            "SameSite=Strict cookie should NOT be sent on cross-site request");
    }
}