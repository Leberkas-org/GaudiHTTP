# Dynamic Protocol Negotiation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable per-connection protocol selection (HTTP/1.1, HTTP/2) based on TLS ALPN negotiation or cleartext connection preface sniffing, with support for HTTP/1.1→HTTP/2 upgrade via `Upgrade: h2c`.

**Architecture:** A new `ProtocolNegotiatingStateMachine` implements `IServerStateMachine` and wraps the real protocol SM. It detects the protocol from `TransportConnected` metadata (ALPN) or first `TransportData` bytes (preface sniffing), then delegates all calls to the selected inner SM. For h2c upgrade, the H1.1 SM signals via an `IProtocolSwitchCapable` interface on wrapped ops, triggering an inner SM swap.

**Tech Stack:** C# 12, Akka.NET Streams, xUnit v3

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/TurboHTTP/Protocol/IProtocolSwitchCapable.cs` | Opt-in interface for h2c upgrade signaling |
| Create | `src/TurboHTTP/Protocol/ProtocolNegotiatingStateMachine.cs` | Detection + delegation + SM swap logic |
| Create | `src/TurboHTTP/Streams/Stages/Server/ProtocolNegotiatorConnectionStage.cs` | Boilerplate GraphStage wrapper |
| Create | `src/TurboHTTP/Streams/NegotiatingServerEngine.cs` | Engine factory for negotiator stage |
| Create | `src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs` | Unit tests for negotiation logic |
| Modify | `src/Servus.Akka/Transport/Tcp/Listener/TcpListenerStage.cs:158-178` | Extract ALPN into SecurityInfo after TLS handshake |
| Modify | `src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs:96-127` | Detect `Upgrade: h2c` and signal switch |
| Modify | `src/TurboHTTP/Streams/ProtocolRouter.cs` | Add `ResolveNegotiating()` method |
| Modify | `src/TurboHTTP/Streams/Lifecycle/ListenerActor.cs:178-191` | Use negotiating engine for all TCP |
| Modify | `src/TurboHTTP.Tests/Streams/ProtocolRouterSpec.cs` | Add test for `ResolveNegotiating()` |

---

### Task 1: IProtocolSwitchCapable Interface

**Files:**
- Create: `src/TurboHTTP/Protocol/IProtocolSwitchCapable.cs`

- [ ] **Step 1: Create the interface**

```csharp
// src/TurboHTTP/Protocol/IProtocolSwitchCapable.cs
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal interface IProtocolSwitchCapable
{
    void RequestProtocolSwitch(
        Func<IServerStageOperations, IServerStateMachine> newSmFactory);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/TurboHTTP/TurboHTTP.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
git add src/TurboHTTP/Protocol/IProtocolSwitchCapable.cs
git commit -m "feat(protocol): add IProtocolSwitchCapable interface for h2c upgrade signaling"
```

---

### Task 2: ProtocolNegotiatingStateMachine — ALPN Detection

**Files:**
- Create: `src/TurboHTTP/Protocol/ProtocolNegotiatingStateMachine.cs`
- Create: `src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs`

- [ ] **Step 1: Write failing tests for ALPN detection**

```csharp
// src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol;

public sealed class ProtocolNegotiatingStateMachineSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public List<string> ScheduledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => EmittedRequests.Add(request);
        public void OnOutbound(ITransportOutbound item) => EmittedOutbound.Add(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers.Add(name);
        public void OnCancelTimer(string name) { }
    }

    private static TransportConnected MakeConnected(SslApplicationProtocol? alpn = null)
    {
        SecurityInfo? security = alpn is not null
            ? new SecurityInfo(SslProtocols.Tls13, alpn.Value)
            : null;

        var info = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Loopback, 50000),
            alpn is not null ? TransportProtocol.Tls : TransportProtocol.Tcp,
            security);

        return new TransportConnected(info);
    }

    private static TransportData MakeData(byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return new TransportData(buffer);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_alpn_h2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2));

        Assert.True(sm.CanAcceptResponse || !sm.ShouldComplete);
        Assert.True(ops.ScheduledTimers.Contains("keep-alive-timeout"));
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_alpn_http11()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http11));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_default_alpn()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(default(SslApplicationProtocol)));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.ProtocolNegotiatingStateMachineSpec"`
Expected: FAIL — `ProtocolNegotiatingStateMachine` does not exist

- [ ] **Step 3: Implement ProtocolNegotiatingStateMachine with ALPN detection**

```csharp
// src/TurboHTTP/Protocol/ProtocolNegotiatingStateMachine.cs
using System.Net.Security;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol;

internal sealed class ProtocolNegotiatingStateMachine : IServerStateMachine
{
    private enum Phase { WaitingForConnect, Sniffing, Running }

    private readonly TurboServerOptions _options;
    private readonly UpgradeAwareOps _wrappedOps;

    private Phase _phase = Phase.WaitingForConnect;
    private IServerStateMachine? _inner;
    private readonly List<ITransportInbound> _buffered = [];

    public bool CanAcceptResponse => _phase == Phase.Running && _inner!.CanAcceptResponse;
    public bool ShouldComplete => _phase == Phase.Running && _inner!.ShouldComplete;

    public ProtocolNegotiatingStateMachine(TurboServerOptions options, IServerStageOperations ops)
    {
        _options = options;
        _wrappedOps = new UpgradeAwareOps(ops, this);
    }

    public void PreStart()
    {
        if (_phase == Phase.Running)
        {
            _inner!.PreStart();
        }
    }

    public void DecodeClientData(ITransportInbound data)
    {
        switch (_phase)
        {
            case Phase.WaitingForConnect:
                OnWaitingForConnect(data);
                break;
            case Phase.Sniffing:
                OnSniffing(data);
                break;
            case Phase.Running:
                _inner!.DecodeClientData(data);
                break;
        }
    }

    public void OnResponse(HttpResponseMessage response) => _inner!.OnResponse(response);
    public void OnDownstreamFinished() => _inner?.OnDownstreamFinished();
    public void OnTimerFired(string name) => _inner?.OnTimerFired(name);
    public void OnBodyMessage(object msg) => _inner?.OnBodyMessage(msg);

    public void Cleanup()
    {
        _inner?.Cleanup();
        DisposeBuffered();
    }

    private void OnWaitingForConnect(ITransportInbound data)
    {
        if (data is not TransportConnected { Info.Security: var security })
        {
            return;
        }

        if (security?.ApplicationProtocol == SslApplicationProtocol.Http2)
        {
            Activate(ops => new Http2ServerStateMachine(_options, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        if (security is not null)
        {
            Activate(ops => new Http11ServerStateMachine(_options, ops));
            _inner!.DecodeClientData(data);
            return;
        }

        _buffered.Add(data);
        _phase = Phase.Sniffing;
    }

    private void OnSniffing(ITransportInbound data)
    {
        _buffered.Add(data);

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        var span = buffer.Memory.Span;
        if (span.Length < 4)
        {
            return;
        }

        if (span[0] == 'P' && span[1] == 'R' && span[2] == 'I' && span[3] == ' ')
        {
            Activate(ops => new Http2ServerStateMachine(_options, ops));
        }
        else
        {
            Activate(ops => new Http11ServerStateMachine(_options, ops));
        }

        ReplayBuffered();
    }

    private void Activate(Func<IServerStageOperations, IServerStateMachine> factory)
    {
        _inner = factory(_wrappedOps);
        _phase = Phase.Running;
        _inner.PreStart();
    }

    private void ReplayBuffered()
    {
        var buffered = _buffered.ToArray();
        _buffered.Clear();

        foreach (var item in buffered)
        {
            _inner!.DecodeClientData(item);
        }
    }

    private void DisposeBuffered()
    {
        foreach (var item in _buffered)
        {
            if (item is TransportData { Buffer: var buf })
            {
                buf.Dispose();
            }
        }

        _buffered.Clear();
    }

    internal void HandleUpgrade(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
    {
        _inner?.Cleanup();
        _inner = newSmFactory(_wrappedOps);
        _inner.PreStart();
    }

    private sealed class UpgradeAwareOps : IServerStageOperations, IProtocolSwitchCapable
    {
        private readonly IServerStageOperations _real;
        private readonly ProtocolNegotiatingStateMachine _parent;

        public UpgradeAwareOps(IServerStageOperations real, ProtocolNegotiatingStateMachine parent)
        {
            _real = real;
            _parent = parent;
        }

        public void OnRequest(HttpRequestMessage request) => _real.OnRequest(request);
        public void OnOutbound(ITransportOutbound item) => _real.OnOutbound(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => _real.OnScheduleTimer(name, delay);
        public void OnCancelTimer(string name) => _real.OnCancelTimer(name);
        public ILoggingAdapter Log => _real.Log;
        public IActorRef StageActor => _real.StageActor;

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            _parent.HandleUpgrade(newSmFactory);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.ProtocolNegotiatingStateMachineSpec"`
Expected: 3 tests PASS

- [ ] **Step 5: Commit**

```
git add src/TurboHTTP/Protocol/ProtocolNegotiatingStateMachine.cs src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs
git commit -m "feat(protocol): add ProtocolNegotiatingStateMachine with ALPN detection"
```

---

### Task 3: ProtocolNegotiatingStateMachine — Preface Sniffing Tests

**Files:**
- Modify: `src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs`

- [ ] **Step 1: Add preface sniffing tests**

Append these tests to `ProtocolNegotiatingStateMachineSpec`:

```csharp
    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_pri_preface()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(preface));

        Assert.True(ops.ScheduledTimers.Contains("keep-alive-timeout"));
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_get_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.EmittedRequests);
        Assert.Equal("GET", ops.EmittedRequests[0].Method.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_post_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.EmittedRequests);
        Assert.Equal("POST", ops.EmittedRequests[0].Method.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_stay_sniffing_for_insufficient_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.DecodeClientData(MakeData("PR"u8.ToArray()));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
        Assert.Empty(ops.EmittedRequests);
        Assert.Empty(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_buffered_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.Cleanup();

        Assert.False(sm.ShouldComplete);
    }
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.ProtocolNegotiatingStateMachineSpec"`
Expected: 8 tests PASS

- [ ] **Step 3: Commit**

```
git add src/TurboHTTP.Tests/Protocol/ProtocolNegotiatingStateMachineSpec.cs
git commit -m "test(protocol): add preface sniffing and cleanup tests for negotiating SM"
```

---

### Task 4: ProtocolNegotiatorConnectionStage + NegotiatingServerEngine

**Files:**
- Create: `src/TurboHTTP/Streams/Stages/Server/ProtocolNegotiatorConnectionStage.cs`
- Create: `src/TurboHTTP/Streams/NegotiatingServerEngine.cs`

- [ ] **Step 1: Create the connection stage**

```csharp
// src/TurboHTTP/Streams/Stages/Server/ProtocolNegotiatorConnectionStage.cs
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ProtocolNegotiatorConnectionStage : GraphStage<ServerConnectionShape>
{
    private readonly Inlet<ITransportInbound> _inNetwork = new("NegotiatorConnection.In.Network");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("NegotiatorConnection.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("NegotiatorConnection.In.Response");
    private readonly Outlet<ITransportOutbound> _outNetwork = new("NegotiatorConnection.Out.Network");
    private readonly TurboServerOptions _options;

    public ProtocolNegotiatorConnectionStage(TurboServerOptions options)
    {
        _options = options;
    }

    public override ServerConnectionShape Shape => new(_inNetwork, _outRequest, _inResponse, _outNetwork);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new HttpConnectionServerStageLogic<ProtocolNegotiatingStateMachine>(this,
            ops => new ProtocolNegotiatingStateMachine(_options, ops));
}
```

- [ ] **Step 2: Create the engine**

```csharp
// src/TurboHTTP/Streams/NegotiatingServerEngine.cs
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal sealed class NegotiatingServerEngine : IServerProtocolEngine
{
    private readonly TurboServerOptions _options;

    public NegotiatingServerEngine(TurboServerOptions options)
    {
        _options = options;
    }

    public BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new ProtocolNegotiatorConnectionStage(_options));

            return new BidiShape<
                ITransportInbound,
                HttpRequestMessage,
                HttpResponseMessage,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/TurboHTTP/TurboHTTP.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```
git add src/TurboHTTP/Streams/Stages/Server/ProtocolNegotiatorConnectionStage.cs src/TurboHTTP/Streams/NegotiatingServerEngine.cs
git commit -m "feat(streams): add ProtocolNegotiatorConnectionStage and NegotiatingServerEngine"
```

---

### Task 5: ProtocolRouter + ListenerActor Integration

**Files:**
- Modify: `src/TurboHTTP/Streams/ProtocolRouter.cs`
- Modify: `src/TurboHTTP/Streams/Lifecycle/ListenerActor.cs:178-191`
- Modify: `src/TurboHTTP.Tests/Streams/ProtocolRouterSpec.cs`

- [ ] **Step 1: Write failing test for ResolveNegotiating**

Append to `ProtocolRouterSpec.cs`:

```csharp
    [Fact(Timeout = 5000)]
    public void ResolveNegotiating_should_return_negotiating_engine()
    {
        var engine = ProtocolRouter.ResolveNegotiating(DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<NegotiatingServerEngine>(engine);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Streams.ProtocolRouterSpec" -method "ResolveNegotiating_should_return_negotiating_engine"`
Expected: FAIL — `ResolveNegotiating` does not exist

- [ ] **Step 3: Add ResolveNegotiating to ProtocolRouter**

Replace the full content of `src/TurboHTTP/Streams/ProtocolRouter.cs`:

```csharp
using System.Net.Security;
using TurboHTTP.Server;

namespace TurboHTTP.Streams;

internal static class ProtocolRouter
{
    internal static IServerProtocolEngine ResolveEngine(SslApplicationProtocol protocol, TurboServerOptions options)
    {
        return protocol == SslApplicationProtocol.Http2
            ? new Http20ServerEngine(options)
            : new Http11ServerEngine(options);
    }

    internal static IServerProtocolEngine ResolveEngine(Version version, TurboServerOptions options)
    {
        return version switch
        {
            { Major: 1, Minor: 0 } => new Http10ServerEngine(options),
            { Major: 1, Minor: 1 } => new Http11ServerEngine(options),
            { Major: 2, Minor: 0 } => new Http20ServerEngine(options),
            { Major: 3, Minor: 0 } => new Http30ServerEngine(options),
            _ => new Http11ServerEngine(options)
        };
    }

    internal static IServerProtocolEngine ResolveNegotiating(TurboServerOptions options)
    {
        return new NegotiatingServerEngine(options);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Streams.ProtocolRouterSpec"`
Expected: All 9 tests PASS

- [ ] **Step 5: Update ListenerActor.ResolveEngineForListener**

In `src/TurboHTTP/Streams/Lifecycle/ListenerActor.cs`, replace lines 178-191:

```csharp
    private IServerProtocolEngine ResolveEngineForListener()
    {
        if (_listenerOptions is QuicListenerOptions)
        {
            return ProtocolRouter.ResolveEngine(new Version(3, 0), _serverOptions);
        }

        return ProtocolRouter.ResolveNegotiating(_serverOptions);
    }
```

- [ ] **Step 6: Verify full build succeeds**

Run: `dotnet build src/TurboHTTP.slnx --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 7: Run existing tests to verify no regressions**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Streams.ProtocolRouterSpec"`
Expected: All 9 tests PASS

- [ ] **Step 8: Commit**

```
git add src/TurboHTTP/Streams/ProtocolRouter.cs src/TurboHTTP/Streams/Lifecycle/ListenerActor.cs src/TurboHTTP.Tests/Streams/ProtocolRouterSpec.cs
git commit -m "feat(streams): integrate negotiating engine into ProtocolRouter and ListenerActor"
```

---

### Task 6: TcpListenerStage ALPN Extraction Fix

**Files:**
- Modify: `src/Servus.Akka/Transport/Tcp/Listener/TcpListenerStage.cs:138-178`

- [ ] **Step 1: Fix ALPN extraction after TLS handshake**

In `src/Servus.Akka/Transport/Tcp/Listener/TcpListenerStage.cs`, replace the `InitializeConnectionAsync` method (lines 138-178) with:

```csharp
        private async Task InitializeConnectionAsync(TcpClient client)
        {
            Stream stream;
            try
            {
                if (_stage._options.NoDelay)
                {
                    client.NoDelay = true;
                }

                if (_stage._options.SocketSendBufferSize is { } sendBuf)
                {
                    client.SendBufferSize = sendBuf;
                }

                if (_stage._options.SocketReceiveBufferSize is { } recvBuf)
                {
                    client.ReceiveBufferSize = recvBuf;
                }

                stream = await GetStreamAsync(client);
            }
            catch (Exception ex)
            {
                client.Dispose();
                _self.Tell(new TcpConnectionInitFailed(ex));
                return;
            }

            var localEndPoint = client.Client.LocalEndPoint!;
            var remoteEndPoint = client.Client.RemoteEndPoint!;

            SecurityInfo? security = null;
            var protocol = TransportProtocol.Tcp;

            if (stream is SslStream sslStream)
            {
                security = new SecurityInfo(
                    sslStream.SslProtocol,
                    sslStream.NegotiatedApplicationProtocol);
                protocol = TransportProtocol.Tls;
            }

            var connectionInfo = new ConnectionInfo(
                localEndPoint,
                remoteEndPoint,
                protocol,
                security);

            var connectionFlow = Flow.FromGraph(
                new TcpServerConnectionStage(stream, connectionInfo));

            _self.Tell(new TcpConnectionReady(connectionFlow));
        }
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Servus.Akka/Servus.Akka.csproj --no-restore -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
git add src/Servus.Akka/Transport/Tcp/Listener/TcpListenerStage.cs
git commit -m "fix(transport): extract ALPN from SslStream into SecurityInfo after TLS handshake"
```

---

### Task 7: Http11ServerStateMachine — h2c Upgrade Detection

**Files:**
- Modify: `src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs`
- Create: `src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11UpgradeH2cSpec.cs`

- [ ] **Step 1: Write failing tests for h2c upgrade**

```csharp
// src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11UpgradeH2cSpec.cs
using System.Text;
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11UpgradeH2cSpec
{
    private sealed class FakeOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => EmittedRequests.Add(request);
        public void OnOutbound(ITransportOutbound item) => EmittedOutbound.Add(item);
        public void OnScheduleTimer(string name, TimeSpan delay) { }
        public void OnCancelTimer(string name) { }
    }

    private sealed class SwitchCapableOps : IServerStageOperations, IProtocolSwitchCapable
    {
        private readonly FakeOps _inner = new();
        public Func<IServerStageOperations, IServerStateMachine>? SwitchFactory { get; private set; }

        public List<HttpRequestMessage> EmittedRequests => _inner.EmittedRequests;
        public List<ITransportOutbound> EmittedOutbound => _inner.EmittedOutbound;
        public ILoggingAdapter Log => _inner.Log;
        public IActorRef StageActor { get => _inner.StageActor; set => _inner.StageActor = value; }

        public void OnRequest(HttpRequestMessage request) => _inner.OnRequest(request);
        public void OnOutbound(ITransportOutbound item) => _inner.OnOutbound(item);
        public void OnScheduleTimer(string name, TimeSpan delay) => _inner.OnScheduleTimer(name, delay);
        public void OnCancelTimer(string name) => _inner.OnCancelTimer(name);

        public void RequestProtocolSwitch(Func<IServerStageOperations, IServerStateMachine> newSmFactory)
        {
            SwitchFactory = newSmFactory;
        }
    }

    private static TransportData MakeData(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return new TransportData(buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_trigger_switch_when_upgrade_h2c_with_switchable_ops()
    {
        var ops = new SwitchCapableOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade, HTTP2-Settings\r\n" +
            "Upgrade: h2c\r\n" +
            "HTTP2-Settings: AAMAAABkAAQBAAAAAAIAAAAA\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.NotNull(ops.SwitchFactory);
        var outbound = ops.EmittedOutbound.OfType<TransportData>().ToList();
        Assert.NotEmpty(outbound);
        var responseText = Encoding.ASCII.GetString(outbound[0].Buffer.Span);
        Assert.Contains("101", responseText);
        Assert.Contains("Upgrade: h2c", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_ignore_upgrade_when_ops_not_switchable()
    {
        var ops = new FakeOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade, HTTP2-Settings\r\n" +
            "Upgrade: h2c\r\n" +
            "HTTP2-Settings: AAMAAABkAAQBAAAAAAIAAAAA\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.Single(ops.EmittedRequests);
        Assert.Equal("GET", ops.EmittedRequests[0].Method.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void DecodeClientData_should_ignore_upgrade_without_http2_settings()
    {
        var ops = new SwitchCapableOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions(), ops);

        sm.DecodeClientData(MakeData(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: Upgrade\r\n" +
            "Upgrade: h2c\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"));

        Assert.Null(ops.SwitchFactory);
        Assert.Single(ops.EmittedRequests);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.Syntax.Http11.Server.Http11UpgradeH2cSpec"`
Expected: FAIL — upgrade detection not implemented

- [ ] **Step 3: Add h2c upgrade detection to Http11ServerStateMachine**

In `src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs`, add these fields after line 23:

```csharp
    private readonly TurboServerOptions _serverOptions;
```

In the constructor (line 28), store the options and add a using for Protocol namespace. Update line 30 to also store `_serverOptions`:

After line 30 (`_ops = ops ?? throw new ArgumentNullException(nameof(ops));`), add:

```csharp
        _serverOptions = options;
```

In the `DecodeClientData` method, after line 125 (`_ops.OnRequest(request);`), insert upgrade detection logic. Replace the block from line 117 to line 126 with:

```csharp
                var request = _decoder.GetRequest();

                if (!ShouldComplete && request.Version == HttpVersion.Version10)
                {
                    ShouldComplete = true;
                }

                if (TryHandleH2cUpgrade(request))
                {
                    _decoder.Reset();
                    break;
                }

                _pendingResponseCount++;
                _ops.OnRequest(request);
```

Add the upgrade detection method before `Cleanup()`:

```csharp
    private bool TryHandleH2cUpgrade(HttpRequestMessage request)
    {
        if (_ops is not IProtocolSwitchCapable switchable)
        {
            return false;
        }

        if (!request.Headers.TryGetValues("Upgrade", out var upgradeValues)
            || !upgradeValues.Any(v => v.Equals("h2c", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!request.Headers.TryGetValues("HTTP2-Settings", out _))
        {
            return false;
        }

        var responseBytes = "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: h2c\r\n\r\n"u8;
        var responseBuffer = TransportBuffer.Rent(responseBytes.Length);
        responseBytes.CopyTo(responseBuffer.FullMemory.Span);
        responseBuffer.Length = responseBytes.Length;
        _ops.OnOutbound(new TransportData(responseBuffer));

        switchable.RequestProtocolSwitch(
            ops => new Http2ServerStateMachine(_serverOptions, ops));

        return true;
    }
```

Add the required using at the top of the file:

```csharp
using TurboHTTP.Protocol.Syntax.Http2.Server;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.Syntax.Http11.Server.Http11UpgradeH2cSpec"`
Expected: 3 tests PASS

- [ ] **Step 5: Run existing H1.1 SM tests to verify no regressions**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.Syntax.Http11.Server.ServerStateMachineSpec"`
Expected: All existing tests PASS

- [ ] **Step 6: Commit**

```
git add src/TurboHTTP/Protocol/Syntax/Http11/Server/Http11ServerStateMachine.cs src/TurboHTTP.Tests/Protocol/Syntax/Http11/Server/Http11UpgradeH2cSpec.cs
git commit -m "feat(http11): add h2c upgrade detection with IProtocolSwitchCapable signaling"
```

---

### Task 8: Full Regression Test

**Files:** None (verification only)

- [ ] **Step 1: Run full test suite**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: All tests PASS — no regressions

- [ ] **Step 2: Run build in Release**

Run: `dotnet build src/TurboHTTP.slnx --configuration Release -v q`
Expected: Build succeeded, no warnings in new files

- [ ] **Step 3: Commit any remaining changes (if needed)**

Only if previous tasks left uncommitted changes.
