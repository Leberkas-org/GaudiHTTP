using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.End2End.H11;

/// <summary>
/// Verifies HTTPS requests tunnel through a forward proxy via CONNECT: an in-process
/// CONNECT proxy terminates the handshake, relays the TLS bytes to the real TurboServer,
/// and records the CONNECT request line and headers for assertions.
/// </summary>
[Collection("H11")]
public sealed class ConnectTunnelSpec : End2EndSpecBase
{
    private ConnectProxy? _proxy;

    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override bool UseTls => true;

    protected override void ConfigureServer(
        TurboServerOptions options, ushort port, System.Security.Cryptography.X509Certificates.X509Certificate2? cert)
    {
        // The base binds HTTP/1.1 without TLS; a CONNECT tunnel only makes sense for HTTPS.
        options.ListenLocalhost(port, listen =>
        {
            listen.UseHttps(cert!);
            listen.Protocols = HttpProtocols.Http1;
        });
    }

    protected override void ConfigureClientOptions(TurboClientOptions options)
    {
        _proxy = new ConnectProxy();
        _proxy.Start();

        options.UseProxy = true;
        options.Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{_proxy.Port}"));
        options.DefaultProxyCredentials = new NetworkCredential("tunnel-user", "tunnel-pass");
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/tunneled", () => Results.Text("through-connect-tunnel"));
    }

    public override async ValueTask DisposeAsync()
    {
        _proxy?.Dispose();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task Https_request_should_tunnel_through_connect_proxy()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/tunneled");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("through-connect-tunnel", body);

        Assert.True(_proxy!.ConnectCount >= 1, "Request did not tunnel through the CONNECT proxy");
        var server = new Uri(BaseUri);
        Assert.Contains($"CONNECT {server.Host}:{server.Port} HTTP/1.1", _proxy.LastConnectRequest);
    }

    [Fact(Timeout = 15000)]
    public async Task Connect_request_should_carry_proxy_authorization()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/tunneled");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("tunnel-user:tunnel-pass"));
        Assert.Contains($"Proxy-Authorization: Basic {expected}", _proxy!.LastConnectRequest);
    }

    /// <summary>An <see cref="IWebProxy"/> that always routes to a fixed proxy and never bypasses.</summary>
    private sealed class FixedProxy(Uri proxy) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => proxy;
        public bool IsBypassed(Uri host) => false;
    }

    private sealed class ConnectProxy : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private int _connectCount;
        private volatile string _lastConnectRequest = string.Empty;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public string LastConnectRequest => _lastConnectRequest;

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
                    _ = TunnelAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task TunnelAsync(TcpClient downstream)
        {
            try
            {
                using (downstream)
                {
                    var ds = downstream.GetStream();

                    var headerBytes = await ReadConnectRequestAsync(ds);
                    var headerText = Encoding.ASCII.GetString(headerBytes);
                    _lastConnectRequest = headerText;

                    var requestLine = headerText[..headerText.IndexOf('\r')].Split(' ');
                    if (requestLine.Length < 2 || requestLine[0] != "CONNECT")
                    {
                        await WriteAsciiAsync(ds, "HTTP/1.1 405 Method Not Allowed\r\n\r\n");
                        return;
                    }

                    Interlocked.Increment(ref _connectCount);

                    var target = requestLine[1].Split(':');
                    using var upstream = new TcpClient();
                    await upstream.ConnectAsync(target[0], int.Parse(target[1]), _cts.Token);

                    await WriteAsciiAsync(ds, "HTTP/1.1 200 Connection Established\r\n\r\n");

                    await using var us = upstream.GetStream();
                    var toUpstream = ds.CopyToAsync(us, _cts.Token);
                    var toDownstream = us.CopyToAsync(ds, _cts.Token);
                    await Task.WhenAny(toUpstream, toDownstream);
                }
            }
            catch
            {
                // Best-effort tunnel; the connection is torn down with the test.
            }
        }

        private async Task<byte[]> ReadConnectRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8 * 1024];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), _cts.Token);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (buffer.AsSpan(0, total).IndexOf("\r\n\r\n"u8) >= 0)
                {
                    break;
                }
            }

            return buffer[..total];
        }

        private async Task WriteAsciiAsync(NetworkStream stream, string text)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(text), _cts.Token);
            await stream.FlushAsync(_cts.Token);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
