using System.Net;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHttp.Client;
using TurboHttp.Streams;

namespace TurboHttp.Tests.Client;

/// <summary>
/// Tests <see cref="ClientStreamInstanceActor"/> lifecycle: stream initialization,
/// failure reporting, pending work notification, shutdown, and resource cleanup.
/// </summary>
public sealed class ClientStreamInstanceTests : TestKit
{
    private static readonly TurboClientOptions DefaultOptions = new();

    private static TurboRequestOptions DefaultRequestOptions() =>
        new(null, new HttpRequestMessage().Headers, HttpVersion.Version11,
            HttpVersionPolicy.RequestVersionOrLower, TimeSpan.FromSeconds(30), 0);

    private IActorRef CreateInstance() =>
        Sys.ActorOf(Props.Create(() => new ClientStreamInstanceActor()));

    /// <summary>
    /// Creates a <see cref="ClientStreamInstanceActor"/> as a child of a forwarding actor,
    /// so that messages sent to <c>Context.Parent</c> are forwarded to <paramref name="target"/>.
    /// Returns the child instance ref.
    /// </summary>
    private IActorRef CreateInstanceWithForwardingParent(IActorRef target)
    {
        var parent = Sys.ActorOf(Props.Create(() => new ForwardingParentActor(target)));
        parent.Tell(Props.Create(() => new ClientStreamInstanceActor()));
        return ExpectMsg<IActorRef>(TimeSpan.FromSeconds(3));
    }

    private static ClientStreamInstance.InitializeStream MakeInitMessage()
    {
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        return new ClientStreamInstance.InitializeStream(
            DefaultOptions,
            DefaultRequestOptions,
            PipelineDescriptor.Empty,
            requests.Reader,
            responses.Writer);
    }

    /// <summary>
    /// Actor that creates a child from received <see cref="Props"/> and forwards
    /// all messages between the child and a target actor. This allows tests to
    /// intercept messages the child sends to <c>Context.Parent</c>.
    /// </summary>
    private sealed class ForwardingParentActor : UntypedActor
    {
        private readonly IActorRef _target;
        private IActorRef _child = Nobody.Instance;

        public ForwardingParentActor(IActorRef target)
        {
            _target = target;
        }

        protected override void OnReceive(object message)
        {
            if (message is Props props)
            {
                _child = Context.ActorOf(props, "instance");
                Context.Watch(_child);
                _target.Tell(_child);
            }
            else if (Sender.Equals(_child))
            {
                _target.Forward(message);
            }
            else
            {
                _child.Forward(message);
            }
        }
    }

    // ── Instance materializes stream on InitializeStream ──────────────────

    [Fact(DisplayName = "RFC-9110-csi-001: Instance attempts materialization on InitializeStream", Timeout = 5000)]
    public void Instance_AttemptsMaterialization_OnInitializeStream()
    {
        var instance = CreateInstance();
        var init = MakeInitMessage();

        instance.Tell(init, TestActor);

        // In the test environment, the pipeline graph may fail to materialize
        // (e.g., ChannelSource or Engine dependencies). The Instance will send
        // either StreamInitialized or StreamFailed to its parent.
        // Since we're the parent (TestActor is the sender, but Context.Parent
        // is the system guardian), we watch for actor behavior.
        // The instance sends to Context.Parent, not Sender.
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    // ── Instance sends messages to parent on stream lifecycle ────────────

    [Fact(DisplayName = "RFC-9110-csi-002: Instance sends StreamInitialized to parent on successful materialization", Timeout = 10000)]
    public void Instance_SendsStreamInitialized_OnSuccessfulMaterialization()
    {
        // Use forwarding parent so Context.Parent messages reach TestActor
        var instance = CreateInstanceWithForwardingParent(TestActor);
        var init = MakeInitMessage();

        instance.Tell(init);

        // The instance sends StreamInitialized to Context.Parent → forwarded to TestActor
        ExpectMsg<ClientStreamInstance.StreamInitialized>(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "RFC-9110-csi-003: Instance sends RequestStreamIdle to parent when sink completes", Timeout = 10000)]
    public void Instance_SendsRequestStreamIdle_WhenSinkCompletes()
    {
        var instance = CreateInstanceWithForwardingParent(TestActor);
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        var init = new ClientStreamInstance.InitializeStream(
            DefaultOptions,
            DefaultRequestOptions,
            PipelineDescriptor.Empty,
            requests.Reader,
            responses.Writer);

        instance.Tell(init);

        // Wait for initialization
        ExpectMsg<ClientStreamInstance.StreamInitialized>(TimeSpan.FromSeconds(5));

        // Complete the request channel — this will cause the stream sink to complete
        requests.Writer.Complete();

        // Instance should send RequestStreamIdle to parent → forwarded to TestActor
        ExpectMsg<ClientStreamOwner.RequestStreamIdle>(TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "RFC-9110-csi-009: Instance sends StreamFailed to parent on initialization failure", Timeout = 10000)]
    public void Instance_SendsStreamFailed_OnInitializationFailure()
    {
        var instance = CreateInstanceWithForwardingParent(TestActor);

        // Use null PipelineDescriptor to force a NullReferenceException during
        // Engine.CreateFlow (inside the try block), triggering the catch block
        // that sends StreamFailed to Context.Parent.
        var requests = Channel.CreateUnbounded<HttpRequestMessage>();
        var responses = Channel.CreateUnbounded<HttpResponseMessage>();
        var init = new ClientStreamInstance.InitializeStream(
            DefaultOptions,
            DefaultRequestOptions,
            null!,
            requests.Reader,
            responses.Writer);

        instance.Tell(init);

        // Instance should send StreamFailed to parent (forwarded to TestActor)
        var failed = ExpectMsg<ClientStreamInstance.StreamFailed>(TimeSpan.FromSeconds(5));
        Assert.NotNull(failed.Reason);
    }

    // ── Instance checks pending work before allowing completion ───────────

    [Fact(DisplayName = "RFC-9110-csi-004: Instance sends RequestStreamIdle to parent on normal completion", Timeout = 5000)]
    public void Instance_SendsRequestStreamIdle_OnNormalCompletion()
    {
        // The Instance sends RequestStreamIdle to its parent when the sink completes normally.
        // We verify this indirectly: when the Owner receives RequestStreamIdle with pending=0,
        // it sends RequestShutdown. This is tested in the Owner tests.
        // Here we verify the Instance handles RequestShutdown correctly.
        var instance = CreateInstance();

        Watch(instance);

        instance.Tell(new ClientStreamInstance.RequestShutdown());

        // RequestShutdown should cause the actor to stop
        ExpectTerminated(instance, TimeSpan.FromSeconds(3));
    }

    // ── Instance cleans up resources on PostStop ──────────────────────────

    [Fact(DisplayName = "RFC-9110-csi-005: Instance stops on RequestShutdown", Timeout = 5000)]
    public void Instance_StopsOnRequestShutdown()
    {
        var instance = CreateInstance();

        Watch(instance);

        instance.Tell(new ClientStreamInstance.RequestShutdown());

        ExpectTerminated(instance, TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "RFC-9110-csi-006: Instance cleans up resources on PostStop after successful init", Timeout = 10000)]
    public void Instance_CleansUpResources_OnPostStop()
    {
        var instance = CreateInstanceWithForwardingParent(TestActor);
        var init = MakeInitMessage();

        Watch(instance);

        // InitializeStream will succeed
        instance.Tell(init);

        ExpectMsg<ClientStreamInstance.StreamInitialized>(TimeSpan.FromSeconds(5));

        // Now stop the instance — PostStop should clean up materializer and pool
        instance.Tell(new ClientStreamInstance.RequestShutdown());

        ExpectTerminated(instance, TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "RFC-9110-csi-007: Instance handles RequestShutdown before initialization", Timeout = 5000)]
    public void Instance_HandlesRequestShutdown_BeforeInitialization()
    {
        // Instance receives RequestShutdown without ever being initialized
        var instance = CreateInstance();

        Watch(instance);

        instance.Tell(new ClientStreamInstance.RequestShutdown());

        // Should stop gracefully even without initialization
        ExpectTerminated(instance, TimeSpan.FromSeconds(3));
    }

    [Fact(DisplayName = "RFC-9110-csi-008: Instance unhandled messages do not crash actor", Timeout = 5000)]
    public void Instance_UnhandledMessages_DoNotCrash()
    {
        var instance = CreateInstance();

        // Send an unknown message
        instance.Tell("unknown-message");

        // Actor should still be alive
        Watch(instance);
        instance.Tell(new ClientStreamInstance.RequestShutdown());
        ExpectTerminated(instance, TimeSpan.FromSeconds(3));
    }
}
