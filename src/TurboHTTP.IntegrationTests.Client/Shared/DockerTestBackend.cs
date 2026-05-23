using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;

namespace TurboHTTP.IntegrationTests.Shared;

internal sealed class DockerTestBackend : ITestBackend
{
    private const string NetworkName = "turbohttp-v2";
    private const int NginxInternalPort = 443;

    private INetwork? _network;
    private HttpbinContainer? _httpbin;
    private NginxTlsContainer? _nginxH2;
    private NginxTlsContainer? _nginxH3;

    public int HttpPort { get; private set; }
    public int HttpsPort { get; private set; }
    public int QuicPort { get; private set; }
    public bool IsQuicAvailable { get; private set; }
    public bool IsHttp10TlsSupported => false;

    public async Task StartAsync()
    {
        await RemoveStaleResourcesAsync();

        _network = new NetworkBuilder()
            .WithName(NetworkName)
            .Build();
        await _network.CreateAsync();

        CertificateManager.EnsureCertificatesExist();

        _httpbin = new HttpbinContainer();
        await _httpbin.StartAsync(_network);
        HttpPort = _httpbin.HttpPort;

        _nginxH2 = new NginxTlsContainer(new NginxContainerOptions(
            Name: "turbohttp-nginx-h2",
            InternalPort: NginxInternalPort,
            EnableQuic: false,
            SslDir: CertificateManager.SslDir,
            HttpbinAlias: HttpbinContainer.NetworkAlias,
            HttpbinPort: 8080));
        await _nginxH2.StartAsync(_network);
        HttpsPort = _nginxH2.Port;

        _nginxH3 = new NginxTlsContainer(new NginxContainerOptions(
            Name: "turbohttp-nginx-h3",
            InternalPort: NginxInternalPort,
            EnableQuic: true,
            SslDir: CertificateManager.SslDir,
            HttpbinAlias: HttpbinContainer.NetworkAlias,
            HttpbinPort: 8080));
        await _nginxH3.StartAsync(_network);
        QuicPort = _nginxH3.Port;
        IsQuicAvailable = _nginxH3.IsAvailable;
    }

    public async ValueTask DisposeAsync()
    {
        if (_nginxH3 is not null) await _nginxH3.DisposeAsync();
        if (_nginxH2 is not null) await _nginxH2.DisposeAsync();
        if (_httpbin is not null) await _httpbin.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
    }

    private static async Task RemoveStaleResourcesAsync()
    {
        var containerNames = new[] { "turbohttp-nginx-h3", "turbohttp-nginx-h2", HttpbinContainer.ContainerName };
        foreach (var name in containerNames)
        {
            await RunDockerQuietAsync($"rm -f {name}");
        }

        await RunDockerQuietAsync($"network rm {NetworkName}");
    }

    private static async Task RunDockerQuietAsync(string arguments)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is not null)
            {
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch
        {
            // noop
        }
    }
}
