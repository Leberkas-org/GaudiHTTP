using System.Diagnostics;

namespace GaudiHTTP.Benchmarks.Internal;

/// <summary>
/// Runs the Kestrel <see cref="BenchmarkServer"/> in a SEPARATE process so the measuring process
/// (a BenchmarkDotNet benchmark or a client trace) contains only client-side allocation — the
/// in-process server otherwise both contaminates the totals and throttles the client by sharing the
/// thread pool. <see cref="StartAsync"/> launches the child and reads the listening ports; the child
/// entry point is <see cref="RunServerAsync"/> (invoked via the <c>--bench-server</c> argument).
/// </summary>
internal sealed class ServerProcessHandle : IDisposable
{
    public const string ServerArgument = "--bench-server";
    private const string PortsPrefix = "PORTS=";

    private readonly Process _process;

    public int Http11Port { get; }
    public int Http20Port { get; }
    public int Http30Port { get; }
    public bool QuicAvailable { get; }

    private ServerProcessHandle(Process process, int http11, int http20, int http30, bool quicAvailable)
    {
        _process = process;
        Http11Port = http11;
        Http20Port = http20;
        Http30Port = http30;
        QuicAvailable = quicAvailable;
    }

    public int PortFor(string httpVersion) => httpVersion switch
    {
        "1.1" => Http11Port,
        "2.0" => Http20Port,
        _ => Http30Port,
    };

    public static async Task<ServerProcessHandle> StartAsync()
    {
        // Launch THIS assembly's dll via the dotnet host so the child runs our Main (and its
        // --bench-server branch) regardless of how the measuring process itself was started — a
        // BenchmarkDotNet-generated host has its own entry assembly, so re-launching the host exe or
        // GetEntryAssembly() would not carry the --bench-server handling.
        var dll = typeof(ServerProcessHandle).Assembly.Location;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(ServerArgument);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the benchmark server child process.");

        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.StartsWith(PortsPrefix, StringComparison.Ordinal))
            {
                var parts = line[PortsPrefix.Length..].Split(',');
                return new ServerProcessHandle(
                    process,
                    int.Parse(parts[0]),
                    int.Parse(parts[1]),
                    int.Parse(parts[2]),
                    bool.Parse(parts[3]));
            }
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        throw new InvalidOperationException("Server child process exited before reporting its ports.");
    }

    /// <summary>
    /// Child-process entry point: starts the Kestrel server, emits a single
    /// <c>PORTS=h11,h20,h30,quic</c> line on stdout, then runs until the parent kills the tree.
    /// </summary>
    public static async Task RunServerAsync()
    {
        await using var server = new BenchmarkServer();
        await server.InitializeAsync();

        Console.WriteLine($"{PortsPrefix}{server.Http11Port},{server.Http20Port},{server.Http30Port},{server.IsQuicAvailable}");
        await Console.Out.FlushAsync();

        await Task.Delay(Timeout.Infinite);
    }

    public void Dispose()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
    }
}
