using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.Client;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

/// <summary>
/// Verifies the client actually routes through a configured proxy: a transparent in-process
/// relay proxy forwards to the real TurboServer and counts how many connections it received.
/// </summary>
[Collection("H11")]
public sealed class ProxySpec : End2EndSpecBase
{
    private RelayProxy? _proxy;

    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureClientOptions(TurboClientOptions options)
    {
        var server = new Uri(BaseUri);
        _proxy = new RelayProxy(server.Host, server.Port);
        _proxy.Start();

        options.UseProxy = true;
        options.Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{_proxy.Port}"));
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/via-proxy", () => Results.Text("through-proxy"));
    }

    public override async ValueTask DisposeAsync()
    {
        _proxy?.Dispose();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Client_should_route_request_through_configured_proxy()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/via-proxy");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("through-proxy", body);
        Assert.True(_proxy!.ConnectionCount >= 1, "Request did not pass through the configured proxy");
    }

    /// <summary>An <see cref="IWebProxy"/> that always routes to a fixed proxy and never bypasses.</summary>
    private sealed class FixedProxy(Uri proxy) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => proxy;
        public bool IsBypassed(Uri host) => false;
    }

    private sealed class RelayProxy(string upstreamHost, int upstreamPort) : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private int _connectionCount;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public void Start()
        {
            _listener.Start();
            _ = AcceptLoop();
        }

        private async Task AcceptLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    Interlocked.Increment(ref _connectionCount);
                    _ = RelayAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RelayAsync(TcpClient downstream)
        {
            try
            {
                using (downstream)
                using (var upstream = new TcpClient())
                {
                    await upstream.ConnectAsync(upstreamHost, upstreamPort, _cts.Token);
                    await using var ds = downstream.GetStream();
                    await using var us = upstream.GetStream();
                    var toUpstream = ds.CopyToAsync(us, _cts.Token);
                    var toDownstream = us.CopyToAsync(ds, _cts.Token);
                    await Task.WhenAny(toUpstream, toDownstream);
                }
            }
            catch
            {
                // Best-effort relay; the connection is torn down with the test.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
