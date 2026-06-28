using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Streams.Lifecycle;

namespace GaudiHTTP.Tests.Client;

/// <summary>
/// Unit regression guard for the lock-free pending-request list (commit 50918ff2). That change
/// tracked in-flight <see cref="GaudiHttpClient.SendAsync"/> calls in a hand-rolled lock-free linked
/// list whose per-request removal is O(N); under high concurrency it degrades to an O(N^2) walk plus
/// a single-point CompareExchange storm on the list head, collapsing throughput.
///
/// This drives <c>SendAsync</c> directly against an in-memory fake pipeline — no sockets, no Akka
/// stream graph, no transport — so the only thing exercised under concurrency is the in-flight
/// request bookkeeping. With O(1) tracking the batch finishes in well under a second; with the
/// regressed list it cannot finish within the deadline.
/// </summary>
public sealed class SendAsyncPendingRequestContentionSpec
{
    private const int Concurrency = 512;
    private const int TotalRequests = 200_000;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact(Timeout = 30_000)]
    public async Task SendAsync_tracks_in_flight_requests_without_quadratic_contention()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>(
            new UnboundedChannelOptions { SingleReader = true });
        var responses = Channel.CreateUnbounded<HttpResponseMessage>(
            new UnboundedChannelOptions { SingleWriter = true });

        // Infinite timeout + CancellationToken.None below selects SendAsync's no-CTS fast path,
        // so per-request work is purely the pending-request add/await/remove bookkeeping.
        var options = new GaudiRequestOptions(
            BaseAddress: new Uri("http://localhost"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: System.Threading.Timeout.InfiniteTimeSpan,
            Credentials: null,
            PreAuthenticate: false,
            UseProxy: false,
            Proxy: null);

        using var client = new GaudiHttpClient(
            requests.Writer,
            responses.Reader,
            options,
            new NamedClientConsumerRegistration(ActorRefs.Nobody, "test", Guid.NewGuid()));

        // Fake pipeline: drain the request channel and complete each PendingRequest immediately.
        using var pipelineCts = new CancellationTokenSource();
        var pipeline = Task.Run(async () =>
        {
            try
            {
                await foreach (var req in requests.Reader.ReadAllAsync(pipelineCts.Token))
                {
                    if (req.Options.TryGetValue(OptionsKey.Key, out var pending) &&
                        req.Options.TryGetValue(OptionsKey.VersionKey, out var version))
                    {
                        pending.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK), version);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        var dispatched = 0;
        var completed = 0;
        var failed = 0;

        async Task Worker()
        {
            while (Interlocked.Increment(ref dispatched) <= TotalRequests)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
                    using var response = await client.SendAsync(request, CancellationToken.None);
                    Interlocked.Increment(ref completed);
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
            }
        }

        var workers = new Task[Concurrency];
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Concurrency; i++)
        {
            workers[i] = Worker();
        }

        try
        {
            await Task.WhenAll(workers).WaitAsync(Deadline);
        }
        catch (TimeoutException)
        {
            pipelineCts.Cancel();
            Assert.Fail(
                $"SendAsync completed only {Volatile.Read(ref completed):N0}/{TotalRequests:N0} requests within " +
                $"{Deadline.TotalSeconds:F0}s at concurrency {Concurrency} — quadratic in-flight request-tracking " +
                $"regression (lock-free pending list, commit 50918ff2).");
        }

        sw.Stop();
        pipelineCts.Cancel();

        Assert.Equal(0, failed);
        Assert.Equal(TotalRequests, completed);
    }
}
