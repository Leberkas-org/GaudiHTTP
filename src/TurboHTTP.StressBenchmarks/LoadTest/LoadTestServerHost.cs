using TurboHTTP.Benchmarks.Internal;

namespace TurboHTTP.Benchmarks.LoadTest;

// Child-process server host. Started by the driver as `loadtest --serve <kind>`, it hosts a single
// server (Turbo or Kestrel), prints "PORT=<h1.1 port>" to stdout, then idles until the parent kills it.
// Running the server in its own process is what makes GC.GetTotalAllocatedBytes measure SERVER-ONLY
// allocations — the load generator lives in the parent and never touches this process's heap.
internal static class LoadTestServerHost
{
    // Set only in the serve child. Passed to InitializeAsync so BenchmarkRoutes uses it for
    // /__allocreset and /__alloctypes; on the normal benchmark path it stays null and those endpoints no-op.
    internal static AllocationProfiler? ActiveProfiler;

    public static async Task RunAsync(string kind)
    {
        ActiveProfiler = new AllocationProfiler();
        ActiveProfiler.Arm();

        await using var server = await StartAsync(kind);

        Console.Out.WriteLine(string.Concat("PORT=", server.Http11Port.ToString(),
            ";H2=", server.Http20Port.ToString(),
            ";H3=", server.Http30Port.ToString()));
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
                await s.InitializeAsync(ActiveProfiler);
                return new TurboHost(s);
            }
            case "kestrel":
            {
                var s = new BenchmarkServer();
                await s.InitializeAsync(ActiveProfiler);
                return new KestrelHost(s);
            }
            default:
                throw new ArgumentException($"Unknown serve kind '{kind}' (expected 'turbo' or 'kestrel').");
        }
    }

    private interface IBenchmarkServerHost : IAsyncDisposable
    {
        int Http11Port { get; }
        int Http20Port { get; }
        int Http30Port { get; }
    }

    private sealed class TurboHost(TurboBenchmarkServer server) : IBenchmarkServerHost
    {
        public int Http11Port => server.Http11Port;
        public int Http20Port => server.Http20Port;
        public int Http30Port => server.Http30Port;

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    private sealed class KestrelHost(BenchmarkServer server) : IBenchmarkServerHost
    {
        public int Http11Port => server.Http11Port;
        public int Http20Port => server.Http20Port;
        public int Http30Port => server.Http30Port;

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
