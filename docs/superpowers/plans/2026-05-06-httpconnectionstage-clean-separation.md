# HttpConnectionStage Clean Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor all four HTTP connection stages so the stage contains zero protocol logic — only mechanical Pull/Push/queue — and all decisions live in protocol-specific StateMachines.

**Architecture:** A single generic `HttpConnectionStageLogic<TSM>` replaces four per-protocol inner Logic classes. Each protocol keeps a thin outer `GraphStage` shell. Two new interfaces (`IHttpStateMachine`, `IStageOperations`) define the contract. StateMachines absorb all transport lifecycle, reconnect, timer, and frame-processing logic from the stages.

**Tech Stack:** C# 12, Akka.Streams (GraphStage, TimerGraphStageLogic), xUnit v3

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `src/TurboHTTP/Streams/Stages/IHttpStateMachine.cs` | Interface for protocol state machines |
| `src/TurboHTTP/Streams/Stages/HttpConnectionStageLogic.cs` | Generic base stage logic (the pump) |
| `src/TurboHTTP.Tests/Stages/MockStageOperations.cs` | Test double capturing all SM→stage callbacks |
| `src/TurboHTTP.Tests/Stages/MockStateMachine.cs` | Test double for verifying stage pump mechanics |
| `src/TurboHTTP.Tests/Stages/HttpConnectionStageLogicSpec.cs` | Stage pump unit tests |

### Modified Files
| File | Change |
|------|--------|
| `src/TurboHTTP/Streams/Stages/IStageOperations.cs` | Add `OnScheduleTimer`, `OnCancelTimer`, `OnComplete`, `OnFail`; remove `OnReconnectFailed` |
| `src/TurboHTTP/Streams/Stages/Http10ConnectionStage.cs` | Thin shell: outer GraphStage + factory delegate only |
| `src/TurboHTTP/Streams/Stages/Http11ConnectionStage.cs` | Thin shell: outer GraphStage + factory delegate only |
| `src/TurboHTTP/Streams/Stages/Http20ConnectionStage.cs` | Thin shell: outer GraphStage + factory delegate only |
| `src/TurboHTTP/Streams/Stages/Http30ConnectionStage.cs` | Thin shell: outer GraphStage + factory delegate only |
| `src/TurboHTTP/Protocol/Http10/StateMachine.cs` | Implement `IHttpStateMachine`, absorb stage logic |
| `src/TurboHTTP/Protocol/Http11/StateMachine.cs` | Implement `IHttpStateMachine`, absorb stage logic |
| `src/TurboHTTP/Protocol/Http2/StateMachine.cs` | Implement `IHttpStateMachine`, absorb stage logic |
| `src/TurboHTTP/Protocol/Http3/StateMachine.cs` | Implement `IHttpStateMachine`, absorb stage logic |

---

## Task 1: Define IHttpStateMachine and Update IStageOperations

**Files:**
- Create: `src/TurboHTTP/Streams/Stages/IHttpStateMachine.cs`
- Modify: `src/TurboHTTP/Streams/Stages/IStageOperations.cs`

- [ ] **Step 1: Create `IHttpStateMachine` interface**

```csharp
// src/TurboHTTP/Streams/Stages/IHttpStateMachine.cs
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal interface IHttpStateMachine
{
    bool CanAcceptRequest { get; }

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void Cleanup();
}
```

- [ ] **Step 2: Update `IStageOperations` — add timer/lifecycle, remove `OnReconnectFailed`**

Replace the contents of `src/TurboHTTP/Streams/Stages/IStageOperations.cs` with:

```csharp
using Akka.Event;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(ITransportOutbound item);
    void OnWarning(string message);
    void OnScheduleTimer(string name, TimeSpan duration);
    void OnCancelTimer(string name);
    void OnComplete();
    void OnFail(Exception exception);
    ILoggingAdapter Log { get; }
}
```

- [ ] **Step 3: Verify the solution compiles**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx`

Expected: Build errors in all four stages (they still implement `OnReconnectFailed`) and all four SMs (they don't implement `IHttpStateMachine` yet). This is expected — we'll fix them in subsequent tasks.

**Note:** If there are too many errors to proceed, temporarily keep `OnReconnectFailed` in `IStageOperations` and remove it later in Task 3. The important thing is that the new members compile.

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/IHttpStateMachine.cs src/TurboHTTP/Streams/Stages/IStageOperations.cs
git commit -m "feat: define IHttpStateMachine interface and expand IStageOperations"
```

---

## Task 2: Create HttpConnectionStageLogic<TSM>

**Files:**
- Create: `src/TurboHTTP/Streams/Stages/HttpConnectionStageLogic.cs`

- [ ] **Step 1: Create the generic stage logic**

```csharp
// src/TurboHTTP/Streams/Stages/HttpConnectionStageLogic.cs
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal sealed class HttpConnectionStageLogic<TSM> : TimerGraphStageLogic, IStageOperations
    where TSM : IHttpStateMachine
{
    private readonly Inlet<ITransportInbound> _inServer;
    private readonly Outlet<HttpResponseMessage> _outResponse;
    private readonly Inlet<HttpRequestMessage> _inApp;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<ITransportOutbound> _outboundQueue = new();
    private readonly Queue<HttpResponseMessage> _responseQueue = new();

    public HttpConnectionStageLogic(
        GraphStage<ConnectionShape> stage,
        Func<IStageOperations, TSM> smFactory) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inServer = shape.InServer;
        _outResponse = shape.OutResponse;
        _inApp = shape.InApp;
        _outNetwork = shape.OutNetwork;

        _sm = smFactory(this);

        SetHandler(_inServer, onPush: OnServerPush,
            onUpstreamFinish: () => _sm.OnUpstreamFinished(),
            onUpstreamFailure: ex =>
            {
                _sm.OnUpstreamFinished();
            });

        SetHandler(_outResponse, onPull: () =>
        {
            if (_responseQueue.Count > 0)
            {
                Push(_outResponse, _responseQueue.Dequeue());
                return;
            }

            if (!HasBeenPulled(_inServer) && !IsClosed(_inServer))
            {
                Pull(_inServer);
            }
        });

        SetHandler(_inApp, onPush: () =>
        {
            var request = Grab(_inApp);
            _sm.OnRequest(request);
            TryPullRequest();
        },
        onUpstreamFinish: () =>
        {
            if (!_sm.CanAcceptRequest)
            {
                return;
            }

            _sm.OnUpstreamFinished();
        },
        onUpstreamFailure: ex =>
        {
            _sm.OnUpstreamFinished();
        });

        SetHandler(_outNetwork, onPull: OnNetworkPull);
    }

    private void OnServerPush()
    {
        var item = Grab(_inServer);
        _sm.DecodeServerData(item);
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            return;
        }

        TryPullRequest();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is string name)
        {
            _sm.OnTimerFired(name);
        }
    }

    public override void PreStart()
    {
        _sm.PreStart();
    }

    // --- IStageOperations implementation ---

    void IStageOperations.OnResponse(HttpResponseMessage response)
    {
        _responseQueue.Enqueue(response);
        TryPushResponse();
    }

    void IStageOperations.OnOutbound(ITransportOutbound item)
    {
        _outboundQueue.Enqueue(item);
        TryPushOutbound();
    }

    void IStageOperations.OnWarning(string message)
    {
        Log.Warning(message);
    }

    void IStageOperations.OnScheduleTimer(string name, TimeSpan duration)
    {
        ScheduleOnce(name, duration);
    }

    void IStageOperations.OnCancelTimer(string name)
    {
        CancelTimer(name);
    }

    void IStageOperations.OnComplete()
    {
        CompleteStage();
    }

    void IStageOperations.OnFail(Exception exception)
    {
        FailStage(exception);
    }

    ILoggingAdapter IStageOperations.Log => Log;

    // --- Mechanical helpers ---

    private void TryPushResponse()
    {
        if (_responseQueue.Count > 0 && IsAvailable(_outResponse))
        {
            Push(_outResponse, _responseQueue.Dequeue());
        }
    }

    private void TryPushOutbound()
    {
        if (_outboundQueue.Count > 0 && IsAvailable(_outNetwork))
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
        }
    }

    private void TryPullRequest()
    {
        if (_sm.CanAcceptRequest
            && !HasBeenPulled(_inApp)
            && !IsClosed(_inApp))
        {
            Pull(_inApp);
        }
    }

    public override void PostStop()
    {
        while (_outboundQueue.Count > 0)
        {
            if (_outboundQueue.Dequeue() is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        while (_responseQueue.Count > 0)
        {
            _responseQueue.Dequeue().Dispose();
        }

        _sm.Cleanup();
    }
}
```

**Design notes:**
- Uses `TimerGraphStageLogic` (not `GraphStageLogic`) so all protocols get timer support. Http10/11 SMs simply never call `OnScheduleTimer`.
- `onUpstreamFinish` for `_inServer` delegates to `_sm.OnUpstreamFinished()` — the SM decides whether to complete, fail, or ignore.
- `onUpstreamFinish` for `_inApp` checks `CanAcceptRequest` first — if there are in-flight requests, the SM handles lifecycle via its own logic. Otherwise delegates to SM.
- `OnServerPush` is a single line: `_sm.DecodeServerData(Grab(_inServer))`. All transport signal routing, frame processing, reconnect decisions happen inside the SM.
- `OnNetworkPull` dequeues one outbound item. No preface handling — preface is emitted by the SM via `OnOutbound` during `DecodeServerData(TransportConnected)`.

- [ ] **Step 2: Verify the file compiles in isolation**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx 2>&1 | Select-String "HttpConnectionStageLogic"`

Expected: No errors for this file specifically (errors in other files are expected since stages haven't been migrated yet).

- [ ] **Step 3: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/HttpConnectionStageLogic.cs
git commit -m "feat: add generic HttpConnectionStageLogic<TSM> base stage"
```

---

## Task 3: Migrate Http11 StateMachine (Reference Implementation)

Http11 is the best reference because it has pipelining complexity but no timers or multiplexing. We migrate SM first, then stage.

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http11/StateMachine.cs`

- [ ] **Step 1: Add `IHttpStateMachine` implementation to Http11 SM**

Add the interface to the class declaration:

```csharp
internal sealed class StateMachine : IHttpStateMachine
```

- [ ] **Step 2: Add `OnRequest` method**

This wraps the existing `EncodeRequest`:

```csharp
void IHttpStateMachine.OnRequest(HttpRequestMessage request)
{
    Tracing.For("Protocol").Debug(_ops, "HTTP/1.1 → {0} {1}", request.Method, request.RequestUri);
    EncodeRequest(request);
}
```

Add `using TurboHTTP.Diagnostics;` to the file imports if not already present.

- [ ] **Step 3: Add `DecodeServerData(ITransportInbound)` overload**

This absorbs all the transport signal handling currently in `Http11ConnectionStage.Logic.OnServerPush()`:

```csharp
void IHttpStateMachine.DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            Tracing.For("Protocol").Debug(_ops, "HTTP/1.1 connected");
            OnConnectionRestored();
            return;

        case TransportDisconnected when IsReconnecting:
            OnReconnectAttemptFailed();
            return;

        case TransportDisconnected when HasInFlightRequests:
            Tracing.For("Protocol").Warning(_ops, "HTTP/1.1 closed, {0} pending", PendingRequestCount);
            StartReconnect();
            return;

        case TransportDisconnected:
            _ops.OnComplete();
            return;
    }

    if (data is not TransportData { Buffer: var buffer })
    {
        return;
    }

    try
    {
        DecodeServerData(buffer);
    }
    catch (HttpRequestException ex)
    {
        _ops.OnWarning(string.Concat("Http11: ", ex.Message));
        _ops.OnComplete();
    }
}
```

**Key changes from the old stage logic:**
- `CompleteStage()` → `_ops.OnComplete()`
- `FailStage(ex)` → `_ops.OnFail(ex)`
- Transport signal inspection moved from stage's `OnServerPush` into SM
- Reconnect attempt failure no longer sets a `_reconnectFailed` flag on the stage — instead `OnReconnectAttemptFailed()` calls `_ops.OnFail()` directly when max attempts reached

- [ ] **Step 4: Update `OnReconnectAttemptFailed` to call `_ops.OnFail` instead of `_ops.OnReconnectFailed`**

Find the existing `OnReconnectAttemptFailed()` method and update it so that when max attempts are exceeded, it calls `_ops.OnFail(new HttpRequestException(...))` instead of `_ops.OnReconnectFailed()`:

```csharp
public void OnReconnectAttemptFailed()
{
    _reconnectAttempts++;
    if (_reconnectAttempts >= _options.MaxReconnectAttempts)
    {
        _ops.OnWarning(string.Concat("HTTP/1.1 reconnect failed after max attempts — discarding ",
            PendingRequestCount.ToString(), " in-flight request(s)."));
        _ops.OnFail(new HttpRequestException(
            "TurboHTTP: HTTP/1.1 reconnect failed after max attempts."));
        return;
    }

    // Re-emit ConnectTransport for next attempt
    _ops.OnOutbound(new ConnectTransport(_transportOptions!));
}
```

Review the existing method body — it likely already has the reconnect logic. The key change is replacing `_ops.OnReconnectFailed()` with `_ops.OnFail(...)`.

- [ ] **Step 5: Add `OnUpstreamFinished` method**

This absorbs the `onUpstreamFinish` handler from the stage's `_inServer` handler:

```csharp
void IHttpStateMachine.OnUpstreamFinished()
{
    if (IsReconnecting)
    {
        _ops.OnWarning(string.Concat(
            "HTTP/1.1 transport closed during reconnect — discarding ",
            PendingRequestCount.ToString(), " buffered request(s)."));
        _ops.OnComplete();
        return;
    }

    if (TryDecodeEof())
    {
        return;
    }

    HandleOrphanedRequests();
    _ops.OnComplete();
}
```

- [ ] **Step 6: Add `OnTimerFired` method (no-op for Http11)**

```csharp
void IHttpStateMachine.OnTimerFired(string name)
{
    // HTTP/1.1 uses no timers
}
```

- [ ] **Step 7: Verify the SM compiles**

Run: `dotnet build --configuration Release ./src/TurboHTTP/Protocol/Http11/ 2>&1`

Expected: Compiles. There may be warnings about unused public methods (`StartReconnect`, `OnConnectionRestored` etc. are now called internally).

- [ ] **Step 8: Commit**

```bash
git add src/TurboHTTP/Protocol/Http11/StateMachine.cs
git commit -m "feat(h11): implement IHttpStateMachine on Http11 StateMachine"
```

---

## Task 4: Migrate Http11ConnectionStage to Thin Shell

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Http11ConnectionStage.cs`

- [ ] **Step 1: Replace the entire inner Logic class with the factory delegate**

Replace the full contents of `Http11ConnectionStage.cs`:

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http11ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http11Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http11Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http11Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http11Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http11ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        var memoryBuffer = inheritedAttributes.GetAttribute(
            new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));

        return new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(ops, _options, memoryBuffer.Initial, memoryBuffer.Max));
    }
}
```

**Key changes:**
- Entire `Logic` inner class deleted (was ~250 lines)
- `CreateLogic` now returns `HttpConnectionStageLogic<StateMachine>` with a factory delegate
- `inheritedAttributes` passed into `CreateLogic` is used to extract `MemoryBuffer` before constructing the SM
- Port definitions and Shape remain identical

- [ ] **Step 2: Build the solution**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx 2>&1 | Select-String "error CS"`

Expected: Http11 related errors should be gone. Other stages still have errors (expected).

- [ ] **Step 3: Run Http11 stream tests**

Run: `dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http11"`

Expected: All Http11 stream tests pass. If any fail, debug the SM's `DecodeServerData(ITransportInbound)` method — the issue is likely in how transport signals trigger pull/push sequences that the SM now needs to handle via callbacks.

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/Http11ConnectionStage.cs
git commit -m "refactor(h11): replace stage logic with generic HttpConnectionStageLogic"
```

---

## Task 5: Migrate Http10 StateMachine and Stage

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http10/StateMachine.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Http10ConnectionStage.cs`

Http10 is nearly identical to Http11 but simpler (single in-flight request, no pipelining). Follow the same pattern.

- [ ] **Step 1: Add `IHttpStateMachine` to Http10 SM**

```csharp
internal sealed class StateMachine : IHttpStateMachine
```

- [ ] **Step 2: Add `OnRequest` method**

```csharp
void IHttpStateMachine.OnRequest(HttpRequestMessage request)
{
    Tracing.For("Protocol").Debug(_ops, "HTTP/1.0 → {0} {1}", request.Method, request.RequestUri);
    EncodeRequest(request);
}
```

- [ ] **Step 3: Add `DecodeServerData(ITransportInbound)` overload**

Same pattern as Http11. Differences from Http11:
- Uses `HasInFlightRequest` (singular, not `HasInFlightRequests`)
- Uses `StartReconnect()` (same as Http11)
- `DecodeServerData` on the existing SM takes `ITransportInbound` directly (not `TransportBuffer`) — check the existing signature and adapt. The existing `DecodeServerData` may already accept `ITransportInbound` — if so, rename the existing method to `DecodeTransportData` and have the new `IHttpStateMachine.DecodeServerData` dispatch to it.

```csharp
void IHttpStateMachine.DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            Tracing.For("Protocol").Debug(_ops, "HTTP/1.0 connected");
            OnConnectionRestored();
            return;

        case TransportDisconnected when IsReconnecting:
            OnReconnectAttemptFailed();
            return;

        case TransportDisconnected when HasInFlightRequest:
            Tracing.For("Protocol").Warning(_ops, "HTTP/1.0 closed, {0} pending", PendingRequestCount);
            StartReconnect();
            return;

        case TransportDisconnected:
            _ops.OnComplete();
            return;
    }

    try
    {
        DecodeServerData(data); // call existing method
    }
    catch (HttpRequestException ex)
    {
        _ops.OnWarning(string.Concat("Http10: ", ex.Message));
        _ops.OnComplete();
    }
}
```

**Important:** The existing `DecodeServerData` on Http10 SM already takes `ITransportInbound`. This creates a signature conflict. Rename the existing method to `DecodeTransportBuffer` (or similar) and have the `IHttpStateMachine` explicit implementation call it. Alternatively, if the existing method already handles `TransportData` extraction internally, refactor so the new method wraps it properly.

- [ ] **Step 4: Add `OnUpstreamFinished` method**

```csharp
void IHttpStateMachine.OnUpstreamFinished()
{
    if (IsReconnecting)
    {
        _ops.OnWarning(string.Concat(
            "HTTP/1.0 transport closed during reconnect — discarding ",
            PendingRequestCount.ToString(), " buffered request(s)."));
        _ops.OnComplete();
        return;
    }

    if (TryDecodeEof())
    {
        return;
    }

    HandleOrphanedRequest();
    _ops.OnComplete();
}
```

- [ ] **Step 5: Update `OnReconnectAttemptFailed` to call `_ops.OnFail`**

Same pattern as Http11 — replace `_ops.OnReconnectFailed()` with `_ops.OnFail(...)`.

- [ ] **Step 6: Add no-op `OnTimerFired`**

```csharp
void IHttpStateMachine.OnTimerFired(string name) { }
```

- [ ] **Step 7: Replace Http10ConnectionStage with thin shell**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http10;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http10ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http10Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http10Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http10Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http10Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http10ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        var memoryBuffer = inheritedAttributes.GetAttribute(
            new TurboAttributes.MemoryBuffer(4 * 1024, 256 * 1024));

        return new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(ops, _options, memoryBuffer.Initial, memoryBuffer.Max));
    }
}
```

- [ ] **Step 8: Build and run Http10 stream tests**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx && dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http10"`

Expected: All pass.

- [ ] **Step 9: Commit**

```bash
git add src/TurboHTTP/Protocol/Http10/StateMachine.cs src/TurboHTTP/Streams/Stages/Http10ConnectionStage.cs
git commit -m "refactor(h10): implement IHttpStateMachine and replace stage logic"
```

---

## Task 6: Migrate Http2 StateMachine

Http2 is more complex: it has frame processing loops, keep-alive timers, preface building, and GOAWAY handling. All of this moves into the SM.

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http2/StateMachine.cs`

- [ ] **Step 1: Add `IHttpStateMachine` to Http2 SM**

```csharp
internal sealed class StateMachine : IHttpStateMachine
```

- [ ] **Step 2: Add `OnRequest` method**

```csharp
void IHttpStateMachine.OnRequest(HttpRequestMessage request)
{
    Tracing.For("Protocol").Debug(_ops, "H2 → {0} {1}", request.Method, request.RequestUri);
    EncodeRequest(request);
}
```

- [ ] **Step 3: Add `DecodeServerData(ITransportInbound)` — the big one**

This absorbs transport signal handling AND the frame processing loop from `Http20ConnectionStage.Logic.OnServerPush()`:

```csharp
void IHttpStateMachine.DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            Tracing.For("Protocol").Debug(_ops, "H2 connected");
            OnConnectionRestored();
            ScheduleKeepAlivePing();
            return;

        case TransportDisconnected when IsReconnecting:
            OnReconnectAttemptFailed();
            return;

        case TransportDisconnected when HasInFlightRequests:
            Tracing.For("Protocol").Warning(_ops, "H2 closed, in-flight requests");
            OnConnectionLost(lastStreamId: 0);
            return;

        case TransportDisconnected:
            _ops.OnComplete();
            return;
    }

    if (data is not TransportData { Buffer: var buffer })
    {
        return;
    }

    var frames = DecodeServerData(buffer);

    var anyProcessed = false;
    for (var i = 0; i < frames.Count; i++)
    {
        var frame = frames[i];

        Tracing.For("Protocol").Trace(_ops,
            $"Frame received: {frame.Type} stream={frame.StreamId} length={frame.SerializedSize}");

        anyProcessed = true;
        var ok = ProcessFrame(frame);
        if (!ok)
        {
            break;
        }
    }

    if (anyProcessed)
    {
        ResetKeepAliveTimer();
    }
}
```

- [ ] **Step 4: Add timer management methods**

These move from the stage into the SM. The SM now calls `_ops.OnScheduleTimer` / `_ops.OnCancelTimer`:

```csharp
private const string KeepAlivePingTimerKey = "keep-alive-ping";
private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

private bool KeepAliveEnabled => _options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

private void ScheduleKeepAlivePing()
{
    if (KeepAliveEnabled)
    {
        _ops.OnScheduleTimer(KeepAlivePingTimerKey, _options.Http2.KeepAlivePingDelay);
    }
}

private void ScheduleKeepAlivePingTimeout()
{
    if (KeepAliveEnabled)
    {
        _ops.OnScheduleTimer(KeepAlivePingTimeoutKey, _options.Http2.KeepAlivePingTimeout);
    }
}

private void ResetKeepAliveTimer()
{
    if (KeepAliveEnabled)
    {
        _ops.OnCancelTimer(KeepAlivePingTimeoutKey);
        ScheduleKeepAlivePing();
    }
}
```

- [ ] **Step 5: Add `OnTimerFired` method**

This absorbs the `OnTimer` override from the stage:

```csharp
void IHttpStateMachine.OnTimerFired(string name)
{
    switch (name)
    {
        case KeepAlivePingTimerKey:
        {
            var policy = _options.Http2.KeepAlivePingPolicy;
            if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !HasInFlightRequests)
            {
                return;
            }

            SendKeepAlivePing();
            ScheduleKeepAlivePingTimeout();
            break;
        }
        case KeepAlivePingTimeoutKey:
        {
            if (IsKeepAliveTimedOut(_options.Http2.KeepAlivePingTimeout))
            {
                _ops.OnWarning("Keep-alive PING timeout — closing connection.");
                if (HasInFlightRequests)
                {
                    OnConnectionLost(lastStreamId: 0);
                }
                else
                {
                    _ops.OnComplete();
                }
            }

            break;
        }
    }
}
```

- [ ] **Step 6: Add `OnUpstreamFinished` method**

```csharp
void IHttpStateMachine.OnUpstreamFinished()
{
    if (IsReconnecting)
    {
        _ops.OnFail(new HttpRequestException(
            "TurboHTTP: HTTP/2 transport closed during reconnect."));
        return;
    }

    _ops.OnComplete();
}
```

- [ ] **Step 7: Move preface emission into `OnConnectionRestored` or `DecodeServerData(TransportConnected)`**

Currently the stage checks `TryBuildPreface()` in `OnNetworkPull`. The SM should emit the preface via `_ops.OnOutbound()` when it sees `TransportConnected`. Update `OnConnectionRestored()` (or the `TransportConnected` case) to call:

```csharp
var preface = TryBuildPreface();
if (preface is not null)
{
    _ops.OnOutbound(preface);
}
```

This way the stage never calls `TryBuildPreface` — it's emitted automatically when the connection is established.

**Important:** Check if `TryBuildPreface` is also called on the initial connection (not just reconnect). If the initial `TransportConnected` signal triggers `OnConnectionRestored`, then this is fine. If not, you may need to also emit the preface from `DecodeServerData` when handling `TransportConnected`.

- [ ] **Step 8: Update `OnReconnectAttemptFailed` to call `_ops.OnFail`**

Replace `_ops.OnReconnectFailed()` with `_ops.OnFail(new HttpRequestException(...))`.

- [ ] **Step 9: Verify the SM compiles**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx 2>&1 | Select-String "Http2"`

- [ ] **Step 10: Commit**

```bash
git add src/TurboHTTP/Protocol/Http2/StateMachine.cs
git commit -m "feat(h2): implement IHttpStateMachine on Http2 StateMachine"
```

---

## Task 7: Migrate Http20ConnectionStage to Thin Shell

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Http20ConnectionStage.cs`

- [ ] **Step 1: Replace with thin shell**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http20ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http20Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http20Connection.Out.Network");
    private readonly TurboClientOptions _options;

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    public Http20ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(_options, ops));
}
```

- [ ] **Step 2: Build and run Http2 stream tests**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx && dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http2"`

Expected: All pass. If ping/keep-alive tests fail, check that `OnTimerFired` correctly maps timer key strings and that `ScheduleKeepAlivePing` is called at the right times.

- [ ] **Step 3: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/Http20ConnectionStage.cs
git commit -m "refactor(h2): replace stage logic with generic HttpConnectionStageLogic"
```

---

## Task 8: Migrate Http3 StateMachine

Http3 is the most complex: multiplexed streams, QPACK, control streams, idle timeout, `PreStart` logic. All of this moves into the SM.

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http3/StateMachine.cs`

- [ ] **Step 1: Add `IHttpStateMachine` to Http3 SM**

```csharp
internal sealed class StateMachine : IHttpStateMachine, IDisposable
```

- [ ] **Step 2: Add `OnRequest` method**

```csharp
void IHttpStateMachine.OnRequest(HttpRequestMessage request)
{
    Tracing.For("Protocol").Debug(_ops, "H3 → {0} {1}", request.Method, request.RequestUri);
    EncodeRequest(request);
}
```

- [ ] **Step 3: Add `DecodeServerData(ITransportInbound)` — the comprehensive dispatcher**

This absorbs everything from `Http30ConnectionStage.OnServerPush()`, `HandleSignalItem()`, `HandleTaggedStreamData()`, and `ProcessFrameData()`:

```csharp
void IHttpStateMachine.DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            Tracing.For("Protocol").Debug(_ops, "H3 connected");
            _transportConnected = true;
            OnConnectionRestored();
            return;

        case TransportDisconnected when IsReconnecting:
            OnReconnectAttemptFailed();
            return;

        case TransportDisconnected when HasInFlightRequests:
            Tracing.For("Protocol").Warning(_ops, "H3 closed, in-flight requests");
            OnConnectionLost();
            return;

        case TransportDisconnected:
            _ops.OnComplete();
            return;

        case ServerStreamAccepted { StreamId: var id }:
            OnServerStreamOpened(id);
            return;

        case StreamOpened:
            return;

        case StreamReadCompleted { StreamId: >= 0 } readCompleted:
            FlushPendingResponse(readCompleted.StreamId);
            return;

        case StreamReadCompleted:
            return;

        case StreamClosed { StreamId: >= 0 } streamClosed:
            if (streamClosed.Reason == DisconnectReason.Error)
            {
                FailInflightRequest(streamClosed.StreamId,
                    new HttpRequestException("HTTP/3 stream aborted by transport."));
            }
            else
            {
                FlushPendingResponse(streamClosed.StreamId);
            }
            return;

        case StreamClosed:
            FlushPendingResponse();
            return;

        case MultiplexedData multiplexed:
            HandleTaggedStreamData(multiplexed);
            return;

        case TransportData rawData:
            _ops.OnWarning("Received untagged TransportData — dropping to prevent stream ID misrouting.");
            rawData.Buffer.Dispose();
            return;
    }
}
```

- [ ] **Step 4: Move `HandleTaggedStreamData` and `ProcessFrameData` into the SM**

These methods were in the stage — move them as private methods on the SM:

```csharp
private const long ControlStreamDecoderId = -2;

private void HandleTaggedStreamData(MultiplexedData tagged)
{
    var (streamId, buffer) = ResolveStreamId(tagged.StreamId, tagged.Buffer);

    if (buffer is null)
    {
        return;
    }

    switch (streamId)
    {
        case -4:
            ProcessQpackDecoderBytes(buffer.Memory);
            buffer.Dispose();
            return;
        case -3:
            ProcessQpackEncoderBytes(buffer.Memory);
            buffer.Dispose();
            return;
        case -2:
            ProcessFrameData(buffer, streamId: ControlStreamDecoderId);
            return;
        default:
            ProcessFrameData(buffer, streamId);
            return;
    }
}

private void ProcessFrameData(TransportBuffer buffer, long streamId)
{
    var frames = DecodeServerData(buffer, streamId);

    for (var i = 0; i < frames.Count; i++)
    {
        var frame = frames[i];
        var forwarded = ProcessFrame(frame);
        if (forwarded is not null)
        {
            AssembleResponse(forwarded, streamId);
        }
    }
}
```

- [ ] **Step 5: Move PreStart logic into SM — emit initial streams and preface**

Add a method that emits the initial control/QPACK streams. This should be called when the SM is constructed or when the first `TransportConnected` is received:

```csharp
public void EmitInitialStreams()
{
    _ops.OnOutbound(new OpenStream(-2, StreamDirection.Unidirectional));
    _ops.OnOutbound(new OpenStream(-3, StreamDirection.Unidirectional));
    _ops.OnOutbound(new OpenStream(-4, StreamDirection.Unidirectional));

    var preface = TryBuildControlPreface();
    if (preface is not null)
    {
        _ops.OnOutbound(preface);
    }

    ScheduleIdleCheck();
}
```

**Decision point:** Should `EmitInitialStreams` be called from the constructor, or should the generic stage have a `PreStart` hook? Since the stage base class can call `_sm.EmitInitialStreams()` in `PreStart`, we can either:
1. Add a `void Initialize()` method to `IHttpStateMachine` called from the stage's `PreStart`. Other SMs implement it as no-op.
2. Have the SM constructor emit these — but the stage's `IStageOperations` aren't connected to ports yet in the constructor.

**Recommendation:** Add `void PreStart()` to `IHttpStateMachine`. Override `PreStart()` in `HttpConnectionStageLogic` to call `_sm.PreStart()`. Http10/11/20 implement it as no-op. Http3 emits initial streams.

Update `IHttpStateMachine`:

```csharp
internal interface IHttpStateMachine
{
    bool CanAcceptRequest { get; }

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void Cleanup();
}
```

Update `HttpConnectionStageLogic`:

```csharp
public override void PreStart()
{
    _sm.PreStart();
}
```

Add no-op `PreStart` to Http10, Http11, Http2 SMs:

```csharp
void IHttpStateMachine.PreStart() { }
```

Http3 SM:

```csharp
void IHttpStateMachine.PreStart()
{
    _ops.OnOutbound(new OpenStream(-2, StreamDirection.Unidirectional));
    _ops.OnOutbound(new OpenStream(-3, StreamDirection.Unidirectional));
    _ops.OnOutbound(new OpenStream(-4, StreamDirection.Unidirectional));

    var preface = TryBuildControlPreface();
    if (preface is not null)
    {
        _ops.OnOutbound(preface);
    }

    ScheduleIdleCheck();
}
```

- [ ] **Step 6: Add timer management for idle timeout**

```csharp
private const string IdleCheckTimerKey = "idle-timeout-check";

private void ScheduleIdleCheck()
{
    if (IsTimeoutDisabled)
    {
        return;
    }

    var remaining = TimeUntilExpiry();
    var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    _ops.OnScheduleTimer(IdleCheckTimerKey, checkInterval);
}
```

- [ ] **Step 7: Add `OnTimerFired` method**

```csharp
void IHttpStateMachine.OnTimerFired(string name)
{
    if (name != IdleCheckTimerKey)
    {
        return;
    }

    var goAway = CheckIdleTimeout();
    if (goAway is not null)
    {
        var buf = TransportBuffer.Rent(goAway.SerializedSize);
        var span = buf.FullMemory.Span;
        goAway.WriteTo(ref span);
        buf.Length = goAway.SerializedSize;
        _ops.OnOutbound(new MultiplexedData(buf, -2));
        _ops.OnComplete();
        return;
    }

    ScheduleIdleCheck();
}
```

- [ ] **Step 8: Add `OnUpstreamFinished` method**

```csharp
void IHttpStateMachine.OnUpstreamFinished()
{
    FlushPendingResponse();

    if (IsReconnecting)
    {
        _ops.OnFail(new HttpRequestException(
            "TurboHTTP: HTTP/3 transport closed during reconnect."));
        return;
    }

    _ops.OnComplete();
}
```

- [ ] **Step 9: Add `_transportConnected` field if not already present**

Check if `_transportConnected` already exists on the SM. If not, add:

```csharp
private bool _transportConnected;
```

Http3's `OnOutbound` currently uses `_pendingOutbound` (the staging list) in the stage. Since the SM now emits via `_ops.OnOutbound` directly, we need to check if the pending/staging pattern is still needed. In the new design, `_ops.OnOutbound` goes straight to the stage's queue, so **the `_pendingOutbound` list in the old stage is no longer needed**. However, the SM may need to buffer outbound items before `TransportConnected` arrives.

If pre-connect buffering is needed, add it to the SM:

```csharp
private readonly List<ITransportOutbound> _preConnectBuffer = [];

private void EmitOutbound(ITransportOutbound item)
{
    if (!_transportConnected && item is not ConnectTransport)
    {
        _preConnectBuffer.Add(item);
        return;
    }

    _ops.OnOutbound(item);
}
```

Then in `OnConnectionRestored`, flush the pre-connect buffer:

```csharp
foreach (var item in _preConnectBuffer)
{
    _ops.OnOutbound(item);
}
_preConnectBuffer.Clear();
```

Review the existing stage's `FlushOutbound()` method carefully — it has specific logic for `ConnectTransport` items that should pass through even before transport is connected. Replicate that logic in the SM.

- [ ] **Step 10: Update `OnReconnectAttemptFailed` to call `_ops.OnFail`**

Replace `_ops.OnReconnectFailed()` with `_ops.OnFail(new HttpRequestException(...))`.

- [ ] **Step 11: Update `Cleanup` to implement `IHttpStateMachine.Cleanup`**

Make sure the existing `Dispose` method is called from `Cleanup`:

```csharp
void IHttpStateMachine.Cleanup()
{
    Dispose();
}
```

- [ ] **Step 12: Verify the SM compiles**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx 2>&1 | Select-String "Http3"`

- [ ] **Step 13: Commit**

```bash
git add src/TurboHTTP/Protocol/Http3/StateMachine.cs
git commit -m "feat(h3): implement IHttpStateMachine on Http3 StateMachine"
```

---

## Task 9: Migrate Http30ConnectionStage to Thin Shell

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Http30ConnectionStage.cs`

- [ ] **Step 1: Replace with thin shell**

```csharp
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages;

internal sealed class Http30ConnectionStage : GraphStage<ConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inServer = new("Http30Connection.In.Server");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Http30Connection.Out.Response");
    private readonly Inlet<HttpRequestMessage> _inApp = new("Http30Connection.In.App");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("Http30Connection.Out.Network");

    private readonly TurboClientOptions _options;

    public Http30ConnectionStage(TurboClientOptions options)
    {
        _options = options;
    }

    public override ConnectionShape Shape => new(_inServer, _outResponse, _inApp, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionStageLogic<StateMachine>(
            this,
            ops => new StateMachine(_options, ops));
}
```

- [ ] **Step 2: Build and run Http3 stream tests**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx && dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.Http3"`

Expected: All pass.

- [ ] **Step 3: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/Http30ConnectionStage.cs
git commit -m "refactor(h3): replace stage logic with generic HttpConnectionStageLogic"
```

---

## Task 10: Remove `OnReconnectFailed` from IStageOperations

Now that all four SMs call `_ops.OnFail` directly, the old `OnReconnectFailed` method should be gone. If it was temporarily kept for compilation, remove it now.

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/IStageOperations.cs`

- [ ] **Step 1: Verify no references to `OnReconnectFailed` remain**

Run: `grep -r "OnReconnectFailed" src/TurboHTTP/`

Expected: No results (or only in the interface definition if still present).

- [ ] **Step 2: Remove from `IStageOperations` if still present**

- [ ] **Step 3: Build the full solution**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx`

Expected: Clean build, zero errors.

- [ ] **Step 4: Commit**

```bash
git add src/TurboHTTP/Streams/Stages/IStageOperations.cs
git commit -m "chore: remove obsolete OnReconnectFailed from IStageOperations"
```

---

## Task 11: Run Full Test Suite

**Files:** None (verification only)

- [ ] **Step 1: Run all unit tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`

Expected: All pass.

- [ ] **Step 2: Run all stream tests**

Run: `dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`

Expected: All pass. These are the critical ones — they exercise the full stage pipeline.

- [ ] **Step 3: Run integration tests per namespace**

Run each separately (per project convention):

```bash
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -namespace "TurboHTTP.IntegrationTests.H10"
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -namespace "TurboHTTP.IntegrationTests.H11"
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -namespace "TurboHTTP.IntegrationTests.H2"
dotnet run --project src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -namespace "TurboHTTP.IntegrationTests.H3"
```

Expected: All pass.

- [ ] **Step 4: Commit (tag as verified)**

No code changes — just verify everything works.

---

## Task 12: Add MockStageOperations and SM Unit Test Infrastructure

**Files:**
- Create: `src/TurboHTTP.Tests/Stages/MockStageOperations.cs`
- Create: `src/TurboHTTP.Tests/Stages/MockStateMachine.cs`
- Create: `src/TurboHTTP.Tests/Stages/HttpConnectionStageLogicSpec.cs`

- [ ] **Step 1: Create `MockStageOperations`**

```csharp
// src/TurboHTTP.Tests/Stages/MockStageOperations.cs
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Stages;

internal sealed class MockStageOperations : IStageOperations
{
    public List<HttpResponseMessage> Responses { get; } = [];
    public List<ITransportOutbound> Outbound { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<(string Name, TimeSpan Duration)> ScheduledTimers { get; } = [];
    public List<string> CancelledTimers { get; } = [];
    public bool Completed { get; private set; }
    public Exception? FailException { get; private set; }

    void IStageOperations.OnResponse(HttpResponseMessage response) => Responses.Add(response);
    void IStageOperations.OnOutbound(ITransportOutbound item) => Outbound.Add(item);
    void IStageOperations.OnWarning(string message) => Warnings.Add(message);
    void IStageOperations.OnScheduleTimer(string name, TimeSpan duration) => ScheduledTimers.Add((name, duration));
    void IStageOperations.OnCancelTimer(string name) => CancelledTimers.Add(name);
    void IStageOperations.OnComplete() => Completed = true;
    void IStageOperations.OnFail(Exception exception) => FailException = exception;
    ILoggingAdapter IStageOperations.Log => NoLogger.Instance;
}
```

- [ ] **Step 2: Create `MockStateMachine`**

```csharp
// src/TurboHTTP.Tests/Stages/MockStateMachine.cs
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Stages;

internal sealed class MockStateMachine : IHttpStateMachine
{
    public bool CanAcceptRequest { get; set; } = true;

    public List<HttpRequestMessage> ReceivedRequests { get; } = [];
    public List<ITransportInbound> ReceivedData { get; } = [];
    public int UpstreamFinishedCount { get; private set; }
    public List<string> FiredTimers { get; } = [];
    public bool PreStartCalled { get; private set; }
    public bool CleanupCalled { get; private set; }

    void IHttpStateMachine.PreStart() => PreStartCalled = true;
    void IHttpStateMachine.OnRequest(HttpRequestMessage request) => ReceivedRequests.Add(request);
    void IHttpStateMachine.DecodeServerData(ITransportInbound data) => ReceivedData.Add(data);
    void IHttpStateMachine.OnUpstreamFinished() => UpstreamFinishedCount++;
    void IHttpStateMachine.OnTimerFired(string name) => FiredTimers.Add(name);
    void IHttpStateMachine.Cleanup() => CleanupCalled = true;
}
```

- [ ] **Step 3: Write stage pump tests**

```csharp
// src/TurboHTTP.Tests/Stages/HttpConnectionStageLogicSpec.cs
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Tests.Stages;

public sealed class HttpConnectionStageLogicSpec
{
    [Fact(Timeout = 5000)]
    public void Stage_should_forward_transport_data_to_state_machine()
    {
        // Test that OnServerPush calls _sm.DecodeServerData
        // This requires Akka stream test infrastructure — use TestSource/TestSink
        // Exact implementation depends on existing test patterns in the project
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_forward_requests_to_state_machine()
    {
        // Test that OnAppPush calls _sm.OnRequest
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_call_cleanup_on_post_stop()
    {
        // Test that PostStop calls _sm.Cleanup()
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_call_pre_start_on_state_machine()
    {
        // Test that PreStart calls _sm.PreStart()
    }
}
```

**Note:** The exact test setup depends on how the existing stream tests work (Akka TestKit, TestSource/TestSink pattern). Look at existing tests in `src/TurboHTTP.StreamTests/` for the pattern and replicate it. The mock SM makes assertions simpler — you verify method calls on the mock rather than asserting protocol behavior.

- [ ] **Step 4: Run the new tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Stages"`

Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add src/TurboHTTP.Tests/Stages/
git commit -m "test: add MockStageOperations, MockStateMachine, and stage pump tests"
```

---

## Task 13: Cleanup — Remove Dead Code and Verify

**Files:**
- All four SM files (remove now-unused public methods that were only called by the old stage)

- [ ] **Step 1: Identify dead public methods on SMs**

After migration, some methods that were public (called by the stage) may now be private or unused:
- `StartReconnect()` — now called internally, can be `private`
- `OnConnectionRestored()` — now called internally, can be `private`
- `OnReconnectAttemptFailed()` — now called internally, can be `private`
- `OnConnectionLost()` — now called internally, can be `private`
- `HandleOrphanedRequest(s)` — now called internally, can be `private`
- `TryDecodeEof()` — now called internally, can be `private`
- `DecodeServerData(TransportBuffer)` — still called by the `IHttpStateMachine.DecodeServerData`, keep as `private`
- `ProcessFrame()` — still called by the SM internally, can be `private`
- `EncodeRequest()` — still called by `OnRequest`, can be `private`
- `TryBuildPreface()` / `TryBuildControlPreface()` — now called internally, can be `private`
- `SendKeepAlivePing()`, `IsKeepAliveTimedOut()` — now called internally, can be `private`

**Check each:** Search for external callers. If only called within the SM, make `private`.

Run: For each method, use the Roslyn navigator `find_references` or grep to verify no external callers.

- [ ] **Step 2: Make internal-only methods private**

Change visibility on all methods that are no longer called externally.

- [ ] **Step 3: Remove any leftover state flags from old stages**

Check that no references to `_reconnectFailed`, `_serverFinished`, `_transportConnected` exist in stage files. They should all be gone since stages are thin shells now.

- [ ] **Step 4: Full build + test**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx && dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`

Expected: Clean build, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: make SM-internal methods private, remove dead stage code"
```
