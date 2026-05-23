using System.Net;
using System.Net.Quic;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace TurboHTTP.IntegrationTests.Shared;

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

            var confDir = Path.Combine(Path.GetTempPath(), "turbohttp-nginx-ssl", _options.Name);
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
                builder = builder.WithPortBinding(listenPort, listenPort);
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

                         location / {
                             proxy_pass http://backend;
                             proxy_set_header Host $http_host;
                             proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                             proxy_set_header X-Forwarded-Proto $scheme;
                             proxy_set_header X-Forwarded-Host $http_host;
                         }
                     }
                 }
                 """;
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> ProbeQuicAsync(int port)
    {
        if (!QuicConnection.IsSupported)
        {
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
            var response = await client.GetAsync($"https://localhost:{port}/get", cts.Token);
            return response.IsSuccessStatusCode && response.Version == HttpVersion.Version30;
        }
        catch
        {
            return false;
        }
    }
}