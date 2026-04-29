# QUIC Transport Stage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a protocol-agnostic QUIC transport stage as `Flow<ITransportOutbound, ITransportInbound>` in `Servus.Akka.Transport.Quic`, replacing the HTTP/3-entangled `Servus.Akka.IO.Quic` implementation.

**Architecture:** GraphStage + StageActor + separate QuicTransportStateMachine (testable via shared ITransportOperations). Generic stream multiplexing by ID + direction. Direct stream I/O (no Pipe+Channel). Behavior-oriented StreamContext, StreamHandle, QuicConnectionHandle. Both leases internal to stage.

**Tech Stack:** Akka.Streams (GraphStage, StageActor), System.Net.Quic, xUnit v3

**Design Spec:** `docs/superpowers/specs/2026-04-29-quic-transport-stage-design.md`

**Prerequisite:** TCP transport plan (Task 1-2) must be completed first — shared Transport types (`TransportData`, `DisconnectReason.Transient`, expanded `IPoolingStrategy`) are created there.

---

## File Map

### Shared Transport types (modified)

| File | Responsibility |
|---|---|
| `src/Servus.Akka/Transport/ITransportOutbound.cs` | Add `MultiplexedData` record |
| `src/Servus.Akka/Transport/ITransportInbound.cs` | Add `DataRejected`, `ConnectionMigrationDetected` records |
| `src/Servus.Akka/Transport/QuicTransportOptions.cs` | Add `AutoReconnect`, `MaxConnectionsPerHost`, `ConnectionLifetime` |

### New QUIC transport files

| File | Responsibility |
|---|---|
| `src/Servus.Akka/Transport/Quic/QuicTransportEvent.cs` | IQuicTransportEvent hierarchy |
| `src/Servus.Akka/Transport/Quic/StreamHandle.cs` | Behavior-oriented per-stream I/O |
| `src/Servus.Akka/Transport/Quic/StreamContext.cs` | Per-stream state (behavior-oriented) |
| `src/Servus.Akka/Transport/Quic/QuicConnectionHandle.cs` | Behavior-oriented connection (OpenStream, AcceptInbound) |
| `src/Servus.Akka/Transport/Quic/QuicConnectionLease.cs` | Internal lease (IsAlive, CanAcceptStream) |
| `src/Servus.Akka/Transport/Quic/QuicPumpManager.cs` | Per-stream inbound pumps + accept loop |
| `src/Servus.Akka/Transport/Quic/QuicTransportStateMachine.cs` | Connection lifecycle, stream map, reconnect |
| `src/Servus.Akka/Transport/Quic/QuicConnectionStage.cs` | GraphStage + Logic adapter |
| `src/Servus.Akka/Transport/Quic/QuicConnectionManagerActor.cs` | Per-host connection pool actor |
| `src/Servus.Akka/Transport/Quic/QuicConnectionFactory.cs` | IQuicConnectionFactory impl |
| `src/Servus.Akka/Transport/Quic/IQuicConnectionFactory.cs` | Factory interface |
| `src/Servus.Akka/Transport/Quic/QuicTransportFactory.cs` | ITransportFactory impl |
| `src/Servus.Akka/Transport/Quic/QuicClientProvider.cs` | .NET QUIC connection establishment |

### Test files

| File | Responsibility |
|---|---|
| `src/TurboHTTP.Tests/Transport/Quic/StreamHandleSpec.cs` | StreamHandle behavior tests |
| `src/TurboHTTP.Tests/Transport/Quic/StreamContextSpec.cs` | StreamContext behavior tests |
| `src/TurboHTTP.Tests/Transport/Quic/QuicTransportStateMachineSpec.cs` | State machine unit tests |

---

### Task 1: Add QUIC-specific shared message types

**Files:**
- Modify: `src/Servus.Akka/Transport/ITransportOutbound.cs`
- Modify: `src/Servus.Akka/Transport/ITransportInbound.cs`

- [ ] **Step 1: Add MultiplexedData to ITransportOutbound.cs**

Append after the existing `TransportData` record:

```csharp
public sealed record MultiplexedData(TransportBuffer Buffer, long StreamId) : ITransportOutbound, ITransportInbound;
```

- [ ] **Step 2: Add DataRejected and ConnectionMigrationDetected to ITransportInbound.cs**

Append after the existing records:

```csharp
public sealed record DataRejected(TransportBuffer Buffer) : ITransportInbound;

public sealed record ConnectionMigrationDetected(
    System.Net.EndPoint OldEndPoint,
    System.Net.EndPoint NewEndPoint) : ITransportInbound;
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/Servus.Akka/Transport/ITransportOutbound.cs src/Servus.Akka/Transport/ITransportInbound.cs
git commit -m "feat(transport): add MultiplexedData, DataRejected, ConnectionMigrationDetected"
```

---

### Task 2: Update QuicTransportOptions

**Files:**
- Modify: `src/Servus.Akka/Transport/QuicTransportOptions.cs`

- [ ] **Step 1: Add AutoReconnect, MaxConnectionsPerHost, ConnectionLifetime**

```csharp
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Servus.Akka.Transport;

public sealed record QuicTransportOptions : TransportOptions
{
    public string? TargetHost { get; init; }
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxBidirectionalStreams { get; init; } = 100;
    public int MaxUnidirectionalStreams { get; init; } = 3;
    public bool AllowEarlyData { get; init; }
    public bool AllowConnectionMigration { get; init; } = true;
    public X509CertificateCollection? ClientCertificates { get; init; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None;
    public List<SslApplicationProtocol>? ApplicationProtocols { get; init; }
    public bool AutoReconnect { get; init; }
    public int MaxConnectionsPerHost { get; init; } = 1;
    public TimeSpan ConnectionLifetime { get; init; } = TimeSpan.FromMinutes(10);
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/QuicTransportOptions.cs
git commit -m "feat(transport): add AutoReconnect, pool config to QuicTransportOptions"
```

---

### Task 3: Create IQuicTransportEvent hierarchy

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicTransportEvent.cs`

- [ ] **Step 1: Implement event hierarchy**

```csharp
using System.Net;

namespace Servus.Akka.Transport.Quic;

internal interface IQuicTransportEvent;

internal readonly record struct ConnectionLeaseAcquired(QuicConnectionLease Lease) : IQuicTransportEvent;

internal readonly record struct StreamLeaseAcquired(StreamHandle Handle, long StreamId) : IQuicTransportEvent;

internal readonly record struct AcquisitionFailed(Exception Error) : IQuicTransportEvent;

internal readonly record struct InboundData(TransportBuffer Buffer, long StreamId, int Gen) : IQuicTransportEvent;

internal readonly record struct InboundStreamAccepted(Stream Stream, long StreamId) : IQuicTransportEvent;

internal readonly record struct InboundComplete(DisconnectReason Reason, int Gen, long StreamId) : IQuicTransportEvent;

internal readonly record struct InboundPumpFailed(Exception Error, long StreamId) : IQuicTransportEvent;

internal readonly record struct OutboundWriteDone(long StreamId) : IQuicTransportEvent;

internal readonly record struct OutboundWriteFailed(Exception Error, long StreamId) : IQuicTransportEvent;

internal readonly record struct MigrationDetected(EndPoint OldEndPoint, EndPoint NewEndPoint) : IQuicTransportEvent;

internal readonly record struct EarlyDataRejected(TransportBuffer Buffer) : IQuicTransportEvent;
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicTransportEvent.cs
git commit -m "feat(transport/quic): add IQuicTransportEvent hierarchy"
```

---

### Task 4: Create StreamHandle (behavior-oriented)

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/StreamHandle.cs`
- Create: `src/TurboHTTP.Tests/Transport/Quic/StreamHandleSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace TurboHTTP.Tests.Transport.Quic;

public sealed class StreamHandleSpec
{
    [Fact(Timeout = 5000)]
    public async Task WriteAsync_should_write_buffer_to_stream()
    {
        var ms = new MemoryStream();
        var handle = new StreamHandle(ms, null);

        var buffer = TransportBuffer.Rent(16);
        buffer.FullMemory.Span[0] = 0xAA;
        buffer.FullMemory.Span[1] = 0xBB;
        buffer.Length = 2;

        await handle.WriteAsync(buffer);

        Assert.Equal(2, ms.Position);
        Assert.Equal(0xAA, ms.GetBuffer()[0]);
        Assert.Equal(0xBB, ms.GetBuffer()[1]);
    }

    [Fact(Timeout = 5000)]
    public async Task ReadAsync_should_read_from_stream()
    {
        var ms = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        var handle = new StreamHandle(ms, null);

        var buf = new byte[16];
        var read = await handle.ReadAsync(buf, CancellationToken.None);

        Assert.Equal(3, read);
        Assert.Equal(0x01, buf[0]);
    }

    [Fact(Timeout = 5000)]
    public void CompleteWrites_should_invoke_callback()
    {
        var called = false;
        var handle = new StreamHandle(Stream.Null, () => called = true);

        handle.CompleteWrites();

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public void CompleteWrites_should_not_throw_when_no_callback()
    {
        var handle = new StreamHandle(Stream.Null, null);
        handle.CompleteWrites();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.StreamHandleSpec"`
Expected: FAIL — `StreamHandle` does not exist in new namespace

- [ ] **Step 3: Implement StreamHandle**

```csharp
namespace Servus.Akka.Transport.Quic;

public sealed class StreamHandle : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly Action? _onWritesComplete;

    internal StreamHandle(Stream stream, Action? onWritesComplete)
    {
        _stream = stream;
        _onWritesComplete = onWritesComplete;
    }

    public ValueTask WriteAsync(TransportBuffer buffer)
    {
        var memory = buffer.Memory;
        var task = _stream.WriteAsync(memory);
        if (task.IsCompletedSuccessfully)
        {
            buffer.Dispose();
            return default;
        }

        return AwaitAndDispose(task, buffer);
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        return _stream.ReadAsync(buffer, ct);
    }

    public void CompleteWrites()
    {
        _onWritesComplete?.Invoke();
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private static async ValueTask AwaitAndDispose(ValueTask writeTask, TransportBuffer buffer)
    {
        try
        {
            await writeTask.ConfigureAwait(false);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.StreamHandleSpec"`
Expected: All 4 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/StreamHandle.cs src/TurboHTTP.Tests/Transport/Quic/StreamHandleSpec.cs
git commit -m "feat(transport/quic): add behavior-oriented StreamHandle"
```

---

### Task 5: Create StreamContext (behavior-oriented)

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/StreamContext.cs`
- Create: `src/TurboHTTP.Tests/Transport/Quic/StreamContextSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace TurboHTTP.Tests.Transport.Quic;

public sealed class StreamContextSpec
{
    [Fact(Timeout = 5000)]
    public void HasHandle_should_return_false_initially()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        Assert.False(ctx.HasHandle());
    }

    [Fact(Timeout = 5000)]
    public void HasHandle_should_return_true_after_attach()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        var handle = new StreamHandle(Stream.Null, null);
        ctx.AttachHandle(handle);
        Assert.True(ctx.HasHandle());
    }

    [Fact(Timeout = 5000)]
    public void Write_should_enqueue_when_no_handle()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        ctx.Write(buffer);

        Assert.True(ctx.TryDequeuePendingWrite(out var dequeued));
        Assert.Equal(4, dequeued!.Length);
        dequeued.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void TryDequeuePendingWrite_should_return_false_when_empty()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        Assert.False(ctx.TryDequeuePendingWrite(out _));
    }

    [Fact(Timeout = 5000)]
    public void Direction_should_return_construction_value()
    {
        var ctx = new StreamContext(StreamDirection.Unidirectional);
        Assert.Equal(StreamDirection.Unidirectional, ctx.Direction());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.StreamContextSpec"`
Expected: FAIL — `StreamContext` does not exist

- [ ] **Step 3: Implement StreamContext**

```csharp
using Akka.Actor;

namespace Servus.Akka.Transport.Quic;

internal sealed class StreamContext
{
    private readonly StreamDirection _direction;
    private StreamHandle? _handle;
    private readonly Queue<TransportBuffer> _pendingWrites = new();
    private IActorRef? _self;

    public StreamContext(StreamDirection direction)
    {
        _direction = direction;
    }

    internal void SetSelf(IActorRef self)
    {
        _self = self;
    }

    public bool HasHandle() => _handle is not null;

    public void AttachHandle(StreamHandle handle)
    {
        _handle = handle;
    }

    public void Write(TransportBuffer buffer)
    {
        if (_handle is null)
        {
            _pendingWrites.Enqueue(buffer);
            return;
        }

        _ = _handle.WriteAsync(buffer).AsTask().ContinueWith((t, state) =>
        {
            var self = (IActorRef)state!;
            if (t.IsFaulted)
            {
                self.Tell(new OutboundWriteFailed(t.Exception!.GetBaseException(), -1));
            }
        }, _self, TaskScheduler.Default);
    }

    public bool TryDequeuePendingWrite(out TransportBuffer? buffer)
    {
        return _pendingWrites.TryDequeue(out buffer);
    }

    public void CompleteWrites()
    {
        _handle?.CompleteWrites();
    }

    public StreamDirection Direction() => _direction;

    public void DisposePendingWrites()
    {
        while (_pendingWrites.TryDequeue(out var orphan))
        {
            orphan.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        DisposePendingWrites();
        if (_handle is not null)
        {
            await _handle.DisposeAsync().ConfigureAwait(false);
            _handle = null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.StreamContextSpec"`
Expected: All 5 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/StreamContext.cs src/TurboHTTP.Tests/Transport/Quic/StreamContextSpec.cs
git commit -m "feat(transport/quic): add behavior-oriented StreamContext"
```

---

### Task 6: Create QuicConnectionHandle

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicConnectionHandle.cs`

- [ ] **Step 1: Implement QuicConnectionHandle**

```csharp
using System.Net;
using System.Runtime.Versioning;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionHandle : IAsyncDisposable
{
    private readonly IClientProvider _provider;

    internal QuicConnectionHandle(IClientProvider provider)
    {
        _provider = provider;
    }

    public async Task<(Stream Stream, long StreamId)> OpenStreamAsync(
        StreamDirection direction, CancellationToken ct = default)
    {
        var stream = direction == StreamDirection.Bidirectional
            ? await _provider.GetStreamAsync(ct).ConfigureAwait(false)
            : await _provider.GetUnidirectionalStreamAsync(ct).ConfigureAwait(false);

        var streamId = stream is System.Net.Quic.QuicStream qs ? qs.Id : -1;
        return (stream, streamId);
    }

    public async Task<(Stream Stream, long StreamId)?> AcceptInboundStreamAsync(
        CancellationToken ct = default)
    {
        Stream stream;
        try
        {
            stream = await _provider.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }

        var streamId = stream is System.Net.Quic.QuicStream qs ? qs.Id : -1;
        return (stream, streamId);
    }

    public EndPoint? LocalEndPoint() => _provider.LocalEndPoint;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

#pragma warning restore CA1416
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds (IClientProvider must exist — check if it's in IO/ or needs porting)

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicConnectionHandle.cs
git commit -m "feat(transport/quic): add behavior-oriented QuicConnectionHandle"
```

---

### Task 7: Create QuicConnectionLease (internal)

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicConnectionLease.cs`

- [ ] **Step 1: Implement QuicConnectionLease**

```csharp
using System.Runtime.Versioning;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionLease : IDisposable
{
    private readonly long _createdTicks = Environment.TickCount64;
    private bool _alive = true;
    private int _activeStreams;
    private int _maxConcurrentStreams;
    private DateTime _lastActivity = DateTime.UtcNow;

    public QuicConnectionLease(QuicConnectionHandle handle, int maxConcurrentStreams)
    {
        Handle = handle;
        _maxConcurrentStreams = maxConcurrentStreams;
    }

    public QuicConnectionHandle Handle { get; }

    public bool IsAlive() => _alive;

    public bool IsExpired(TimeSpan maxLifetime)
    {
        if (maxLifetime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        return Environment.TickCount64 - _createdTicks > (long)maxLifetime.TotalMilliseconds;
    }

    public bool CanAcceptStream() => _alive && _activeStreams < _maxConcurrentStreams;

    public void MarkBusy()
    {
        _activeStreams++;
        _lastActivity = DateTime.UtcNow;
    }

    public void MarkIdle()
    {
        _activeStreams--;
        _lastActivity = DateTime.UtcNow;
    }

    public int ActiveStreams => _activeStreams;

    public DateTime LastActivity => _lastActivity;

    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;
        _ = Handle.DisposeAsync().AsTask();
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicConnectionLease.cs
git commit -m "feat(transport/quic): add internal QuicConnectionLease"
```

---

### Task 8: Create QuicPumpManager

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicPumpManager.cs`

- [ ] **Step 1: Implement QuicPumpManager**

```csharp
using System.Buffers;
using Akka.Actor;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

internal sealed class QuicPumpManager
{
    private readonly IActorRef _self;
    private CancellationTokenSource? _pumpsCts;
    private CancellationTokenSource? _acceptCts;

    public QuicPumpManager(IActorRef self)
    {
        _self = self;
    }

    public void StartInboundPump(StreamHandle handle, long streamId, int gen)
    {
        _pumpsCts ??= new CancellationTokenSource();
        _ = DirectStreamPumpAsync(handle, streamId, _pumpsCts.Token, _self, gen);
    }

    public void StartAcceptLoop(QuicConnectionHandle connectionHandle)
    {
        _acceptCts?.Cancel();
        _acceptCts?.Dispose();
        _acceptCts = new CancellationTokenSource();
        _ = AcceptLoopAsync(connectionHandle, _self, _acceptCts.Token);
    }

    public void StopAll()
    {
        _acceptCts?.Cancel();
        _acceptCts?.Dispose();
        _acceptCts = null;

        _pumpsCts?.Cancel();
        _pumpsCts?.Dispose();
        _pumpsCts = null;
    }

    private static async Task AcceptLoopAsync(
        QuicConnectionHandle handle, IActorRef self, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await handle.AcceptInboundStreamAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                if (result is not null)
                {
                    await result.Value.Stream.DisposeAsync().ConfigureAwait(false);
                }
                return;
            }

            if (result is null)
            {
                continue;
            }

            self.Tell(new InboundStreamAccepted(result.Value.Stream, result.Value.StreamId));
        }
    }

    private static async Task DirectStreamPumpAsync(
        StreamHandle handle, long streamId, CancellationToken ct,
        IActorRef self, int gen)
    {
        var closeReason = DisconnectReason.Graceful;
        var pool = MemoryPool<byte>.Shared;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var owner = pool.Rent(16384);
                int bytesRead;
                try
                {
                    bytesRead = await handle.ReadAsync(owner.Memory, ct).ConfigureAwait(false);
                }
                catch
                {
                    owner.Dispose();
                    throw;
                }

                if (bytesRead == 0)
                {
                    owner.Dispose();
                    break;
                }

                var tb = TransportBuffer.Rent(bytesRead);
                owner.Memory.Span[..bytesRead].CopyTo(tb.FullMemory.Span);
                tb.Length = bytesRead;
                owner.Dispose();

                self.Tell(new InboundData(tb, streamId, gen));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            self.Tell(new InboundPumpFailed(ex, streamId));
            return;
        }

        self.Tell(new InboundComplete(closeReason, gen, streamId));
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicPumpManager.cs
git commit -m "feat(transport/quic): add QuicPumpManager with direct stream I/O"
```

---

### Task 9: Create QuicTransportStateMachine

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicTransportStateMachine.cs`
- Create: `src/TurboHTTP.Tests/Transport/Quic/QuicTransportStateMachineSpec.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Quic;

public sealed class QuicTransportStateMachineSpec
{
    private sealed class StubOps : ITransportOperations
    {
        public readonly List<ITransportInbound> PushedInbound = [];
        public int PullCount;
        public bool Completed;
        public readonly Dictionary<string, TimeSpan> Timers = new();
        public readonly HashSet<string> CancelledTimers = [];

        public void OnPushInbound(ITransportInbound item) => PushedInbound.Add(item);
        public void OnSignalPullOutbound() => PullCount++;
        public void OnCompleteStage() => Completed = true;
        public void OnScheduleTimer(string key, TimeSpan delay) => Timers[key] = delay;
        public void OnCancelTimer(string key) => CancelledTimers.Add(key);
        public ILoggingAdapter Log => NoLogger.Instance;
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_schedule_connect_timeout()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);
        var options = new QuicTransportOptions { Host = "localhost", Port = 443 };

        sm.HandlePush(new ConnectTransport(options));

        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_OpenStream_should_enqueue_when_not_connected()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandlePush(new OpenStream(1, StreamDirection.Bidirectional));

        Assert.True(ops.PullCount > 0);
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_should_complete_when_no_connection()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_MultiplexedData_should_signal_pull_when_no_stream()
    {
        var ops = new StubOps();
        var sm = new QuicTransportStateMachine(ops, ActorRefs.Nobody, ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        sm.HandlePush(new MultiplexedData(buffer, 1));

        Assert.True(ops.PullCount > 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.QuicTransportStateMachineSpec"`
Expected: FAIL — `QuicTransportStateMachine` does not exist

- [ ] **Step 3: Implement QuicTransportStateMachine**

```csharp
using System.Net;
using Akka.Actor;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

public sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _connectionManager;
    private readonly IActorRef _self;

    private QuicConnectionHandle? _connectionHandle;
    private QuicConnectionLease? _connectionLease;
    private int _connectionGen;
    private ConnectTransport? _pendingConnect;
    private bool _autoReconnect;
    private bool _upstreamFinished;
    private bool _isReconnecting;
    private CancellationTokenSource? _acquireCts;
    private EndPoint? _lastLocalEndPoint;

    private readonly Dictionary<long, StreamContext> _streams = new();
    private readonly Queue<(long StreamId, StreamDirection Direction)> _pendingStreamOpens = new();
    private QuicPumpManager? _pumpManager;

    public QuicTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        IActorRef self)
    {
        _ops = ops;
        _connectionManager = connectionManager;
        _self = self;
    }

    public void Dispatch(IQuicTransportEvent evt)
    {
        switch (evt)
        {
            case ConnectionLeaseAcquired e:
                OnConnectionLeaseAcquired(e.Lease);
                break;
            case StreamLeaseAcquired e:
                OnStreamLeaseAcquired(e.Handle, e.StreamId);
                break;
            case AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case InboundData e:
                if (e.Gen == _connectionGen)
                {
                    CheckForConnectionMigration();
                    _ops.OnPushInbound(new MultiplexedData(e.Buffer, e.StreamId));
                }
                else
                {
                    e.Buffer.Dispose();
                }
                break;
            case InboundStreamAccepted e:
                OnInboundStreamAccepted(e.Stream, e.StreamId);
                break;
            case InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.Reason, e.StreamId);
                }
                break;
            case InboundPumpFailed e:
                _ops.Log.Warning("QuicTransportStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(DisconnectReason.Error, e.StreamId);
                break;
            case OutboundWriteDone:
                _ops.OnSignalPullOutbound();
                break;
            case OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
            case MigrationDetected e:
                _ops.OnPushInbound(new ConnectionMigrationDetected(e.OldEndPoint, e.NewEndPoint));
                break;
            case EarlyDataRejected e:
                _ops.OnPushInbound(new DataRejected(e.Buffer));
                break;
        }
    }

    public void HandlePush(ITransportOutbound item)
    {
        switch (item)
        {
            case ConnectTransport connect:
                HandleConnectTransport(connect);
                break;
            case OpenStream open:
                HandleOpenStream(open.StreamId, open.Direction);
                break;
            case MultiplexedData data:
                HandleMultiplexedData(data);
                break;
            case CloseStream close:
                HandleCloseStream(close.StreamId);
                break;
            case DisconnectTransport:
                CleanupTransport();
                _ops.OnSignalPullOutbound();
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        _upstreamFinished = true;
        if (_connectionHandle is null)
        {
            _ops.OnCompleteStage();
            return;
        }

        _pumpManager?.StopAll();
        _ops.OnCompleteStage();
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    public void OnTimer(string? timerKey)
    {
        if (timerKey != ConnectTimerKey || _pendingConnect is null)
        {
            return;
        }

        _ops.Log.Warning("QuicTransportStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Options.Host, _pendingConnect.Options.Port);
        _pendingConnect = null;

        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Timeout));
        _ops.OnSignalPullOutbound();
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        CleanupTransport();
    }

    private void HandleConnectTransport(ConnectTransport connect)
    {
        if (connect.Options is QuicTransportOptions quicOpts)
        {
            _autoReconnect = quicOpts.AutoReconnect;
        }

        if (_connectionLease is not null)
        {
            _isReconnecting = true;
        }

        CleanupTransport();
        _pendingConnect = connect;
        AcquireConnection(connect);
        _ops.OnSignalPullOutbound();
    }

    private void HandleOpenStream(long streamId, StreamDirection direction)
    {
        if (_connectionHandle is null)
        {
            _pendingStreamOpens.Enqueue((streamId, direction));
            _ops.OnSignalPullOutbound();
            return;
        }

        var ctx = new StreamContext(direction);
        ctx.SetSelf(_self);
        _streams[streamId] = ctx;

        _ = _connectionHandle.OpenStreamAsync(direction)
            .ContinueWith((t, state) =>
            {
                var (self, sid) = ((IActorRef, long))state!;
                if (t.IsFaulted)
                {
                    self.Tell(new AcquisitionFailed(t.Exception!.GetBaseException()));
                    return;
                }

                var (stream, _) = t.Result;
                var handle = new StreamHandle(stream, null);
                self.Tell(new StreamLeaseAcquired(handle, sid));
            }, (_self, streamId), TaskScheduler.Default);

        _ops.OnSignalPullOutbound();
    }

    private void HandleMultiplexedData(MultiplexedData data)
    {
        if (_streams.TryGetValue(data.StreamId, out var ctx))
        {
            ctx.Write(data.Buffer);
        }
        else
        {
            data.Buffer.Dispose();
        }

        _ops.OnSignalPullOutbound();
    }

    private void HandleCloseStream(long streamId)
    {
        if (_streams.Remove(streamId, out var ctx))
        {
            ctx.CompleteWrites();
            _ = ctx.DisposeAsync();
        }

        _ops.OnSignalPullOutbound();
    }

    private void OnConnectionLeaseAcquired(QuicConnectionLease lease)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _pendingConnect = null;
        _connectionGen++;
        _connectionLease = lease;
        _connectionHandle = lease.Handle;
        _lastLocalEndPoint = _connectionHandle.LocalEndPoint();

        _pumpManager = new QuicPumpManager(_self);
        _pumpManager.StartAcceptLoop(_connectionHandle);

        if (_isReconnecting)
        {
            _isReconnecting = false;
        }

        _ops.OnPushInbound(new TransportConnected(default!));

        while (_pendingStreamOpens.TryDequeue(out var pending))
        {
            HandleOpenStream(pending.StreamId, pending.Direction);
        }
    }

    private void OnStreamLeaseAcquired(StreamHandle handle, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var ctx))
        {
            _ = handle.DisposeAsync();
            return;
        }

        ctx.AttachHandle(handle);
        _pumpManager?.StartInboundPump(handle, streamId, _connectionGen);

        while (ctx.TryDequeuePendingWrite(out var buffer))
        {
            ctx.Write(buffer!);
        }

        _ops.OnPushInbound(new StreamOpened(streamId, ctx.Direction()));
    }

    private void OnInboundStreamAccepted(Stream stream, long streamId)
    {
        var handle = new StreamHandle(stream, null);
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        ctx.SetSelf(_self);
        ctx.AttachHandle(handle);
        _streams[streamId] = ctx;

        _pumpManager?.StartInboundPump(handle, streamId, _connectionGen);
        _ops.OnPushInbound(new StreamOpened(streamId, StreamDirection.Bidirectional));
    }

    private void OnInboundComplete(DisconnectReason reason, long streamId)
    {
        if (_streams.Remove(streamId, out var ctx))
        {
            _ = ctx.DisposeAsync();
        }

        _ops.OnPushInbound(new StreamClosed(streamId, reason));
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        _ops.Log.Warning("QuicTransportStage: Outbound write failed — {0}", ex.Message);
        HandleConnectionFailure(DisconnectReason.Error);
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        _ops.OnCancelTimer(ConnectTimerKey);
        _ops.Log.Warning("QuicTransportStage: Acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        _pendingConnect = null;
        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Error));
        _ops.OnSignalPullOutbound();
    }

    private void HandleConnectionFailure(DisconnectReason reason)
    {
        foreach (var (streamId, ctx) in _streams)
        {
            _ops.OnPushInbound(new StreamClosed(streamId, reason));
            _ = ctx.DisposeAsync();
        }
        _streams.Clear();

        if (_autoReconnect && !_upstreamFinished)
        {
            _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Transient));
            _isReconnecting = true;
            _pumpManager?.StopAll();
            ReturnConnectionToPool(false);
            _connectionHandle = null;
            _connectionLease = null;
            _ops.OnSignalPullOutbound();
            return;
        }

        _ops.OnPushInbound(new TransportDisconnected(reason));
        _pumpManager?.StopAll();
        ReturnConnectionToPool(false);
        _connectionHandle = null;
        _connectionLease = null;

        if (_upstreamFinished)
        {
            _ops.OnCompleteStage();
        }
        else
        {
            _ops.OnSignalPullOutbound();
        }
    }

    private void CheckForConnectionMigration()
    {
        var currentLocal = _connectionHandle?.LocalEndPoint();
        if (currentLocal is null || _lastLocalEndPoint is null)
        {
            return;
        }

        if (!currentLocal.Equals(_lastLocalEndPoint))
        {
            var old = _lastLocalEndPoint;
            _lastLocalEndPoint = currentLocal;
            _self.Tell(new MigrationDetected(old, currentLocal));
        }
    }

    private void AcquireConnection(ConnectTransport connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        var quicOptions = (QuicTransportOptions)connect.Options;
        var acquireTask = QuicConnectionManagerActor.AcquireAsync(
            _connectionManager, quicOptions, _acquireCts.Token);

        acquireTask.PipeTo(_self,
            success: static lease => new ConnectionLeaseAcquired(lease),
            failure: static ex => new AcquisitionFailed(ex.GetBaseException()));

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void ReturnConnectionToPool(bool canReuse)
    {
        if (_connectionLease is null)
        {
            return;
        }

        var lease = _connectionLease;
        _connectionLease = null;
        _connectionManager.Tell(new QuicConnectionManagerActor.Release(lease, canReuse));
    }

    private void CleanupTransport()
    {
        _connectionGen++;
        _pumpManager?.StopAll();

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        foreach (var (_, ctx) in _streams)
        {
            _ = ctx.DisposeAsync();
        }
        _streams.Clear();
        _pendingStreamOpens.Clear();

        ReturnConnectionToPool(false);
        _connectionHandle = null;
        _connectionLease = null;
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Quic.QuicTransportStateMachineSpec"`
Expected: All 4 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicTransportStateMachine.cs src/TurboHTTP.Tests/Transport/Quic/QuicTransportStateMachineSpec.cs
git commit -m "feat(transport/quic): add QuicTransportStateMachine"
```

---

### Task 10: Create QuicConnectionStage

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/QuicConnectionStage.cs`

- [ ] **Step 1: Implement QuicConnectionStage**

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

public sealed class QuicConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly IActorRef _connectionManager;

    private readonly Inlet<ITransportOutbound> _in = new("QuicConnection.In");
    private readonly Outlet<ITransportInbound> _out = new("QuicConnection.Out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public QuicConnectionStage(IActorRef connectionManager)
    {
        _connectionManager = connectionManager;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, ITransportOperations
    {
        private readonly QuicConnectionStage _stage;
        private readonly Queue<ITransportInbound> _pendingReads = new();
        private QuicTransportStateMachine _sm = null!;

        public Logic(QuicConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () => _sm.HandlePush(Grab(stage._in)),
                onUpstreamFinish: () => _sm.HandleUpstreamFinish());

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._out, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    _sm.HandleDownstreamFinish();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnReceive);
            _sm = new QuicTransportStateMachine(this, _stage._connectionManager, stageActor.Ref);
            Pull(_stage._in);
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            if (args.message is IQuicTransportEvent evt)
            {
                _sm.Dispatch(evt);
            }
        }

        protected override void OnTimer(object timerKey)
            => _sm.OnTimer(timerKey as string);

        public override void PostStop() => _sm.PostStop();

        void ITransportOperations.OnPushInbound(ITransportInbound item)
        {
            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, item);
            }
            else
            {
                _pendingReads.Enqueue(item);
            }
        }

        void ITransportOperations.OnSignalPullOutbound()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        void ITransportOperations.OnCompleteStage() => CompleteStage();

        void ITransportOperations.OnScheduleTimer(string key, TimeSpan delay)
            => ScheduleOnce(key, delay);

        void ITransportOperations.OnCancelTimer(string key) => CancelTimer(key);

        ILoggingAdapter ITransportOperations.Log => Log;
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/QuicConnectionStage.cs
git commit -m "feat(transport/quic): add QuicConnectionStage GraphStage"
```

---

### Task 11: Create QuicConnectionManagerActor, factory, provider

**Files:**
- Create: `src/Servus.Akka/Transport/Quic/IQuicConnectionFactory.cs`
- Create: `src/Servus.Akka/Transport/Quic/QuicConnectionFactory.cs`
- Create: `src/Servus.Akka/Transport/Quic/QuicConnectionManagerActor.cs`
- Create: `src/Servus.Akka/Transport/Quic/QuicTransportFactory.cs`
- Create: `src/Servus.Akka/Transport/Quic/QuicClientProvider.cs`

- [ ] **Step 1: Create IQuicConnectionFactory**

```csharp
namespace Servus.Akka.Transport.Quic;

public interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct);
}
```

- [ ] **Step 2: Port QuicClientProvider**

Copy from `src/Servus.Akka/IO/Quic/QuicClientProvider.cs`. Change namespace to `Servus.Akka.Transport.Quic`. Replace `QuicOptions` with `QuicTransportOptions`. Adjust any `ITransportOptions` references.

- [ ] **Step 3: Implement QuicConnectionFactory**

```csharp
using System.Runtime.Versioning;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionFactory : IQuicConnectionFactory
{
    public static readonly QuicConnectionFactory Instance = new();

    public async Task<QuicConnectionLease> EstablishAsync(
        QuicTransportOptions options, CancellationToken ct = default)
    {
        var provider = new QuicClientProvider(options);
        await provider.ConnectAsync(ct).ConfigureAwait(false);

        var handle = new QuicConnectionHandle(provider);
        var lease = new QuicConnectionLease(handle, options.MaxBidirectionalStreams);

        return lease;
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 4: Implement QuicConnectionManagerActor**

Port from `src/Servus.Akka/IO/Quic/QuicConnectionManagerActor.cs` with these changes:
- `Acquire` takes `QuicTransportOptions` instead of `(QuicOptions, RequestEndpoint)`
- Pool keyed by `TransportOptions` (via `QuicTransportOptions`) instead of `RequestEndpoint`
- `MaxConnectionsPerHost`, `IdleTimeout`, `ConnectionLifetime` read from `QuicTransportOptions`
- Remove all `RequestEndpoint` references

```csharp
using System.Runtime.Versioning;
using Akka.Actor;

namespace Servus.Akka.Transport.Quic;

#pragma warning disable CA1416

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionManagerActor : ReceiveActor, IWithTimers
{
    public sealed record Acquire(
        QuicTransportOptions Options,
        TaskCompletionSource<QuicConnectionLease> Tcs,
        CancellationToken Token);

    public sealed record Release(QuicConnectionLease Lease, bool CanReuse);

    private sealed record Established(QuicConnectionLease Lease, Acquire Original);
    private sealed record EstablishFailed(Exception Ex, Acquire Original);
    private sealed class Evict { public static readonly Evict Instance = new(); }

    private sealed class HostState(TransportOptions options, int maxConnections)
    {
        public readonly TransportOptions Options = options;
        public readonly int MaxConnections = maxConnections;
        public readonly List<QuicConnectionLease> Leases = [];
        public readonly Queue<Acquire> Pending = new();
        public int Establishing;
    }

    private readonly Dictionary<TransportOptions, HostState> _hosts = new();
    private readonly IQuicConnectionFactory _factory;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    public static Task<QuicConnectionLease> AcquireAsync(
        IActorRef actor, QuicTransportOptions options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<QuicConnectionLease>();
        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<QuicConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }
        actor.Tell(new Acquire(options, tcs, ct));
        return tcs.Task;
    }

    public QuicConnectionManagerActor(IQuicConnectionFactory factory)
    {
        _factory = factory;
        Receive<Acquire>(OnAcquire);
        Receive<Release>(OnRelease);
        Receive<Established>(OnEstablished);
        Receive<EstablishFailed>(OnFailed);
        Receive<Evict>(_ => OnEvict());
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(EvictTimerKey, Evict.Instance,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void OnAcquire(Acquire msg)
    {
        if (msg.Tcs.Task.IsCompleted) return;

        var host = GetOrCreateHost(msg.Options);

        foreach (var lease in host.Leases)
        {
            if (!lease.CanAcceptStream() || lease.IsExpired(msg.Options.ConnectionLifetime))
            {
                continue;
            }

            lease.MarkBusy();
            if (msg.Tcs.TrySetResult(lease))
            {
                return;
            }
            lease.MarkIdle();
        }

        if (host.Leases.Count + host.Establishing < host.MaxConnections)
        {
            Establish(host, msg);
        }
        else
        {
            host.Pending.Enqueue(msg);
        }
    }

    private void OnRelease(Release msg)
    {
        msg.Lease.MarkIdle();

        if (!msg.CanReuse || !msg.Lease.IsAlive())
        {
            foreach (var host in _hosts.Values)
            {
                if (host.Leases.Remove(msg.Lease))
                {
                    break;
                }
            }
            if (msg.Lease.ActiveStreams == 0)
            {
                msg.Lease.Dispose();
            }
            return;
        }
    }

    private void OnEstablished(Established msg)
    {
        var host = GetOrCreateHost(msg.Original.Options);
        host.Establishing--;
        host.Leases.Add(msg.Lease);
        msg.Lease.MarkBusy();

        if (!msg.Original.Tcs.TrySetResult(msg.Lease))
        {
            OnRelease(new Release(msg.Lease, CanReuse: true));
        }
    }

    private void OnFailed(EstablishFailed msg)
    {
        if (_hosts.TryGetValue(msg.Original.Options, out var host))
        {
            host.Establishing--;
        }

        if (msg.Ex is OperationCanceledException oce)
        {
            msg.Original.Tcs.TrySetCanceled(oce.CancellationToken);
        }
        else
        {
            msg.Original.Tcs.TrySetException(msg.Ex);
        }
    }

    private void OnEvict()
    {
        foreach (var host in _hosts.Values)
        {
            var toRemove = host.Leases
                .Where(l => !l.IsAlive() || (l.ActiveStreams == 0 && l.IsExpired(TimeSpan.FromMinutes(10))))
                .ToList();

            foreach (var lease in toRemove)
            {
                host.Leases.Remove(lease);
                lease.Dispose();
            }
        }
    }

    protected override void PostStop()
    {
        Timers.CancelAll();
        foreach (var host in _hosts.Values)
        {
            while (host.Pending.TryDequeue(out var pending))
            {
                pending.Tcs.TrySetException(new ObjectDisposedException(nameof(QuicConnectionManagerActor)));
            }
            foreach (var lease in host.Leases)
            {
                lease.Dispose();
            }
        }
        _hosts.Clear();
    }

    private HostState GetOrCreateHost(QuicTransportOptions options)
    {
        if (!_hosts.TryGetValue(options, out var state))
        {
            state = new HostState(options, options.MaxConnectionsPerHost);
            _hosts[options] = state;
        }
        return state;
    }

    private void Establish(HostState host, Acquire msg)
    {
        host.Establishing++;
        _factory.EstablishAsync(msg.Options, msg.Token)
            .PipeTo(Self,
                success: lease => new Established(lease, msg),
                failure: ex => new EstablishFailed(ex, msg));
    }
}

#pragma warning restore CA1416
```

- [ ] **Step 5: Implement QuicTransportFactory**

```csharp
using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Servus.Akka.Transport.Quic;

public sealed class QuicTransportFactory
{
    private readonly IActorRef _connectionManager;

    public QuicTransportFactory(IActorRef connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Create()
    {
        return Flow.FromGraph(new QuicConnectionStage(_connectionManager));
    }
}
```

- [ ] **Step 6: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds (may need to fix IClientProvider references — check if shared or needs porting)

- [ ] **Step 7: Commit**

```bash
git add src/Servus.Akka/Transport/Quic/
git commit -m "feat(transport/quic): add QuicConnectionManagerActor, factory, provider"
```

---

### Task 12: Verify full build and test suite

- [ ] **Step 1: Build entire solution**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx`
Expected: Build succeeds. Old IO/Quic/ code stays untouched — this is a new parallel implementation.

- [ ] **Step 2: Run new QUIC transport tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Transport.Quic"`
Expected: All QUIC transport tests pass

- [ ] **Step 3: Run existing tests to verify no regression**

Run: `dotnet test --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Run: `dotnet test --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`
Expected: All existing tests still pass

- [ ] **Step 4: Commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix(transport/quic): resolve build and test issues"
```
