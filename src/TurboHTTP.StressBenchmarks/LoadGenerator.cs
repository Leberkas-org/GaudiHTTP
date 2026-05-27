using System.Diagnostics;

namespace TurboHTTP.StressBenchmarks;

public static class LoadGenerator
{
    public static async Task RunAsync(
        Uri baseUri,
        StressRunConfig config,
        Func<HttpClient, Uri, Task<HttpResponseMessage>> requestFunc,
        Action<RequestResult> onResult,
        CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = config.Concurrency,
            PooledConnectionLifetime = config.DisableKeepAlive
                ? TimeSpan.Zero
                : TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };

        var tasks = new Task[config.Concurrency];
        for (var i = 0; i < config.Concurrency; i++)
        {
            tasks[i] = WorkerLoop(client, baseUri, requestFunc, onResult, ct);
        }

        await Task.WhenAll(tasks);
    }

    private static async Task WorkerLoop(
        HttpClient client,
        Uri baseUri,
        Func<HttpClient, Uri, Task<HttpResponseMessage>> requestFunc,
        Action<RequestResult> onResult,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                using var response = await requestFunc(client, baseUri);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                onResult(new RequestResult((int)response.StatusCode, elapsed, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                onResult(new RequestResult(0, elapsed, ex));
            }
        }
    }
}
