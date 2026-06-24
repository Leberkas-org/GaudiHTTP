using System.Net;
using System.Net.Quic;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace GaudiHTTP.IntegrationTests.Client.Shared;

internal sealed record NginxContainerOptions(
    string Name,
    int InternalPort,
    bool EnableQuic,
    string SslDir,
    string HttpbinAlias,
    int HttpbinPort);

internal sealed class NginxTlsContainer : IAsyncDisposable
{
    private readonly NginxContainerOptions _options;
    private IContainer? _container;

    public NginxTlsContainer(NginxContainerOptions options)
    {
        _options = options;
    }

    public int Port { get; private set; }

    public bool IsAvailable { get; private set; }

    public async Task StartAsync(INetwork network)
    {
        if (_options.EnableQuic && !QuicConnection.IsSupported)
        {
            return;
        }

        try
        {
            var listenPort = _options.EnableQuic ? GetFreePort() : _options.InternalPort;

            var confDir = Path.Combine(Path.GetTempPath(), "GaudiHttp-nginx-ssl", _options.Name);
            Directory.CreateDirectory(confDir);
            var confPath = Path.Combine(confDir, "nginx.conf");
            await File.WriteAllTextAsync(confPath, BuildNginxConf(listenPort));

            var builder = new ContainerBuilder("macbre/nginx-http3:latest")
                .WithName(_options.Name)
                .WithNetwork(network)
                .WithResourceMapping(new FileInfo(confPath), "/etc/nginx/")
                .WithResourceMapping(new DirectoryInfo(_options.SslDir), "/etc/nginx/ssl/");

            if (_options.EnableQuic)
            {
                var portStr = listenPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                builder = builder
                    .WithPortBinding(listenPort, listenPort)
                    .WithPortBinding(portStr, portStr + "/udp")
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilInternalTcpPortIsAvailable(listenPort));
            }
            else
            {
                builder = builder
                    .WithPortBinding(listenPort, true)
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilInternalTcpPortIsAvailable(listenPort));
            }

            _container = builder.Build();
            await _container.StartAsync();

            if (_options.EnableQuic)
            {
                Port = listenPort;
                IsAvailable = await ProbeQuicAsync(listenPort);
            }
            else
            {
                Port = _container.GetMappedPublicPort(listenPort);
                IsAvailable = true;
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[NginxTlsContainer] {_options.Name} failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private string BuildNginxConf(int listenPort)
    {
        return $$"""
                 events {}
                 http {
                     upstream backend {
                         server {{_options.HttpbinAlias}}:{{_options.HttpbinPort}};
                     }
                     server {
                         listen {{listenPort}} ssl;
                         listen {{listenPort}} quic reuseport;
                         http2 on;

                         ssl_certificate     /etc/nginx/ssl/cert.pem;
                         ssl_certificate_key /etc/nginx/ssl/key.pem;
                         ssl_protocols       TLSv1.2 TLSv1.3;

                         add_header Alt-Svc 'h3=":{{listenPort}}"; ma=86400' always;

                         set $proxy_host $http_host;
                         if ($proxy_host = '') {
                             set $proxy_host $host:$server_port;
                         }

                         location / {
                             proxy_pass http://backend;
                             proxy_set_header Host $proxy_host;
                             proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                             proxy_set_header X-Forwarded-Proto $scheme;
                             proxy_set_header X-Forwarded-Host $proxy_host;
                         }
                     }
                 }
                 """;
    }

    private static int GetFreePort()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            int port;
            using (var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
            {
                tcp.Start();
                port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                tcp.Stop();
            }

            try
            {
                using var udp = new System.Net.Sockets.UdpClient(port, System.Net.Sockets.AddressFamily.InterNetwork);
                return port;
            }
            catch (System.Net.Sockets.SocketException)
            {
            }
        }

        using var fallback = new System.Net.Sockets.UdpClient(0, System.Net.Sockets.AddressFamily.InterNetwork);
        return ((IPEndPoint)fallback.Client.LocalEndPoint!).Port;
    }

    private static async Task<bool> ProbeQuicAsync(int port)
    {
        if (!QuicConnection.IsSupported)
        {
            await Console.Error.WriteLineAsync(
                "[NginxTlsContainer] QUIC probe skipped: QuicConnection.IsSupported=false");
            return false;
        }

        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync($"https://127.0.0.1:{port}/get", cts.Token);
            await Console.Error.WriteLineAsync(
                $"[NginxTlsContainer] QUIC probe: status={response.StatusCode} version={response.Version}");
            return response.Version == HttpVersion.Version30;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[NginxTlsContainer] QUIC probe failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}