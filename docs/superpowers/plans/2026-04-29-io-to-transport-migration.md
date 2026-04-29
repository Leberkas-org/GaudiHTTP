# IO → Transport Migration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all `Servus.Akka.IO` usage in TurboHTTP with `Servus.Akka.Transport`, implement `IPoolingStrategy` per HTTP version **in TurboHTTP** (not Servus.Akka), and delete the IO namespace.

**Architecture:** The old IO layer uses `IInputItem`/`IOutputItem` with `NetworkBuffer`, `ConnectItem`, `StreamAcquireItem`, `ConnectionReuseItem`, `CloseSignalItem` as the protocol boundary. The new Transport layer uses `ITransportInbound`/`ITransportOutbound` with `TransportBuffer`, `ConnectTransport`, `TransportData`, `TransportDisconnected`, `TransportConnected`. Connection pooling behavior (which was hard-coded per HTTP version in the old `TcpConnectionManagerActor`) moves to `IPoolingStrategy` implementations. Protocol-level ConnectionReuseEvaluator decisions map to `PoolAction.Reuse`/`PoolAction.Dispose` via the strategy.

**Boundary rule:** `IPoolingStrategy` (interface), `PoolAction`, `DisconnectReason`, `TransportOptions` stay in `Servus.Akka.Transport` — they are generic transport abstractions. The **HTTP-version-specific implementations** (`Http10PoolingStrategy`, `Http11PoolingStrategy`, `Http2PoolingStrategy`) live in `TurboHTTP.Streams.Pooling` because they encode HTTP semantics. Tests live in `TurboHTTP.Tests`.

**Tech Stack:** C# 12, Akka.NET, Akka.Streams, xUnit v3

---

## Phase 1: IPoolingStrategy Implementations

### Task 1: HTTP/1.0 Pooling Strategy

**Files:**
- Create: `src/TurboHTTP/Streams/Pooling/Http10PoolingStrategy.cs`
- Test: `src/TurboHTTP.Tests/Streams/Pooling/Http10PoolingStrategySpec.cs`

HTTP/1.0: No reuse by default. Connections are always new. MaxConnectionsPerHost = unlimited (int.MaxValue).
When server sends `Connection: Keep-Alive`, the *caller* (protocol SM) decides via `PoolAction` — strategy says "always dispose".

- [ ] **Step 1: Write the failing test**

```csharp
// src/TurboHTTP.Tests/Streams/Pooling/Http10PoolingStrategySpec.cs
using Servus.Akka.Transport;
using TurboHTTP.Streams.Pooling;

namespace TurboHTTP.Tests.Streams.Pooling;

public sealed class Http10PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_be_unlimited()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(int.MaxValue, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_false()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.False(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Dispose()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_be_zero()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(TimeSpan.Zero, strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLifetime_should_be_zero()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(TimeSpan.Zero, strategy.ConnectionLifetime);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Streams.Pooling.Http10PoolingStrategySpec"`
Expected: FAIL — `Http10PoolingStrategy` does not exist

- [ ] **Step 3: Write implementation**

```csharp
// src/TurboHTTP/Streams/Pooling/Http10PoolingStrategy.cs
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Pooling;

public sealed class Http10PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost => int.MaxValue;
    public TimeSpan IdleTimeout => TimeSpan.Zero;
    public TimeSpan ConnectionLifetime => TimeSpan.Zero;

    public bool CanReuse(TransportOptions options) => false;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Dispose;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Dispose;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Http10PoolingStrategySpec"`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

---

### Task 2: HTTP/1.1 Pooling Strategy

**Files:**
- Create: `src/TurboHTTP/Streams/Pooling/Http11PoolingStrategy.cs`
- Test: `src/TurboHTTP.Tests/Streams/Pooling/Http11PoolingStrategySpec.cs`

HTTP/1.1: Persistent connections by default. MaxConnectionsPerHost = 6 (browser convention). Idle timeout and connection lifetime configurable.

- [ ] **Step 1: Write the failing test**

```csharp
// src/TurboHTTP.Tests/Http11PoolingStrategySpec.cs
using Servus.Akka.Transport;
using TurboHTTP.Streams.Pooling;

namespace TurboHTTP.Tests.Steams.Pooling;

public sealed class Http11PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_default_to_6()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(6, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(maxConnectionsPerHost: 10);
        Assert.Equal(10, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_true()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.True(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Reuse()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Reuse, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_have_default()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.True(strategy.IdleTimeout > TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(idleTimeout: TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLifetime_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(connectionLifetime: TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(20), strategy.ConnectionLifetime);
    }

    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Reuse()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Reuse, strategy.OnUpstreamFinish(new object()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Streams.Pooling.Http11PoolingStrategySpec"`
Expected: FAIL

- [ ] **Step 3: Write implementation**

```csharp
// src/TurboHTTP/Streams/Pooling/Http11PoolingStrategy.cs
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Pooling;

public sealed class Http11PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost { get; }
    public TimeSpan IdleTimeout { get; }
    public TimeSpan ConnectionLifetime { get; }

    public Http11PoolingStrategy(
        int maxConnectionsPerHost = 6,
        TimeSpan? idleTimeout = null,
        TimeSpan? connectionLifetime = null)
    {
        MaxConnectionsPerHost = maxConnectionsPerHost;
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        ConnectionLifetime = connectionLifetime ?? TimeSpan.FromMinutes(10);
    }

    public bool CanReuse(TransportOptions options) => true;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
}
```

- [ ] **Step 4: Run test to verify it passes**

- [ ] **Step 5: Commit**

---

### Task 3: HTTP/2 Pooling Strategy

**Files:**
- Create: `src/TurboHTTP/Streams/Pooling/Http2PoolingStrategy.cs`
- Test: `src/TurboHTTP.Tests/Streams/Pooling/Http2PoolingStrategySpec.cs`

HTTP/2: Multiplexed connections. Each `TcpConnectionStage` gets its own connection — no reuse at transport layer (the multiplexing happens at the HTTP/2 stream layer). MaxConnectionsPerHost = 1 per stage instance, but multiple stages can coexist.

- [ ] **Step 1: Write the failing test**

```csharp
// src/TurboHTTP.Tests/Streams/Pooling/Http2PoolingStrategySpec.cs
using Servus.Akka.Transport;
using TurboHTTP.Streams.Pooling;

namespace TurboHTTP.Tests.Streams.Pooling;

public sealed class Http2PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TlsTransportOptions
    {
        Host = "example.com",
        Port = 443
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_be_1()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(1, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_false()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.False(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnUpstreamFinish(new object()));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_accept_custom_value()
    {
        var strategy = new Http2PoolingStrategy(idleTimeout: TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), strategy.IdleTimeout);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

- [ ] **Step 3: Write implementation**

```csharp
// src/TurboHTTP/Streams/Pooling/Http2PoolingStrategy.cs
using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Pooling;

public sealed class Http2PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost { get; }
    public TimeSpan IdleTimeout { get; }
    public TimeSpan ConnectionLifetime { get; }

    public Http2PoolingStrategy(
        int maxConnectionsPerHost = 1,
        TimeSpan? idleTimeout = null,
        TimeSpan? connectionLifetime = null)
    {
        MaxConnectionsPerHost = maxConnectionsPerHost;
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        ConnectionLifetime = connectionLifetime ?? Timeout.InfiniteTimeSpan;
    }

    public bool CanReuse(TransportOptions options) => false;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Dispose;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Dispose;
}
```

- [ ] **Step 4: Run test to verify it passes**

- [ ] **Step 5: Commit**

---

## Phase 2: OptionsFactory + TransportRegistry Migration

### Task 4: Migrate OptionsFactory from IO types to Transport types

**Files:**
- Modify: `src/TurboHTTP/Internal/OptionsFactory.cs`

Replace `TcpOptions`/`TlsOptions`/`QuicOptions` with `TcpTransportOptions`/`TlsTransportOptions`/`QuicTransportOptions`. Return `TransportOptions` instead of `TcpOptions`. Note: Port changes from `int` to `ushort`.

- [ ] **Step 1: Replace OptionsFactory**

```csharp
// src/TurboHTTP/Internal/OptionsFactory.cs
using System.Net.Security;
using Servus.Akka.Transport;

namespace TurboHTTP.Internal;

internal static class OptionsFactory
{
    internal static TransportOptions Build(string host, ushort port, string scheme, Version version,
        TurboClientOptions clientOptions)
    {
        var isTls = scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                    || scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        var effectivePort = port != 0 ? port : (ushort)(isTls ? 443 : 80);
        List<SslApplicationProtocol>? alpn = version switch
        {
            { Major: 3, Minor: 0 } => [SslApplicationProtocol.Http3],
            { Major: 2, Minor: 0 } => [SslApplicationProtocol.Http2],
            { Major: 1, Minor: 1 } => [SslApplicationProtocol.Http11],
            _ => null
        };

        if (version is { Major: 3, Minor: 0 })
        {
            return new QuicTransportOptions
            {
                Host = host,
                Port = effectivePort,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ConnectTimeout = clientOptions.ConnectTimeout,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                AllowConnectionMigration = clientOptions.Http3.AllowConnectionMigration,
                AllowEarlyData = clientOptions.Http3.AllowEarlyData,
                ApplicationProtocols = alpn,
                AutoReconnect = true
            };
        }

        if (isTls)
        {
            return new TlsTransportOptions
            {
                Host = host,
                Port = effectivePort,
                TargetHost = host,
                ServerCertificateValidationCallback = clientOptions.EffectiveServerCertificateValidationCallback,
                ClientCertificates = clientOptions.ClientCertificates,
                EnabledSslProtocols = clientOptions.EnabledSslProtocols,
                ConnectTimeout = clientOptions.ConnectTimeout,
                SocketSendBufferSize = clientOptions.SocketSendBufferSize,
                SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
                UseProxy = clientOptions.UseProxy,
                Proxy = clientOptions.Proxy,
                DefaultProxyCredentials = clientOptions.DefaultProxyCredentials,
                ApplicationProtocols = alpn,
            };
        }

        return new TcpTransportOptions
        {
            Host = host,
            Port = effectivePort,
            ConnectTimeout = clientOptions.ConnectTimeout,
            SocketSendBufferSize = clientOptions.SocketSendBufferSize,
            SocketReceiveBufferSize = clientOptions.SocketReceiveBufferSize,
            UseProxy = clientOptions.UseProxy,
            Proxy = clientOptions.Proxy,
            DefaultProxyCredentials = clientOptions.DefaultProxyCredentials,
        };
    }
}
```

Note: Removed `RequestEndpoint` parameter — caller now passes `host`, `port`, `scheme`, `version` directly.

- [ ] **Step 2: Verify build compiles** (will fail on callers — that's expected, fixed in later tasks)

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

Each protocol StateMachine emits old IO messages (`ConnectItem`, `StreamAcquireItem`, `NetworkBuffer`, `ConnectionReuseItem`). These need to be replaced with Transport messages (`ConnectTransport`, `TransportData`). The `ConnectionReuseItem` is removed — pooling decisions are now made by `IPoolingStrategy` inside the `TcpConnectionManagerActor`.

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

### Task 12: Migrate ClientStreamOwner to use Transport ConnectionManagers with IPoolingStrategy

**Files:**
- Modify: `src/TurboHTTP/Streams/Lifecycle/ClientStreamOwner.cs`

Replace old IO `TcpConnectionManagerActor(idleTimeout, connectionLifetime, maxConns)` and `QuicConnectionManagerActor(idleTimeout, connectionLifetime)` with new Transport constructors that take `IPoolingStrategy`.

- [ ] **Step 1: Update actor creation with pooling strategies**

```csharp
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;
using Servus.Akka.Transport.Quic;
using TurboHTTP.Streams.Pooling;

// In MaterializeStream:

var http10Strategy = new Http10PoolingStrategy();
var http11Strategy = new Http11PoolingStrategy(
    maxConnectionsPerHost: create.ClientOptions.Http1.MaxConnectionsPerServer,
    idleTimeout: create.ClientOptions.PooledConnectionIdleTimeout,
    connectionLifetime: create.ClientOptions.PooledConnectionLifetime);
var http2Strategy = new Http2PoolingStrategy(
    idleTimeout: create.ClientOptions.PooledConnectionIdleTimeout,
    connectionLifetime: create.ClientOptions.PooledConnectionLifetime);

_tcpConnectionManager = Context.ActorOf(
    Props.Create(() => new TcpConnectionManagerActor(
        new TcpConnectionFactory(), http11Strategy)),
    "tcp-pool");

_quicConnectionManager = Context.ActorOf(
    Props.Create(() => new QuicConnectionManagerActor(
        new QuicConnectionFactory())),
    "quic-pool");

var tcpFactory = new TcpTransportFactory(_tcpConnectionManager, http11Strategy);
var quicFactory = new QuicTransportFactory(_quicConnectionManager);

var transports = new TransportRegistry()
    .Register(new Version(1, 0), () => new TcpTransportFactory(_tcpConnectionManager, http10Strategy).Create())
    .Register(new Version(1, 1), () => tcpFactory.Create())
    .Register(new Version(2, 0), () => new TcpTransportFactory(_tcpConnectionManager, http2Strategy).Create())
    .Register(new Version(3, 0), () => quicFactory.Create());
```

- [ ] **Step 2: Commit**

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
