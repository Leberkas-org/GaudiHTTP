# IO → Transport Migration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all `Servus.Akka.IO` usage in TurboHTTP with `Servus.Akka.Transport`, implement `IPoolingStrategy` per HTTP version **in TurboHTTP** (not Servus.Akka), and delete the IO namespace.

**Architecture:** The old IO layer uses `IInputItem`/`IOutputItem` with `NetworkBuffer`, `ConnectItem`, `StreamAcquireItem`, `ConnectionReuseItem`, `CloseSignalItem` as the protocol boundary. The new Transport layer uses `ITransportInbound`/`ITransportOutbound` with `TransportBuffer`, `ConnectTransport`, `TransportData`, `TransportDisconnected`, `TransportConnected`. Connection pooling is split into two layers:

1. **Pool sizing & eviction** (`TcpPoolConfig` + `PoolConfigRegistry`) — lives in `Servus.Akka.Transport`. The `TcpConnectionManagerActor` takes a `PoolConfigRegistry` and resolves per-host config via `TransportOptions.PoolKey`. `TcpPoolConfig` holds `MaxConnectionsPerHost`, `IdleTimeout`, `ConnectionLifetime`, `ReuseOnUpstreamFinish`. This is protocol-agnostic — the transport layer doesn't know about HTTP versions, only pool keys.
2. **Lease-return decisions** (`IPoolingStrategy`) — used by the `TcpConnectionStage`/`TcpTransportStateMachine` to decide `PoolAction.Reuse` vs `PoolAction.Dispose` on disconnect and upstream finish. The interface has only two methods: `OnDisconnect(object lease, DisconnectReason reason)` and `OnUpstreamFinish(object lease)`.

**Boundary rule:**
- `Servus.Akka.Transport` owns: `IPoolingStrategy` (slim, 2 methods), `PoolAction`, `DisconnectReason`, `TransportOptions` (with `string? PoolKey`), `TcpPoolConfig`, `PoolConfigRegistry`
- `TurboHTTP.Streams.Pooling` owns the **HTTP-version-specific** `IPoolingStrategy` implementations (`Http10PoolingStrategy`, `Http11PoolingStrategy`, `Http2PoolingStrategy`) — they encode HTTP semantics (e.g. HTTP/1.1 reuses on upstream finish, HTTP/1.0 and HTTP/2 always dispose)
- `TurboHTTP.Internal.PoolKeys` owns the pool key constants (`"http10"`, `"http11"`, `"http2"`) that bridge `OptionsFactory` and `ClientStreamOwner`
- `TurboHTTP.Internal.OptionsFactory` sets `PoolKey` on `TransportOptions` based on the HTTP version
- Tests live in `TurboHTTP.Tests`

**Tech Stack:** C# 12, Akka.NET, Akka.Streams, xUnit v3

---

## Phase 1: IPoolingStrategy Implementations + PoolConfigRegistry

> **DONE** — Phase 1 is already implemented. The strategies and registry exist in the codebase.

### Design

`IPoolingStrategy` is a slim interface with only two methods — it governs **stage-level lease-return decisions**:

```csharp
// Servus.Akka.Transport.IPoolingStrategy
public interface IPoolingStrategy
{
    PoolAction OnDisconnect(object lease, DisconnectReason reason);
    PoolAction OnUpstreamFinish(object lease);
}
```

Pool sizing and eviction (max connections, idle timeout, connection lifetime) are handled by `TcpPoolConfig` + `PoolConfigRegistry`, which the `TcpConnectionManagerActor` uses. The connection manager resolves the right config per host via `TransportOptions.PoolKey`.

```csharp
// Servus.Akka.Transport.TcpPoolConfig
public sealed record TcpPoolConfig(
    int MaxConnectionsPerHost,
    TimeSpan IdleTimeout,
    TimeSpan ConnectionLifetime,
    bool ReuseOnUpstreamFinish);

// Servus.Akka.Transport.PoolConfigRegistry
public sealed class PoolConfigRegistry
{
    public PoolConfigRegistry(TcpPoolConfig defaultConfig);
    public PoolConfigRegistry Register(string poolKey, TcpPoolConfig config);
    public TcpPoolConfig Resolve(string? poolKey);
}
```

`TransportOptions` has a `string? PoolKey` property. TurboHTTP sets it via `OptionsFactory` using constants from `TurboHTTP.Internal.PoolKeys` (`"http10"`, `"http11"`, `"http2"`).

### Existing files

- `src/Servus.Akka/Transport/IPoolingStrategy.cs` — slim interface (2 methods)
- `src/Servus.Akka/Transport/TcpPoolConfig.cs` — pool config record
- `src/Servus.Akka/Transport/PoolConfigRegistry.cs` — string-key → config registry
- `src/Servus.Akka/Transport/TransportOptions.cs` — has `PoolKey` property
- `src/TurboHTTP/Streams/Pooling/Http10PoolingStrategy.cs` — always Dispose
- `src/TurboHTTP/Streams/Pooling/Http11PoolingStrategy.cs` — Reuse on upstream finish, Dispose on disconnect
- `src/TurboHTTP/Streams/Pooling/Http2PoolingStrategy.cs` — always Dispose
- `src/TurboHTTP/Internal/PoolKeys.cs` — pool key constants
- `src/TurboHTTP.Tests/Streams/Pooling/Http10PoolingStrategySpec.cs`
- `src/TurboHTTP.Tests/Streams/Pooling/Http11PoolingStrategySpec.cs`
- `src/TurboHTTP.Tests/Streams/Pooling/Http2PoolingStrategySpec.cs`

### Important: Do NOT revert to the old design

The old `IPoolingStrategy` had properties (`MaxConnectionsPerHost`, `IdleTimeout`, `ConnectionLifetime`) and additional methods (`CanReuse`, `OnRelease`, `OnIdle`). These are **gone by design**. Pool config is now in `TcpPoolConfig`/`PoolConfigRegistry`. The `TcpConnectionManagerActor` takes a `PoolConfigRegistry`, NOT an `IPoolingStrategy`. The `TcpConnectionStage`/`TcpTransportStateMachine` take an `IPoolingStrategy` for lease-return decisions only.

---

## Phase 2: OptionsFactory + TransportRegistry Migration

### Task 4: Migrate OptionsFactory from IO types to Transport types

> **DONE** — `OptionsFactory` already uses Transport types and sets `PoolKey`.

**Files:**
- `src/TurboHTTP/Internal/OptionsFactory.cs` — already migrated
- `src/TurboHTTP/Internal/PoolKeys.cs` — pool key constants

`OptionsFactory.Build(RequestEndpoint, TurboClientOptions)` returns `TransportOptions` with `PoolKey` set based on HTTP version:
- HTTP/1.0 → `PoolKeys.Http10` (`"http10"`)
- HTTP/1.1 → `PoolKeys.Http11` (`"http11"`)
- HTTP/2.0 → `PoolKeys.Http2` (`"http2"`)
- HTTP/3.0 → no `PoolKey` (QUIC uses its own `QuicConnectionManagerActor` without `PoolConfigRegistry`)

The `PoolKey` is set on `TcpTransportOptions` and `TlsTransportOptions`. QUIC transport options don't use it.

---

### Task 5: Migrate TransportRegistry to Transport types

**Files:**
- Modify: `src/TurboHTTP/Streams/TransportRegistry.cs`

Replace `ITransportFactory`/`IInputItem`/`IOutputItem` with Transport types.

- [ ] **Step 1: Update TransportRegistry**

```csharp
// src/TurboHTTP/Streams/TransportRegistry.cs
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal sealed class TransportRegistry
{
    private readonly Dictionary<Version, Func<Flow<ITransportOutbound, ITransportInbound, NotUsed>>> _transports = new();

    public TransportRegistry Register(Version version,
        Func<Flow<ITransportOutbound, ITransportInbound, NotUsed>> factory)
    {
        _transports[version] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Get(Version version)
    {
        if (_transports.TryGetValue(version, out var factory))
        {
            return factory();
        }

        throw new InvalidOperationException(
            $"No transport factory registered for HTTP version {version}. " +
            $"Registered versions: {string.Join(", ", _transports.Keys)}");
    }
}
```

Note: Changed from `ITransportFactory` interface to `Func<Flow<...>>` — the `TcpTransportFactory.Create()` and `QuicTransportFactory.Create()` already return flows; wrapping in a Func is simpler than maintaining a separate interface.

- [ ] **Step 2: Verify build** (callers will break — fixed in Task 8)

---

### Task 6: Migrate ConnectionShape and IStageOperations to Transport types

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/ConnectionShape.cs`
- Modify: `src/TurboHTTP/Streams/Stages/IStageOperations.cs`

- [ ] **Step 1: Update ConnectionShape**

```csharp
// src/TurboHTTP/Streams/Stages/ConnectionShape.cs
using System.Collections.Immutable;
using Akka.Streams;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal sealed class ConnectionShape : Shape
{
    public Inlet<ITransportInbound> InServer { get; }
    public Outlet<HttpResponseMessage> OutResponse { get; }
    public Inlet<HttpRequestMessage> InApp { get; }
    public Outlet<ITransportOutbound> OutNetwork { get; }

    public ConnectionShape(
        Inlet<ITransportInbound> inServer,
        Outlet<HttpResponseMessage> outResponse,
        Inlet<HttpRequestMessage> inApp,
        Outlet<ITransportOutbound> outNetwork)
    {
        InServer = inServer;
        OutResponse = outResponse;
        InApp = inApp;
        OutNetwork = outNetwork;
    }

    public override ImmutableArray<Inlet> Inlets => [InServer, InApp];
    public override ImmutableArray<Outlet> Outlets => [OutResponse, OutNetwork];

    public override Shape DeepCopy()
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)InServer.CarbonCopy(),
            (Outlet<HttpResponseMessage>)OutResponse.CarbonCopy(),
            (Inlet<HttpRequestMessage>)InApp.CarbonCopy(),
            (Outlet<ITransportOutbound>)OutNetwork.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new ConnectionShape(
            (Inlet<ITransportInbound>)inlets[0],
            (Outlet<HttpResponseMessage>)outlets[0],
            (Inlet<HttpRequestMessage>)inlets[1],
            (Outlet<ITransportOutbound>)outlets[1]);
    }
}
```

- [ ] **Step 2: Update IStageOperations**

```csharp
// src/TurboHTTP/Streams/Stages/IStageOperations.cs
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Stages;

internal interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(ITransportOutbound item);
    void OnWarning(string message);
    void OnReconnectFailed();
}
```

- [ ] **Step 3: Commit phase 2 checkpoint**

---

## Phase 3: Protocol StateMachine Migration (HTTP/1.0, 1.1, 2, 3)

Each protocol StateMachine emits old IO messages (`ConnectItem`, `StreamAcquireItem`, `NetworkBuffer`, `ConnectionReuseItem`). These need to be replaced with Transport messages (`ConnectTransport`, `TransportData`). The `ConnectionReuseItem` is removed — pooling decisions are now split: pool sizing/eviction is in `TcpPoolConfig` via `PoolConfigRegistry` (inside `TcpConnectionManagerActor`), and lease-return decisions are in `IPoolingStrategy` (inside `TcpTransportStateMachine`).

### Task 7: Migrate Http11 StateMachine

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http11/StateMachine.cs`

Key changes:
- `NetworkBuffer` → `TransportBuffer`
- `ConnectItem { Key, Options }` → `ConnectTransport(options)`
- `StreamAcquireItem { Key }` → removed (transport layer handles stream lifecycle)
- `ConnectionReuseItem(canReuse) { Key }` → removed (pooling strategy handles this)
- `CloseSignalItem(TlsCloseKind)` → `TransportDisconnected(DisconnectReason)`
- `ConnectedSignalItem` → `TransportConnected`
- `IInputItem` → `ITransportInbound`
- `IOutputItem` → `ITransportOutbound`
- `RequestEndpoint` stays in the protocol SM for request routing, but is NOT passed to transport messages
- `List<NetworkBuffer>? _bodyOwners` → `List<TransportBuffer>? _bodyOwners`

- [ ] **Step 1: Update using directives and field types**

Replace:
```csharp
using Servus.Akka.IO;
```
With:
```csharp
using Servus.Akka.Transport;
```

Replace field:
```csharp
private ITransportOptions? _transportOptions;
```
With:
```csharp
private TransportOptions? _transportOptions;
```

Replace field:
```csharp
private List<NetworkBuffer>? _bodyOwners;
```
With:
```csharp
private List<TransportBuffer>? _bodyOwners;
```

- [ ] **Step 2: Update EncodeRequest — emit ConnectTransport + TransportData**

Replace the `ConnectItem` + `StreamAcquireItem` + `NetworkBuffer` emission with:
```csharp
if (Endpoint == default && endpoint != default)
{
    Endpoint = endpoint;
    _transportOptions = OptionsFactory.Build(
        Endpoint.Host, Endpoint.Port, Endpoint.Scheme, Endpoint.Version, _options);
    _ops.OnOutbound(new ConnectTransport(_transportOptions));
}

TransportBuffer? item = null;
try
{
    var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
    var estimatedSize = _minBufferSize + contentLength;
    var bufferSize = Math.Min(estimatedSize, _maxBufferSize);
    item = TransportBuffer.Rent(bufferSize);
    var span = item.FullMemory.Span;

    var written = Encoder.Encode(request, ref span);
    item.Length = written;

    _ops.OnOutbound(new TransportData(item));
}
```

Note: `StreamAcquireItem` is removed — the transport layer doesn't need explicit stream acquire signals for TCP.

- [ ] **Step 3: Update DecodeServerData — handle TransportDisconnected + TransportData**

Replace:
```csharp
public bool DecodeServerData(IInputItem inputItem)
{
    if (inputItem is CloseSignalItem closeSignal)
    {
        HandleCloseSignal(closeSignal);
        return false;
    }

    if (inputItem is not NetworkBuffer buffer)
    {
        return true;
    }
    ...
}
```
With:
```csharp
public bool DecodeServerData(ITransportInbound inputItem)
{
    if (inputItem is TransportDisconnected disconnected)
    {
        HandleDisconnect(disconnected);
        return false;
    }

    if (inputItem is not TransportData { Buffer: var buffer })
    {
        return true;
    }
    ...
}
```

- [ ] **Step 4: Update HandleCloseSignal → HandleDisconnect**

Map `TlsCloseKind.CleanClose` → `DisconnectReason.Graceful`, `TlsCloseKind.AbruptClose` → `DisconnectReason.Error`:

```csharp
private void HandleDisconnect(TransportDisconnected disconnected)
{
    if (_pendingCloseDelimitedResponse is not null)
    {
        if (disconnected.Reason == DisconnectReason.Graceful)
        {
            var content = PooledBodyContent.FromChunks(_initialBodyBytes, _bodyOwners);
            _pendingCloseDelimitedResponse.Content = content;
            var response = _pendingCloseDelimitedResponse;
            _pendingCloseDelimitedResponse = null;
            _bodyOwners = null;
            _initialBodyBytes = null;
            CompleteResponse(response);
        }
        else
        {
            _ops.OnWarning("Abrupt connection close — discarding incomplete response");
            if (_bodyOwners is not null)
            {
                foreach (var buf in _bodyOwners)
                {
                    buf.Dispose();
                }
            }
            _pendingCloseDelimitedResponse = null;
            _bodyOwners = null;
            _initialBodyBytes = null;
            throw new HttpRequestException(
                "Connection was aborted while receiving close-delimited HTTP/1.1 response.");
        }
        return;
    }

    if (disconnected.Reason == DisconnectReason.Graceful)
    {
        if (_decoder.TryDecodeEof(out var response) && response is not null)
        {
            CompleteResponse(response);
        }
    }
    else
    {
        _ops.OnWarning("Abrupt connection close — discarding incomplete response");
    }
}
```

- [ ] **Step 5: Update CompleteResponse — remove ConnectionReuseItem emission**

Remove the `ConnectionReuseItem` at the end of `CompleteResponse`. The pooling strategy handles reuse:

```csharp
private void CompleteResponse(HttpResponseMessage response)
{
    if (_inFlightQueue.Count > 0)
    {
        var request = _inFlightQueue.Dequeue();
        response.RequestMessage = request;
    }

    if (HasConnectionClose(response))
    {
        _effectivePipelineDepth = 1;
    }

    var partialContentResult = PartialContentValidator.Validate(response);
    if (!partialContentResult.IsValid)
    {
        _ops.OnWarning(partialContentResult.ErrorMessage!);
    }

    _ops.OnResponse(response);
}
```

- [ ] **Step 6: Update StartReconnect — emit ConnectTransport**

```csharp
public void StartReconnect()
{
    _reconnectBufferedQueue = new Queue<HttpRequestMessage>(_inFlightQueue);
    _inFlightQueue.Clear();
    IsReconnecting = true;
    _reconnectAttempts = 1;
    _decoder.Reset();
    _ops.OnOutbound(new ConnectTransport(_transportOptions!));
}
```

- [ ] **Step 7: Update OnReconnectAttemptFailed**

```csharp
public void OnReconnectAttemptFailed()
{
    if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
    {
        _ops.OnReconnectFailed();
        return;
    }

    _reconnectAttempts++;
    _ops.OnOutbound(new ConnectTransport(_transportOptions!));
}
```

- [ ] **Step 8: Update AccumulateCloseDelimitedBody**

```csharp
private bool AccumulateCloseDelimitedBody(TransportBuffer buffer)
{
    _bodyOwners ??= [];
    _bodyOwners.Add(buffer);
    return true;
}
```

- [ ] **Step 9: Update DecodeNormalResponse**

```csharp
private bool DecodeNormalResponse(TransportBuffer buffer)
{
    try
    {
        var data = buffer.Memory;

        if (!_decoder.TryDecode(data, out var responses))
        {
            buffer.Dispose();
            return true;
        }

        buffer.Dispose();
        // ... rest unchanged
    }
    catch (Exception ex)
    {
        buffer.Dispose();
        // ... rest unchanged
    }
}
```

- [ ] **Step 10: Update Cleanup**

```csharp
public void Cleanup()
{
    _inFlightQueue.Clear();
    _decoder.Reset();

    if (_bodyOwners is not null)
    {
        foreach (var buf in _bodyOwners)
        {
            buf.Dispose();
        }
        _bodyOwners = null;
    }

    _pendingCloseDelimitedResponse?.Dispose();
    _pendingCloseDelimitedResponse = null;
    _initialBodyBytes = null;
}
```

- [ ] **Step 11: Update PooledBodyContent.FromChunks signature**

Modify `src/TurboHTTP/Internal/PooledBodyContent.cs` — change `List<NetworkBuffer>?` to `List<TransportBuffer>?`:
```csharp
using Servus.Akka.Transport;
// ...
public static PooledBodyContent FromChunks(byte[]? initial, List<TransportBuffer>? chunks)
```

- [ ] **Step 12: Commit**

---

### Task 8: Migrate Http11ConnectionStage

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Http11ConnectionStage.cs`

Replace `IInputItem`/`IOutputItem` with `ITransportInbound`/`ITransportOutbound`. Update inbound signal handling: `ConnectedSignalItem` → `TransportConnected`, `CloseSignalItem` → `TransportDisconnected`.

- [ ] **Step 1: Update port types and using directives**

Replace `Inlet<IInputItem>` → `Inlet<ITransportInbound>`, `Outlet<IOutputItem>` → `Outlet<ITransportOutbound>`, `List<IOutputItem>` → `List<ITransportOutbound>`.

- [ ] **Step 2: Update OnServerPush signal handling**

Replace:
```csharp
case ConnectedSignalItem:
```
With:
```csharp
case TransportConnected:
```

Replace:
```csharp
case CloseSignalItem:
```
With:
```csharp
case TransportDisconnected:
```

- [ ] **Step 3: Update PostStop — dispose TransportBuffer items**

```csharp
foreach (var item in _pendingOutbound)
{
    if (item is TransportData { Buffer: var buf })
    {
        buf.Dispose();
    }
}
```

- [ ] **Step 4: Commit**

---

### Task 9: Apply same pattern to Http10 StateMachine + ConnectionStage

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http10/StateMachine.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Http10ConnectionStage.cs`

Same transformation as Tasks 7+8 but for HTTP/1.0. The Http10 SM is structurally similar to Http11.

- [ ] **Step 1-4: Apply same changes as Task 7 to Http10 StateMachine**
- [ ] **Step 5-6: Apply same changes as Task 8 to Http10ConnectionStage**
- [ ] **Step 7: Commit**

---

### Task 10: Migrate Http2 StateMachine + ConnectionStage

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http2/StateMachine.cs`
- Modify: `src/TurboHTTP/Protocol/Http2/FrameDecoder.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Http20ConnectionStage.cs`

HTTP/2 uses `NetworkBuffer` for frame decoding. Map:
- `NetworkBuffer` → `TransportBuffer` in StateMachine and FrameDecoder
- Connection stage signal handling same as HTTP/1.1

- [ ] **Step 1: Update Http2 StateMachine — NetworkBuffer → TransportBuffer**
- [ ] **Step 2: Update FrameDecoder — NetworkBuffer → TransportBuffer**
- [ ] **Step 3: Update Http20ConnectionStage — same pattern as Task 8**
- [ ] **Step 4: Commit**

---

### Task 11: Migrate Http3 StateMachine + ConnectionStage

**Files:**
- Modify: `src/TurboHTTP/Protocol/Http3/StateMachine.cs`
- Modify: `src/TurboHTTP/Protocol/Http3/StreamManager.cs`
- Modify: `src/TurboHTTP/Protocol/Http3/QpackStreamHandler.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Http30ConnectionStage.cs`

HTTP/3 uses `RoutedNetworkBuffer` (with `StreamId` and `StreamTypeValue`). Map to `MultiplexedData(TransportBuffer, long StreamId)`. The `StreamTypeValue` can be carried on a separate message or encoded differently.

- [ ] **Step 1: Update Http3 StateMachine**
- [ ] **Step 2: Update StreamManager — RoutedNetworkBuffer → MultiplexedData/TransportBuffer**
- [ ] **Step 3: Update QpackStreamHandler — RoutedNetworkBuffer → TransportBuffer + OpenStream**
- [ ] **Step 4: Update Http30ConnectionStage — QuicCloseItem → TransportDisconnected etc.**
- [ ] **Step 5: Commit**

---

## Phase 4: ClientStreamOwner + Routing Stages Migration

### Task 12: Migrate ClientStreamOwner to use Transport ConnectionManagers with PoolConfigRegistry + IPoolingStrategy

> **DONE** — `ClientStreamOwner` already uses `PoolConfigRegistry` + per-version `IPoolingStrategy`.

**Files:**
- `src/TurboHTTP/Streams/Lifecycle/ClientStreamOwner.cs` — already migrated

**Architecture:** A single `TcpConnectionManagerActor` serves all TCP-based HTTP versions (1.0, 1.1, 2.0). It receives a `PoolConfigRegistry` with per-version pool configs keyed by pool key strings. The transport stage gets the correct `IPoolingStrategy` per version for lease-return decisions.

```csharp
// In MaterializeStream:
var opts = create.ClientOptions;

var poolRegistry = new PoolConfigRegistry(
        new TcpPoolConfig(/* default = http11 config */))
    .Register(PoolKeys.Http10, new TcpPoolConfig(
        MaxConnectionsPerHost: int.MaxValue,
        IdleTimeout: TimeSpan.Zero,
        ConnectionLifetime: TimeSpan.Zero,
        ReuseOnUpstreamFinish: false))
    .Register(PoolKeys.Http11, new TcpPoolConfig(/* from Http1Options */))
    .Register(PoolKeys.Http2, new TcpPoolConfig(/* from Http2Options */));

_tcpConnectionManager = Context.ActorOf(
    Props.Create(() => new TcpConnectionManagerActor(poolRegistry)), "tcp-pool");

_quicConnectionManager = Context.ActorOf(
    Props.Create(() => new QuicConnectionManagerActor()), "quic-pool");

var transports = new TransportRegistry()
    .Register(new Version(1, 0), new TcpTransportFactory(_tcpConnectionManager, new Http10PoolingStrategy()))
    .Register(new Version(1, 1), new TcpTransportFactory(_tcpConnectionManager, new Http11PoolingStrategy()))
    .Register(new Version(2, 0), new TcpTransportFactory(_tcpConnectionManager, new Http2PoolingStrategy()))
    .Register(new Version(3, 0), new QuicTransportFactory(_quicConnectionManager));
```

**Important:** Do NOT create separate `TcpConnectionManagerActor` instances per HTTP version. A single connection manager with the `PoolConfigRegistry` handles all TCP versions — it resolves the right `TcpPoolConfig` per host via `TransportOptions.PoolKey`. Each `TcpTransportFactory` gets its own `IPoolingStrategy` for stage-level lease decisions.

---

### Task 13: Migrate routing stages (GroupByRequestEndpoint, HostKeyMergeBack, EndpointDispatch, GroupByExtensions)

**Files:**
- Modify: `src/TurboHTTP/Streams/Stages/Internal/GroupByRequestEndpointStage.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Internal/HostKeyMergeBack.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Internal/EndpointDispatchStage.cs`
- Modify: `src/TurboHTTP/Streams/Stages/Internal/GroupByExtensions.cs`

These stages use `RequestEndpoint` for routing. `RequestEndpoint` is an IO type but is fundamentally an HTTP routing concept, not a transport concept. **Decision: Keep `RequestEndpoint` but move it to a shared location** (e.g., `TurboHTTP.Internal`) or keep importing from IO until this task deletes IO.

For the delete-IO goal, `RequestEndpoint` should be copied/moved to TurboHTTP since it's HTTP-specific, not transport-specific.

- [ ] **Step 1: Move `RequestEndpoint` to `TurboHTTP/Internal/RequestEndpoint.cs`**

Copy the struct from `Servus.Akka/IO/RequestEndpoint.cs` to `TurboHTTP/Internal/RequestEndpoint.cs`, change namespace to `TurboHTTP.Internal`.

- [ ] **Step 2: Update all TurboHTTP files that reference `Servus.Akka.IO.RequestEndpoint`** to use `TurboHTTP.Internal.RequestEndpoint`

- [ ] **Step 3: Update routing stages to remove `using Servus.Akka.IO`**

- [ ] **Step 4: Commit**

---

### Task 14: Update remaining TurboHTTP references

**Files:**
- Modify: `src/TurboHTTP/TurboHttpClient.cs` — `NetworkBuffer.ConfigurePoolSize` → `TransportBuffer.ConfigurePoolSize`
- Modify: Engine files, ProtocolCoreBuilder, etc. — remove IO imports, use Transport

- [ ] **Step 1: Update TurboHttpClient**
- [ ] **Step 2: Update all remaining `using Servus.Akka.IO` in TurboHTTP**
- [ ] **Step 3: Build TurboHTTP.slnx and verify zero IO references remain**
- [ ] **Step 4: Commit**

---

## Phase 5: Test Migration

### Task 15: Update TurboHTTP.Tests.Shared test utilities

**Files:**
- Modify: `src/TurboHTTP.Tests.Shared/` — all files using IO types

Replace `NetworkBuffer` with `TransportBuffer`, `IInputItem`/`IOutputItem` with Transport equivalents, `FakeOps`/`MockTransportOperations` with Transport versions.

- [ ] **Step 1: Update ScriptedFakeConnectionStage, EngineFakeConnectionStage, etc.**
- [ ] **Step 2: Update NetworkBufferTestExtensions → TransportBufferTestExtensions**
- [ ] **Step 3: Update MockTransportOperations in Tests.Shared**
- [ ] **Step 4: Commit**

---

### Task 16: Update TurboHTTP.Tests

**Files:**
- Modify: All files in `src/TurboHTTP.Tests/` referencing IO types

- [ ] **Step 1: Update Http11StateMachineSpec**
- [ ] **Step 2: Update Http11StateMachineReconnectSpec**
- [ ] **Step 3: Update Http3 TransportSelectionSpec**
- [ ] **Step 4: Run tests, fix failures**
- [ ] **Step 5: Commit**

---

### Task 17: Update TurboHTTP.StreamTests + AcceptanceTests

**Files:**
- Modify: All files in `src/TurboHTTP.StreamTests/` and `src/TurboHTTP.AcceptanceTests/`

- [ ] **Step 1: Update StreamTests (TransportRegistrySpec, StageOrderingSpec, etc.)**
- [ ] **Step 2: Update AcceptanceTests (ModuleInit, all spec files)**
- [ ] **Step 3: Run full test suite**
- [ ] **Step 4: Commit**

---

## Phase 6: Delete IO Namespace

### Task 18: Delete Servus.Akka.IO and old IO tests

**Files:**
- Delete: `src/Servus.Akka/IO/` (entire directory)
- Delete: `src/Servus.Akka.Tests/IO/` (entire directory)
- Delete: `src/Servus.Akka.Tests/Utils/InMemoryConnectionFactory.cs`
- Delete: `src/Servus.Akka.Tests/Utils/SlowConnectionFactory.cs`
- Delete: `src/Servus.Akka.Tests/Utils/FailOnceConnectionFactory.cs`
- Delete: `src/Servus.Akka.Tests/Utils/InMemoryQuicConnectionFactory.cs`
- Delete: `src/Servus.Akka.Tests/Utils/SlowQuicConnectionFactory.cs`
- Delete: `src/Servus.Akka.Tests/Utils/MockTransportOperations.cs` (old IO version)
- Delete: `src/Servus.Akka.Tests/Utils/FakeClientProvider.cs`
- Delete: `src/Servus.Akka.Tests/Utils/NetworkBufferTestExtensions.cs`

- [ ] **Step 1: Delete IO directory**

```bash
rm -rf src/Servus.Akka/IO
rm -rf src/Servus.Akka.Tests/IO
```

- [ ] **Step 2: Delete old test utilities that reference IO types**

- [ ] **Step 3: Build entire solution — verify zero compile errors**

```bash
dotnet build --configuration Release src/TurboHTTP.slnx
```

- [ ] **Step 4: Run full test suite**

```bash
dotnet test src/TurboHTTP.slnx
```

- [ ] **Step 5: Commit**

```
feat(transport): complete IO→Transport migration

Replace Servus.Akka.IO with Servus.Akka.Transport throughout.
IPoolingStrategy per HTTP version replaces hard-coded reuse logic.
Delete entire IO namespace.
```

---

## Summary

| Phase | Tasks | Description |
|-------|-------|-------------|
| 1 | 1-3 | IPoolingStrategy per HTTP version |
| 2 | 4-6 | OptionsFactory, TransportRegistry, ConnectionShape |
| 3 | 7-11 | Protocol StateMachines + ConnectionStages |
| 4 | 12-14 | ClientStreamOwner, routing stages, RequestEndpoint |
| 5 | 15-17 | All test projects |
| 6 | 18 | Delete IO namespace |
