using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TurboHTTP.IntegrationTests.Shared;

/// <summary>
/// Minimal forward proxy for integration tests.
/// Supports HTTP CONNECT tunneling (for HTTPS) and plain HTTP relay (using Host header).
/// Tracks connection metadata for test assertions.
/// </summary>
public sealed class ProxyServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _connections = [];
    private readonly object _lock = new();

    private int _connectRequestCount;
    private int _relayRequestCount;
    private string? _lastProxyAuthHeader;

    public int Port { get; }
    public int ConnectRequestCount => Volatile.Read(ref _connectRequestCount);
    public int RelayRequestCount => Volatile.Read(ref _relayRequestCount);
    public string? LastProxyAuthHeader => _lastProxyAuthHeader;

    /// <summary>
    /// Optional credentials required by the proxy. When set, the proxy returns 407
    /// if the Proxy-Authorization header is missing or doesn't match.
    /// </summary>
    public NetworkCredential? RequiredCredentials { get; set; }

    private ProxyServer(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    public static ProxyServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = new ProxyServer(listener, port);
        _ = server.AcceptLoopAsync();
        return server;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var task = HandleClientAsync(client);
                lock (_lock)
                {
                    _connections.Add(task);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            await using var clientStream = client.GetStream();
            var requestLine = await ReadLineAsync(clientStream, _cts.Token);
            if (requestLine is null)
            {
                return;
            }

            var headers = await ReadHeadersAsync(clientStream, _cts.Token);

            // Extract Proxy-Authorization if present
            foreach (var header in headers)
            {
                if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                {
                    _lastProxyAuthHeader = header["Proxy-Authorization:".Length..].Trim();
                }
            }

            if (requestLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
            {
                await HandleConnectAsync(requestLine, headers, clientStream);
            }
            else
            {
                await HandleRelayAsync(requestLine, headers, clientStream);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            client.Dispose();
        }
    }

    private async Task HandleConnectAsync(string requestLine, List<string> headers, NetworkStream clientStream)
    {
        // CONNECT host:port HTTP/1.1
        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            return;
        }

        // Check proxy auth if required
        if (!ValidateProxyAuth(headers))
        {
            await clientStream.WriteAsync(
                "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"proxy\"\r\n\r\n"u8.ToArray(), _cts.Token);
            return;
        }

        var hostPort = parts[1].Split(':');
        var host = hostPort[0];
        var port = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 443;

        Interlocked.Increment(ref _connectRequestCount);

        // Connect to the target
        using var target = new TcpClient();
        await target.ConnectAsync(host, port, _cts.Token);
        await using var targetStream = target.GetStream();

        // Send 200 to client
        await clientStream.WriteAsync(
            "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), _cts.Token);

        // Relay bidirectionally
        await RelayAsync(clientStream, targetStream);
    }

    private async Task HandleRelayAsync(string requestLine, List<string> headers, NetworkStream clientStream)
    {
        // Parse Host header to determine target
        string? hostHeader = null;
        foreach (var header in headers)
        {
            if (header.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                hostHeader = header["Host:".Length..].Trim();
                break;
            }
        }

        if (hostHeader is null)
        {
            return;
        }

        // Check proxy auth if required
        if (!ValidateProxyAuth(headers))
        {
            await clientStream.WriteAsync(
                "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"proxy\"\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), _cts.Token);
            return;
        }

        var hostParts = hostHeader.Split(':');
        var host = hostParts[0];
        var port = hostParts.Length > 1 ? int.Parse(hostParts[1]) : 80;

        Interlocked.Increment(ref _relayRequestCount);

        // Connect to target
        using var target = new TcpClient();
        await target.ConnectAsync(host, port, _cts.Token);
        await using var targetStream = target.GetStream();

        // Reconstruct and forward the original request
        var sb = new StringBuilder();
        sb.Append(requestLine).Append("\r\n");
        foreach (var header in headers)
        {
            // Strip proxy-specific headers
            if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.Append(header).Append("\r\n");
        }

        sb.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await targetStream.WriteAsync(requestBytes, _cts.Token);

        // Relay bidirectionally
        await RelayAsync(clientStream, targetStream);
    }

    private bool ValidateProxyAuth(List<string> headers)
    {
        if (RequiredCredentials is null)
        {
            return true;
        }

        string? authValue = null;
        foreach (var header in headers)
        {
            if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
            {
                authValue = header["Proxy-Authorization:".Length..].Trim();
                break;
            }
        }

        if (authValue is null)
        {
            return false;
        }

        var expected = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{RequiredCredentials.UserName}:{RequiredCredentials.Password}"));
        return authValue.Equals($"Basic {expected}", StringComparison.Ordinal);
    }

    private async Task RelayAsync(Stream a, Stream b)
    {
        var aToB = CopyAsync(a, b);
        var bToA = CopyAsync(b, a);
        await Task.WhenAny(aToB, bToA);
    }

    private async Task CopyAsync(Stream from, Stream to)
    {
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, _cts.Token);
                if (read == 0)
                {
                    break;
                }

                await to.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
                await to.FlushAsync(_cts.Token);
            }
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];
        var prev = (byte)0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                return sb.Length > 0 ? sb.ToString() : null;
            }

            if (prev == '\r' && buffer[0] == '\n')
            {
                sb.Length--; // remove trailing \r
                return sb.ToString();
            }

            prev = buffer[0];
            sb.Append((char)buffer[0]);
        }
    }

    private static async Task<List<string>> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var headers = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            headers.Add(line);
        }

        return headers;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();

        Task[] tasks;
        lock (_lock)
        {
            tasks = _connections.ToArray();
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best-effort cleanup
        }

        _cts.Dispose();
    }
}
