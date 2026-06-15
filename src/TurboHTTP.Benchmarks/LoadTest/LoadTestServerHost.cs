using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.LoadTest;

// Child-process server host. Started by the driver as `loadtest --serve <kind>`, it hosts a single
// server (Turbo or Kestrel), prints "PORT=<h1.1 port>" to stdout, then idles until the parent kills it.
// Running the server in its own process is what makes GC.GetTotalAllocatedBytes measure SERVER-ONLY
// allocations — the load generator lives in the parent and never touches this process's heap.
internal static class LoadTestServerHost
{
    public static async Task RunAsync(string kind)
    {
        await using var server = await StartAsync(kind);

        Console.Out.WriteLine($"PORT={server.Http11Port}");
        Console.Out.Flush();

        var done = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => done.TrySetResult();

        await done.Task;
    }

    private static async Task<IBenchmarkServerHost> StartAsync(string kind)
    {
        switch (kind)
        {
            case "turbo":
            {
                var s = new TurboBenchmarkServer();
                await s.InitializeAsync();
                return new TurboHost(s);
            }
            case "kestrel":
            {
                var s = new BenchmarkServer();
                await s.InitializeAsync();
                return new KestrelHost(s);
            }
            default:
                throw new ArgumentException($"Unknown serve kind '{kind}' (expected 'turbo' or 'kestrel').");
        }
    }

    private interface IBenchmarkServerHost : IAsyncDisposable
    {
        int Http11Port { get; }
    }

    private sealed class TurboHost(TurboBenchmarkServer server) : IBenchmarkServerHost
    {
        public int Http11Port => server.Http11Port;

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    private sealed class KestrelHost(BenchmarkServer server) : IBenchmarkServerHost
    {
        public int Http11Port => server.Http11Port;

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
