using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Shared;

/// <summary>
/// Base for tests that drive TurboServer over TLS with one or more negotiated protocols,
/// using a neutral .NET <see cref="HttpClient"/> as the reference client. Subclasses choose
/// the server's advertised protocols via <see cref="ServerProtocols"/>; tests pick the client's
/// requested version per call via <see cref="CreateVersionedTlsClient"/>.
/// </summary>
public abstract class MultiProtocolTlsServerSpecBase : ServerSpecBase
{
    protected abstract HttpProtocols ServerProtocols { get; }

    protected virtual Version DefaultClientVersion => HttpVersion.Version20;

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        var certificate = CreateSelfSignedCertificate("localhost");
        builder.Host.UseTurboHttp(options => ConfigureListener(options, port, certificate));
    }

    /// <summary>
    /// Binds the server listener. Default is TCP + TLS advertising <see cref="ServerProtocols"/>.
    /// H3 subclasses override this to bind a QUIC listener.
    /// </summary>
    protected virtual void ConfigureListener(TurboServerOptions options, ushort port, X509Certificate2 certificate)
    {
        options.ListenLocalhost(port, listen =>
        {
            listen.UseHttps(certificate);
            listen.Protocols = ServerProtocols;
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        // Echoes the protocol the server actually negotiated for this request ("HTTP/1.1", "HTTP/2", "HTTP/3").
        app.MapGet("/protocol", (HttpContext ctx) => Results.Ok(ctx.Request.Protocol));

        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(CancellationToken);
            return Results.Ok(body);
        });
    }

    protected override HttpClient CreateHttpClient() => CreateVersionedTlsClient(DefaultClientVersion);

    /// <summary>
    /// Creates a TLS client (accepting the self-signed cert) that requests <paramref name="version"/>
    /// with <c>RequestVersionOrLower</c>, so the negotiated protocol reflects ALPN selection.
    /// </summary>
    protected HttpClient CreateVersionedTlsClient(Version version)
    {
        var client = CreateTlsClient();
        client.DefaultRequestVersion = version;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        return client;
    }

    /// <summary>
    /// Creates a TLS client that requests <paramref name="version"/> with <c>RequestVersionExact</c>,
    /// so ALPN offers only that protocol — the connection is that version or the request fails.
    /// </summary>
    protected HttpClient CreateExactVersionTlsClient(Version version)
    {
        var client = CreateTlsClient();
        client.DefaultRequestVersion = version;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        return client;
    }

    protected Uri Url(string path) => new($"https://127.0.0.1:{Port}{path}");

    /// <summary>
    /// Builds a request pinned to <see cref="DefaultClientVersion"/> with <c>RequestVersionExact</c>.
    /// Caller-constructed <see cref="HttpRequestMessage"/> instances do NOT inherit the client's
    /// <c>DefaultRequestVersion</c> (only the convenience methods like GetAsync do), so a manual
    /// <c>SendAsync</c> request must carry the version itself to exercise the intended protocol.
    /// </summary>
    protected HttpRequestMessage NewRequest(HttpMethod method, string path) =>
        new(method, Url(path))
        {
            Version = DefaultClientVersion,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
}
