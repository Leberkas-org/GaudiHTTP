# Servus.Akka.TestKit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a reusable TestKit library providing a configurable `TestConnectionStage` (GraphStage) that replaces real TCP/QUIC transport stages in Akka.Streams pipelines, enabling bidirectional test control without network I/O.

**Architecture:** A single `TestConnectionStage` with `FlowShape<ITransportOutbound, ITransportInbound>` shape. External test thread communicates via `Channel<T>` + `GetAsyncCallback` for thread safety. A fluent `TestConnectionStageBuilder` configures auto-behaviors (`AutoConnect`, `AutoDisconnect`, `OnOutbound<T>`) using an `IStageContext` interface for handler control. Supporting types `BehaviorStack`, `ActivityLog`, and `TransportBufferFactory` round out the library.

**Tech Stack:** .NET 10, Akka.Streams (GraphStage), Servus.Akka (transport types), System.Threading.Channels

---

### Task 1: Project Scaffolding

**Files:**
- Create: `src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
- Modify: `src/TurboHTTP.slnx`

- [ ] **Step 1: Create project file**

```xml
<!-- src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <IsPackable>true</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Akka.Streams"/>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="../Servus.Akka/Servus.Akka.csproj"/>
    </ItemGroup>

</Project>
```

Note: `TargetFramework`, `ImplicitUsings`, `Nullable` are inherited from `src/Directory.Build.props`. `IsPackable` defaults to false for projects with "Tests" in the name, but `TestKit` does not match that condition. Set explicitly to be clear.

- [ ] **Step 2: Add project to solution**

Add after the existing `Servus.Akka` entry in `src/TurboHTTP.slnx`:

```xml
  <Project Path="Servus.Akka.TestKit/Servus.Akka.TestKit.csproj" />
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj src/TurboHTTP.slnx
git commit -m "feat(testkit): scaffold Servus.Akka.TestKit project"
```

---

### Task 2: TransportBufferFactory

**Files:**
- Create: `src/Servus.Akka.TestKit/TransportBufferFactory.cs`

- [ ] **Step 1: Create TransportBufferFactory**

```csharp
// src/Servus.Akka.TestKit/TransportBufferFactory.cs
using Servus.Akka.Transport;

namespace Servus.Akka.TestKit;

public static class TransportBufferFactory
{
    public static TransportBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        var buf = TransportBuffer.Rent(len);
        data.AsSpan(0, len).CopyTo(buf.FullMemory.Span);
        buf.Length = len;
        return buf;
    }

    public static TransportBuffer FromSpan(ReadOnlySpan<byte> data)
    {
        var buf = TransportBuffer.Rent(data.Length);
        data.CopyTo(buf.FullMemory.Span);
        buf.Length = data.Length;
        return buf;
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/TransportBufferFactory.cs
git commit -m "feat(testkit): add TransportBufferFactory"
```

---

### Task 3: ActivityLog

**Files:**
- Create: `src/Servus.Akka.TestKit/ActivityLog.cs`

- [ ] **Step 1: Create ActivityLog with activity record types**

```csharp
// src/Servus.Akka.TestKit/ActivityLog.cs
using Servus.Akka.Transport;

namespace Servus.Akka.TestKit;

public abstract record Activity
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record OutboundReceived(int Index, ITransportOutbound Message) : Activity;

public sealed record InboundPushed(int Index, ITransportInbound Message) : Activity;

public sealed record HandlerInvoked(string HandlerType, ITransportOutbound Trigger) : Activity;

public sealed record StageCompleted : Activity;

public sealed record StageFailed(Exception Exception) : Activity;

public sealed class ActivityLog
{
    private readonly List<Activity> _entries = [];

    public IReadOnlyList<Activity> Entries => _entries;

    public void Record(Activity activity) => _entries.Add(activity);

    public IEnumerable<T> OfType<T>() where T : Activity
        => _entries.OfType<T>();

    public void Clear() => _entries.Clear();
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/ActivityLog.cs
git commit -m "feat(testkit): add ActivityLog with typed activity records"
```

---

### Task 4: BehaviorStack

**Files:**
- Create: `src/Servus.Akka.TestKit/BehaviorStack.cs`

Source: Adapted from `src/TurboHTTP.Tests.Shared/BehaviorStack.cs` — same API, new namespace, made public.

- [ ] **Step 1: Create BehaviorStack**

```csharp
// src/Servus.Akka.TestKit/BehaviorStack.cs
namespace Servus.Akka.TestKit;

public sealed class BehaviorStack<TIn, TOut>
{
    private readonly Func<TIn, TOut> _default;
    private readonly Stack<Func<TIn, TOut>> _stack = new();

    public BehaviorStack(Func<TIn, TOut> defaultBehavior)
    {
        _default = defaultBehavior;
    }

    public void Push(Func<TIn, TOut> behavior) => _stack.Push(behavior);

    public void PushConstant(TOut value) => Push(_ => value);

    public void PushError(Exception exception) => Push(_ => throw exception);

    public DelayGate<TIn, TOut> PushDelayed()
    {
        var gate = new DelayGate<TIn, TOut>();
        Push(gate.Execute);
        return gate;
    }

    public void PushOnce(Func<TIn, TOut> behavior)
    {
        Push(input =>
        {
            Pop();
            return behavior(input);
        });
    }

    public void Pop() => _stack.TryPop(out _);

    public TOut Apply(TIn input)
    {
        if (_stack.TryPeek(out var behavior))
        {
            return behavior(input);
        }

        return _default(input);
    }
}

public sealed class DelayGate<TIn, TOut>
{
    private readonly TaskCompletionSource<TOut> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TOut Execute(TIn _) => _tcs.Task.GetAwaiter().GetResult();

    public void Release(TOut value) => _tcs.TrySetResult(value);

    public void Fault(Exception exception) => _tcs.TrySetException(exception);
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/BehaviorStack.cs
git commit -m "feat(testkit): add BehaviorStack with DelayGate"
```

---

### Task 5: IStageContext

**Files:**
- Create: `src/Servus.Akka.TestKit/IStageContext.cs`

- [ ] **Step 1: Create IStageContext interface**

```csharp
// src/Servus.Akka.TestKit/IStageContext.cs
using Servus.Akka.Transport;

namespace Servus.Akka.TestKit;

public interface IStageContext
{
    void Push(ITransportInbound inbound);
    void Complete();
    void Fail(Exception ex);
    void ScheduleTimer(string key, TimeSpan delay);
    void CancelTimer(string key);
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/IStageContext.cs
git commit -m "feat(testkit): add IStageContext interface"
```

---

### Task 6: TestConnectionStage — Core Stage

This is the main stage. It uses `Channel<T>` for thread-safe cross-thread communication and `GetAsyncCallback` to safely marshal from the test thread into the Akka stage thread.

**Files:**
- Create: `src/Servus.Akka.TestKit/TestConnectionStage.cs`

- [ ] **Step 1: Create TestConnectionStage**

```csharp
// src/Servus.Akka.TestKit/TestConnectionStage.cs
using System.Collections.Concurrent;
using System.Threading.Channels;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace Servus.Akka.TestKit;

public sealed class TestConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly List<OutboundHandler> _handlers;
    private readonly ActivityLog? _activityLog;

    private readonly Channel<ITransportInbound> _inboundChannel =
        Channel.CreateUnbounded<ITransportInbound>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly Channel<ITransportOutbound> _outboundChannel =
        Channel.CreateUnbounded<ITransportOutbound>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

    private readonly ConcurrentBag<ITransportOutbound> _receivedOutbound = [];

    private int _outboundIndex;
    private int _inboundIndex;

    public Inlet<ITransportOutbound> In { get; } = new("TestConnection.In");
    public Outlet<ITransportInbound> Out { get; } = new("TestConnection.Out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    internal TestConnectionStage(List<OutboundHandler> handlers, ActivityLog? activityLog)
    {
        _handlers = handlers;
        _activityLog = activityLog;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    public void PushOnce(ITransportInbound message)
    {
        _inboundChannel.Writer.TryWrite(message);
    }

    public void PushInbound(ITransportInbound message)
    {
        _inboundChannel.Writer.TryWrite(message);
    }

    public async Task<ITransportOutbound> WaitForOutbound(CancellationToken ct = default)
    {
        return await _outboundChannel.Reader.ReadAsync(ct).ConfigureAwait(false);
    }

    public bool TryGetOutbound(out ITransportOutbound? message)
    {
        return _outboundChannel.Reader.TryRead(out message);
    }

    public IReadOnlyCollection<ITransportOutbound> ReceivedOutbound => _receivedOutbound;

    public static implicit operator Flow<ITransportOutbound, ITransportInbound, NotUsed>(
        TestConnectionStage stage)
        => Flow.FromGraph(stage);

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> AsFlow()
        => Flow.FromGraph(this);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, IStageContext
    {
        private readonly TestConnectionStage _stage;
        private readonly Queue<ITransportInbound> _pendingInbound = new();
        private bool _downstreamWaiting;
        private Action<ITransportInbound>? _onInboundCallback;

        public Logic(TestConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    var index = _stage._outboundIndex++;

                    _stage._receivedOutbound.Add(item);
                    _stage._outboundChannel.Writer.TryWrite(item);
                    _stage._activityLog?.Record(new OutboundReceived(index, item));

                    InvokeHandlers(item);

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }

                    TryPushNext();
                },
                onUpstreamFinish: () =>
                {
                    _stage._outboundChannel.Writer.TryComplete();

                    if (_pendingInbound.Count == 0 && !HasPendingChannelItems())
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex =>
                {
                    _stage._activityLog?.Record(new StageFailed(ex));
                    FailStage(ex);
                });

            SetHandler(stage.Out,
                onPull: () =>
                {
                    _downstreamWaiting = true;
                    TryPushNext();
                },
                onDownstreamFinish: _ =>
                {
                    if (!IsClosed(stage.In))
                    {
                        Cancel(stage.In);
                    }

                    _stage._outboundChannel.Writer.TryComplete();
                });
        }

        public override void PreStart()
        {
            _onInboundCallback = GetAsyncCallback<ITransportInbound>(inbound =>
            {
                _pendingInbound.Enqueue(inbound);
                TryPushNext();
            });

            Pull(_stage.In);
            ScheduleInboundPoll();
        }

        public override void PostStop()
        {
            _stage._activityLog?.Record(new StageCompleted());
            _stage._outboundChannel.Writer.TryComplete();
            _stage._inboundChannel.Writer.TryComplete();
        }

        private void ScheduleInboundPoll()
        {
            var callback = _onInboundCallback!;
            var reader = _stage._inboundChannel.Reader;

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var item in reader.ReadAllAsync())
                    {
                        callback(item);
                    }
                }
                catch (ChannelClosedException)
                {
                }
            });
        }

        private void TryPushNext()
        {
            if (!_downstreamWaiting)
            {
                return;
            }

            if (_pendingInbound.TryDequeue(out var next))
            {
                _downstreamWaiting = false;
                Push(_stage.Out, next);
            }
        }

        private void InvokeHandlers(ITransportOutbound item)
        {
            var itemType = item.GetType();
            foreach (var handler in _stage._handlers)
            {
                if (handler.MessageType.IsAssignableFrom(itemType))
                {
                    _stage._activityLog?.Record(
                        new HandlerInvoked(itemType.Name, item));
                    handler.Invoke(item, this);
                }
            }
        }

        private bool HasPendingChannelItems()
        {
            return _stage._inboundChannel.Reader.TryPeek(out _);
        }

        // IStageContext implementation
        void IStageContext.Push(ITransportInbound inbound)
        {
            var index = _stage._inboundIndex++;
            _stage._activityLog?.Record(new InboundPushed(index, inbound));
            _pendingInbound.Enqueue(inbound);
            TryPushNext();
        }

        void IStageContext.Complete() => CompleteStage();

        void IStageContext.Fail(Exception ex)
        {
            _stage._activityLog?.Record(new StageFailed(ex));
            FailStage(ex);
        }

        void IStageContext.ScheduleTimer(string key, TimeSpan delay)
            => ScheduleOnce(key, delay);

        void IStageContext.CancelTimer(string key)
            => CancelTimer(key);
    }

    internal sealed class OutboundHandler
    {
        public Type MessageType { get; }
        private readonly Action<ITransportOutbound, IStageContext> _handler;

        public OutboundHandler(Type messageType, Action<ITransportOutbound, IStageContext> handler)
        {
            MessageType = messageType;
            _handler = handler;
        }

        public void Invoke(ITransportOutbound message, IStageContext context)
            => _handler(message, context);
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/TestConnectionStage.cs
git commit -m "feat(testkit): add TestConnectionStage with bidirectional control"
```

---

### Task 7: TestConnectionStageBuilder

**Files:**
- Create: `src/Servus.Akka.TestKit/TestConnectionStageBuilder.cs`

- [ ] **Step 1: Create TestConnectionStageBuilder**

```csharp
// src/Servus.Akka.TestKit/TestConnectionStageBuilder.cs
using Servus.Akka.Transport;

namespace Servus.Akka.TestKit;

public sealed class TestConnectionStageBuilder
{
    private readonly List<TestConnectionStage.OutboundHandler> _handlers = [];
    private ActivityLog? _activityLog;

    public TestConnectionStageBuilder AutoConnect(ConnectionInfo? info = null)
    {
        var connectionInfo = info ?? new ConnectionInfo(null!, null!, null, null);
        return OnOutbound<ConnectTransport>((_, ctx) =>
            ctx.Push(new TransportConnected(connectionInfo)));
    }

    public TestConnectionStageBuilder AutoDisconnect()
    {
        return OnOutbound<DisconnectTransport>((msg, ctx) =>
            ctx.Push(new TransportDisconnected(msg.Reason)));
    }

    public TestConnectionStageBuilder OnOutbound<T>(Action<T, IStageContext> handler)
        where T : ITransportOutbound
    {
        _handlers.Add(new TestConnectionStage.OutboundHandler(
            typeof(T),
            (msg, ctx) => handler((T)msg, ctx)));
        return this;
    }

    public TestConnectionStageBuilder WithActivityLog(ActivityLog log)
    {
        _activityLog = log;
        return this;
    }

    public TestConnectionStage Build()
    {
        return new TestConnectionStage(new List<TestConnectionStage.OutboundHandler>(_handlers), _activityLog);
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Servus.Akka.TestKit/Servus.Akka.TestKit.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka.TestKit/TestConnectionStageBuilder.cs
git commit -m "feat(testkit): add TestConnectionStageBuilder with auto-behaviors"
```

---

### Task 8: Wire Up Test Project + Smoke Test

**Files:**
- Modify: `src/Servus.Akka.Tests/Servus.Akka.Tests.csproj`
- Create: `src/Servus.Akka.Tests/TestKit/TestConnectionStageSpec.cs`

- [ ] **Step 1: Add TestKit reference to test project**

Add to `src/Servus.Akka.Tests/Servus.Akka.Tests.csproj` in the `<ItemGroup>` with `<ProjectReference>`:

```xml
        <ProjectReference Include="..\Servus.Akka.TestKit\Servus.Akka.TestKit.csproj"/>
```

- [ ] **Step 2: Write smoke test — stage materializes and pushes TransportConnected via AutoConnect**

```csharp
// src/Servus.Akka.Tests/TestKit/TestConnectionStageSpec.cs
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;

namespace Servus.Akka.Tests.TestKit;

[Collection("TransportBuffer")]
public sealed class TestConnectionStageSpec : Akka.TestKit.Xunit.TestKit
{
    private readonly IMaterializer _materializer;

    public TestConnectionStageSpec()
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_materialize_and_deliver_TransportConnected_via_AutoConnect()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        var tcs = new TaskCompletionSource<ITransportInbound>();

        Source.Single<ITransportOutbound>(new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }))
            .Via(stage.AsFlow())
            .RunWith(Sink.ForEach<ITransportInbound>(msg => tcs.TrySetResult(msg)), _materializer);

        var result = await tcs.Task.WaitAsync(ct);
        Assert.IsType<TransportConnected>(result);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_capture_outbound_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        Source.Single<ITransportOutbound>(new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }))
            .Via(stage.AsFlow())
            .RunWith(Sink.Ignore<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance), _materializer);

        var outbound = await stage.WaitForOutbound(ct);
        Assert.IsType<ConnectTransport>(outbound);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_deliver_PushOnce_messages()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        var responseBytes = "HTTP/1.1 200 OK\r\n\r\n"u8.ToArray();
        stage.PushOnce(new TransportData(TransportBufferFactory.FromArray(responseBytes)));

        var results = new List<ITransportInbound>();
        var tcs = new TaskCompletionSource();

        Source.Single<ITransportOutbound>(new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }))
            .Via(stage.AsFlow())
            .RunWith(Sink.ForEach<ITransportInbound>(msg =>
            {
                results.Add(msg);
                if (results.Count >= 2)
                {
                    tcs.TrySetResult();
                }
            }), _materializer);

        await tcs.Task.WaitAsync(ct);
        Assert.IsType<TransportConnected>(results[0]);
        Assert.IsType<TransportData>(results[1]);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_support_bidirectional_control()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        var inboundResults = new List<ITransportInbound>();
        var tcs = new TaskCompletionSource();

        Source.From<ITransportOutbound>([
                new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }),
                new TransportData(TransportBufferFactory.FromArray([1, 2, 3]))
            ])
            .Via(stage.AsFlow())
            .RunWith(Sink.ForEach<ITransportInbound>(msg =>
            {
                inboundResults.Add(msg);
                if (inboundResults.Count >= 3)
                {
                    tcs.TrySetResult();
                }
            }), _materializer);

        var outbound = await stage.WaitForOutbound(ct);
        Assert.IsType<ConnectTransport>(outbound);

        var dataOut = await stage.WaitForOutbound(ct);
        Assert.IsType<TransportData>(dataOut);

        stage.PushInbound(new TransportData(TransportBufferFactory.FromArray([4, 5, 6])));
        stage.PushInbound(new TransportDisconnected(DisconnectReason.Graceful));

        await tcs.Task.WaitAsync(ct);
        Assert.IsType<TransportConnected>(inboundResults[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_record_activity_log()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new ActivityLog();
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .WithActivityLog(log)
            .Build();

        var tcs = new TaskCompletionSource<ITransportInbound>();

        Source.Single<ITransportOutbound>(new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }))
            .Via(stage.AsFlow())
            .RunWith(Sink.ForEach<ITransportInbound>(msg => tcs.TrySetResult(msg)), _materializer);

        await tcs.Task.WaitAsync(ct);

        Assert.Contains(log.Entries, e => e is OutboundReceived);
        Assert.Contains(log.Entries, e => e is HandlerInvoked);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_invoke_typed_OnOutbound_handlers()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<TransportData>((msg, ctx) =>
            {
                ctx.Push(new TransportData(TransportBufferFactory.FromArray([0xFF])));
            })
            .Build();

        var results = new List<ITransportInbound>();
        var tcs = new TaskCompletionSource();

        Source.From<ITransportOutbound>([
                new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }),
                new TransportData(TransportBufferFactory.FromArray([1, 2, 3]))
            ])
            .Via(stage.AsFlow())
            .RunWith(Sink.ForEach<ITransportInbound>(msg =>
            {
                results.Add(msg);
                if (results.Count >= 2)
                {
                    tcs.TrySetResult();
                }
            }), _materializer);

        await tcs.Task.WaitAsync(ct);
        Assert.IsType<TransportConnected>(results[0]);
        var responseData = Assert.IsType<TransportData>(results[1]);
        Assert.Equal(0xFF, responseData.Buffer.Span[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_support_implicit_flow_conversion()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        Flow<ITransportOutbound, ITransportInbound, Akka.NotUsed> flow = stage;

        var tcs = new TaskCompletionSource<ITransportInbound>();

        Source.Single<ITransportOutbound>(new ConnectTransport(new TcpTransportOptions { Host = "localhost", Port = 80 }))
            .Via(flow)
            .RunWith(Sink.ForEach<ITransportInbound>(msg => tcs.TrySetResult(msg)), _materializer);

        var result = await tcs.Task.WaitAsync(ct);
        Assert.IsType<TransportConnected>(result);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --project src/Servus.Akka.Tests/Servus.Akka.Tests.csproj -- -class "Servus.Akka.Tests.TestKit.TestConnectionStageSpec"`
Expected: All 7 tests pass.

- [ ] **Step 4: Verify existing tests still pass**

Run: `dotnet test --project src/Servus.Akka.Tests/Servus.Akka.Tests.csproj`
Expected: All tests pass, no regressions.

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka.Tests/Servus.Akka.Tests.csproj src/Servus.Akka.Tests/TestKit/TestConnectionStageSpec.cs
git commit -m "test(testkit): add TestConnectionStage smoke tests"
```

---

### Task 9: Full Solution Build Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build src/TurboHTTP.slnx`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 2: Run all Servus.Akka tests**

Run: `dotnet test --project src/Servus.Akka.Tests/Servus.Akka.Tests.csproj`
Expected: All tests pass.
