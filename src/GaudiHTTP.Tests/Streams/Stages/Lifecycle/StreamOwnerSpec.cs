using Akka.Actor;
using Akka.TestKit.Xunit;
using GaudiHTTP.Client;
using GaudiHTTP.Streams;
using GaudiHTTP.Streams.Lifecycle;

namespace GaudiHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class StreamOwnerSpec : TestKit
{
    private static GaudiClientOptions DefaultClientOptions() => new()
    {
        BaseAddress = new Uri("http://localhost:8080")
    };

    private static PipelineDescriptor EmptyPipeline() => PipelineDescriptor.Empty;

    /// <summary>
    /// After MaxRetryAttempts stream failures, StreamOwner must stop itself.
    /// We inject StreamSinkCompleted failures directly (it is internal and test-visible)
    /// with a 1ms initial backoff so 11 rapid failures exhaust the retry budget
    /// without waiting for exponential backoff timers.
    /// </summary>
    [Fact(Timeout = 10000)]
    public void StreamOwner_should_stop_after_retry_exhaustion()
    {
        var owner = Sys.ActorOf(Props.Create(() => new StreamOwner(
            DefaultClientOptions(),
            EmptyPipeline(),
            transportOverride: null,
            initialBackoffOverride: TimeSpan.FromMilliseconds(1))));

        Watch(owner);

        // Inject 11 simulated stream failures (MaxRetryAttempts = 10).
        // Sending them in rapid succession increments _retryAttempts on each and
        // replaces the pending retry timer. After the 11th, _retryAttempts > 10
        // and the actor calls Stash.ClearStash() + Context.Stop(Self).
        var failEx = new InvalidOperationException("simulated stream failure");
        for (var i = 0; i <= 10; i++)
        {
            owner.Tell(new StreamOwner.StreamSinkCompleted(failEx));
        }

        ExpectTerminated(owner, TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// When Shutdown is sent before any stream is materialized, the actor stops immediately.
    /// With a wired-up WatchTermination, the KillSwitch signals completion which
    /// propagates through the stream and the actor stops cleanly.
    /// </summary>
    [Fact(Timeout = 5000)]
    public void StreamOwner_should_stop_on_shutdown_after_stream_drains()
    {
        var owner = Sys.ActorOf(Props.Create(() => new StreamOwner(
            DefaultClientOptions(),
            EmptyPipeline(),
            transportOverride: null)));

        Watch(owner);
        owner.Tell(new StreamOwner.Shutdown());

        ExpectTerminated(owner, TimeSpan.FromSeconds(4),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
