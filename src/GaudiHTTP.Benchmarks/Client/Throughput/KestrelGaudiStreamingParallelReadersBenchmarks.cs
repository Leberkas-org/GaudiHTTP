using System.Runtime.CompilerServices;
using GaudiHTTP.Client;
using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks.Client.Throughput;

/// <summary>
/// Diagnostic variant of <see cref="KestrelGaudiStreamingConcurrentBenchmarks"/> that isolates
/// <em>why</em> the channel (streaming) API trails <see cref="IGaudiHttpClient.SendAsync"/> in the
/// concurrent throughput report.
///
/// Two independent knobs separate the two competing explanations:
/// <list type="bullet">
///   <item><b><see cref="ReaderCount"/></b> — number of parallel tasks draining the single
///   <c>Responses</c> channel. The channel is created with <c>SingleWriter = true</c> but NOT
///   <c>SingleReader</c>, so multiple concurrent readers are legal. <c>ReaderCount = 1</c>
///   reproduces the production report's serial-consumer loop; higher values test whether that
///   serial loop — not the channel itself — is the bottleneck.</item>
///   <item><b><see cref="ReadBody"/></b> — whether each reader drains the response body. The
///   SendAsync benchmark only calls <c>EnsureSuccessStatusCode()</c> and never reads the body,
///   so part of its apparent win is simply skipped work. <c>ReadBody = false</c> aligns this
///   path with SendAsync; <c>ReadBody = true</c> matches the streaming report.</item>
/// </list>
///
/// The redundant <c>.WaitAsync(ct)</c> wrapper from the original streaming benchmark is removed —
/// <c>ReadAsByteArrayAsync(ct)</c> already honors the token, so the wrapper only added an
/// allocation per response.
///
/// Baseline equivalence: <c>ReaderCount = 1, ReadBody = true</c> is behaviourally identical to
/// <see cref="KestrelGaudiStreamingConcurrentBenchmarks"/> and serves as the calibration point.
/// </summary>
[WarmupCount(3)]
[IterationCount(10)]
public class KestrelGaudiStreamingParallelReadersBenchmarks : KestrelBaseClass
{
    [Params(512, 4096)]
    public int ConcurrencyLevel { get; set; }

    [Params(1, 4, 16)]
    public int ReaderCount { get; set; }

    [Params(false, true)]
    public bool ReadBody { get; set; }

    private ClientHelper _clientHelper = null!;

    [GlobalSetup]
    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();
        _clientHelper = ClientHelper.CreateStreamingClient(BaseAddress, HttpVersionValue);
        await WarmupRequest();
    }

    [GlobalCleanup]
    public override async Task GlobalCleanup()
    {
        await _clientHelper.DisposeAsync();
        await base.GlobalCleanup();
    }

    [IterationCleanup]
    public void DrainResponses()
    {
        try
        {
            while (_clientHelper.Client.Responses.TryRead(out var stale))
            {
                stale.Dispose();
            }
        }
        catch
        {
            // Channel may be in a faulted state — ignore during cleanup.
        }
    }

    public override async Task WarmupRequest()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, LightUri);
        using var response = await _clientHelper.Client.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
    }

    [Benchmark]
    public Task ConcurrentRequests_Light() => StreamRequests(LightUri, HttpMethod.Get);

    [Benchmark]
    public Task ConcurrentRequests_Heavy() => StreamRequests(UploadUri, HttpMethod.Post);

    private async Task StreamRequests(Uri uri, HttpMethod method)
    {
        var client = _clientHelper.Client;
        var count = ConcurrencyLevel;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        // Signals the reader loops to stop once all responses have been received, so parked
        // readers blocked in WaitToReadAsync unblock instead of hanging until the timeout.
        // A TaskCompletionSource (not token cancellation) is used deliberately: cancelling a
        // token would throw OperationCanceledException in every parked reader, and those throws
        // land inside the measured op — at ReaderCount=16 that is 15 exceptions per iteration,
        // polluting the very measurement this benchmark exists to take.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Drain stale responses from prior iterations before starting.
        while (client.Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }

        // Cap in-flight requests to avoid unbounded memory growth at high CL.
        // 512 matches Kestrel's MaxStreamsPerConnection — more just queues.
        using var throttle = new SemaphoreSlim(Math.Min(count, 512));

        // Shared across all reader loops; boxed so the async state machines can capture it
        // (an async method cannot take a `ref int` parameter).
        var received = new StrongBox<int>(0);

        try
        {
            var writer = Task.Run(async () =>
            {
                for (var i = 0; i < count; i++)
                {
                    await throttle.WaitAsync(ct);
                    var request = new HttpRequestMessage(method, uri);
                    if (method == HttpMethod.Post)
                    {
                        request.Content = new ByteArrayContent(HeavyPayload);
                    }

                    await client.Requests.WriteAsync(request, ct);
                }
            }, ct);

            var readers = new Task[ReaderCount];
            for (var r = 0; r < ReaderCount; r++)
            {
                readers[r] = DrainLoop(client, count, throttle, done, received, ct);
            }

            await Task.WhenAll(readers);
            await writer.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainLoop(
        IGaudiHttpClient client,
        int count,
        SemaphoreSlim throttle,
        TaskCompletionSource done,
        StrongBox<int> received,
        CancellationToken ct)
    {
        try
        {
            while (Volatile.Read(ref received.Value) < count)
            {
                var wait = client.Responses.WaitToReadAsync(ct);
                if (!wait.IsCompletedSuccessfully)
                {
                    // Channel is empty — park until either data arrives or all responses are in.
                    // Racing against `done.Task` lets surplus readers exit without an exception.
                    var waitTask = wait.AsTask();
                    if (await Task.WhenAny(waitTask, done.Task) == done.Task)
                    {
                        return;
                    }

                    if (!await waitTask)
                    {
                        break;
                    }
                }
                else if (!wait.Result)
                {
                    break;
                }

                while (client.Responses.TryRead(out var response))
                {
                    if (ReadBody)
                    {
                        await response.Content.ReadAsByteArrayAsync(ct);
                    }

                    response.Dispose();
                    throttle.Release();

                    if (Interlocked.Increment(ref received.Value) >= count)
                    {
                        done.TrySetResult();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
