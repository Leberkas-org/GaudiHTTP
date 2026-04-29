# TCP Transport Stage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a protocol-agnostic TCP transport stage as `Flow<ITransportOutbound, ITransportInbound>` in `Servus.Akka.Transport.Tcp`, replacing the HTTP-entangled `Servus.Akka.IO.Tcp` implementation.

**Architecture:** GraphStage + StageActor + separate TcpTransportStateMachine (testable via ITransportOperations callback interface). Bidirectional Pipe+Channel pumping managed by TcpPumpManager (lifecycle only). ConnectionHandle is behavior-oriented (methods, not properties). IPoolingStrategy injected into stage for lease return decisions.

**Tech Stack:** Akka.Streams (GraphStage, StageActor), System.IO.Pipelines (Pipe), System.Threading.Channels, xUnit v3

**Design Spec:** `docs/superpowers/specs/2026-04-29-tcp-transport-stage-design.md`

---

## File Map

### Shared Transport types (modified)

| File | Responsibility |
|---|---|
| `src/Servus.Akka/Transport/ITransportOutbound.cs` | Add `TransportData` record |
| `src/Servus.Akka/Transport/ITransportInbound.cs` | Add `TransportData` record |
| `src/Servus.Akka/Transport/DisconnectReason.cs` | Add `Transient` value |
| `src/Servus.Akka/Transport/IPoolingStrategy.cs` | Expand with OnIdle, OnDisconnect, OnUpstreamFinish |
| `src/Servus.Akka/Transport/TcpTransportOptions.cs` | Add AutoReconnect property |
| `src/Servus.Akka/Transport/ITransportFactory.cs` | Update flow signature to new types |

### New TCP transport files

| File | Responsibility |
|---|---|
| `src/Servus.Akka/Transport/Tcp/ITransportOperations.cs` | Callback interface for SM → Stage communication |
| `src/Servus.Akka/Transport/Tcp/ConnectionHandle.cs` | Behavior-oriented connection (Write, TryRead, SignalClose) |
| `src/Servus.Akka/Transport/Tcp/ConnectionLease.cs` | Slim lifecycle wrapper (IsAlive, IsExpired, Dispose) |
| `src/Servus.Akka/Transport/Tcp/ClientState.cs` | Internal Pipe+Channel state per connection |
| `src/Servus.Akka/Transport/Tcp/ClientByteMover.cs` | Bidirectional Stream↔Pipe↔Channel pump tasks |
| `src/Servus.Akka/Transport/Tcp/TcpTransportEvent.cs` | ITcpTransportEvent hierarchy |
| `src/Servus.Akka/Transport/Tcp/TcpPumpManager.cs` | Start/stop bidirectional pumps |
| `src/Servus.Akka/Transport/Tcp/TcpTransportStateMachine.cs` | All connection/write/read/reconnect logic |
| `src/Servus.Akka/Transport/Tcp/TcpConnectionStage.cs` | GraphStage + Logic adapter |
| `src/Servus.Akka/Transport/Tcp/TcpConnectionManagerActor.cs` | Per-host connection pool actor |
| `src/Servus.Akka/Transport/Tcp/TcpConnectionFactory.cs` | IConnectionFactory impl |
| `src/Servus.Akka/Transport/Tcp/TcpTransportFactory.cs` | ITransportFactory impl |
| `src/Servus.Akka/Transport/Tcp/TcpClientProvider.cs` | Socket creation, DNS, proxy |
| `src/Servus.Akka/Transport/Tcp/TlsClientProvider.cs` | TLS/SSL wrapper |
| `src/Servus.Akka/Transport/Tcp/DnsCache.cs` | DNS resolution caching |

### Test files

| File | Responsibility |
|---|---|
| `src/TurboHTTP.Tests/Transport/Tcp/ConnectionHandleSpec.cs` | ConnectionHandle behavior tests |
| `src/TurboHTTP.Tests/Transport/Tcp/ConnectionLeaseSpec.cs` | ConnectionLease lifecycle tests |
| `src/TurboHTTP.Tests/Transport/Tcp/TcpTransportStateMachineSpec.cs` | State machine unit tests |
| `src/TurboHTTP.Tests/Transport/Tcp/TcpPumpManagerSpec.cs` | Pump lifecycle tests |
| `src/TurboHTTP.StreamTests/Transport/Tcp/TcpConnectionStageSpec.cs` | Stage integration tests |

---

### Task 1: Update shared Transport message types

**Files:**
- Modify: `src/Servus.Akka/Transport/ITransportOutbound.cs`
- Modify: `src/Servus.Akka/Transport/ITransportInbound.cs`
- Modify: `src/Servus.Akka/Transport/DisconnectReason.cs`

- [ ] **Step 1: Add TransportData to ITransportOutbound.cs**

```csharp
namespace Servus.Akka.Transport;

public interface ITransportOutbound;

public sealed record ConnectTransport(TransportOptions Options) : ITransportOutbound;

public sealed record DisconnectTransport(DisconnectReason Reason) : ITransportOutbound;

public sealed record OpenStream(long StreamId, StreamDirection Direction) : ITransportOutbound;

public sealed record CloseStream(long StreamId) : ITransportOutbound;

public sealed record ConnectionReuse(PoolAction Action) : ITransportOutbound;

public sealed record TransportData(TransportBuffer Buffer) : ITransportOutbound, ITransportInbound;
```

- [ ] **Step 2: Add TransportData to ITransportInbound.cs**

```csharp
namespace Servus.Akka.Transport;

public interface ITransportInbound;

public sealed record TransportConnected(ConnectionInfo Info) : ITransportInbound;

public sealed record TransportDisconnected(DisconnectReason Reason) : ITransportInbound;

public sealed record TransportError(Exception Exception, bool Fatal) : ITransportInbound;

public sealed record StreamOpened(long StreamId, StreamDirection Direction) : ITransportInbound;

public sealed record StreamClosed(long StreamId, DisconnectReason Reason) : ITransportInbound;

public sealed record InboundStreamAccepted(long StreamId, long StreamType) : ITransportInbound;
```

Note: `TransportData` is defined in `ITransportOutbound.cs` since it implements both interfaces. Remove any duplicate definition from `ITransportInbound.cs`.

- [ ] **Step 3: Add Transient to DisconnectReason**

```csharp
namespace Servus.Akka.Transport;

public enum DisconnectReason
{
    Graceful,
    Timeout,
    Error,
    Evicted,
    Transient
}
```

- [ ] **Step 4: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds (existing code doesn't reference new records yet)

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/ITransportOutbound.cs src/Servus.Akka/Transport/ITransportInbound.cs src/Servus.Akka/Transport/DisconnectReason.cs
git commit -m "feat(transport): add TransportData record and Transient disconnect reason"
```

---

### Task 2: Expand IPoolingStrategy and TcpTransportOptions

**Files:**
- Modify: `src/Servus.Akka/Transport/IPoolingStrategy.cs`
- Modify: `src/Servus.Akka/Transport/TcpTransportOptions.cs`

- [ ] **Step 1: Expand IPoolingStrategy**

```csharp
namespace Servus.Akka.Transport;

public interface IPoolingStrategy
{
    int MaxConnectionsPerHost { get; }
    TimeSpan IdleTimeout { get; }
    TimeSpan ConnectionLifetime { get; }
    bool CanReuse(TransportOptions options);
    PoolAction OnRelease(TransportOptions options);
    PoolAction OnIdle(object lease);
    PoolAction OnDisconnect(object lease, DisconnectReason reason);
    PoolAction OnUpstreamFinish(object lease);
}
```

Note: `OnIdle`/`OnDisconnect`/`OnUpstreamFinish` use `object lease` because the strategy is shared across transport types and `ConnectionLease` is defined in the TCP namespace. The implementation casts to the concrete type.

- [ ] **Step 2: Add AutoReconnect to TcpTransportOptions**

```csharp
using System.Net;

namespace Servus.Akka.Transport;

public sealed record TcpTransportOptions : TransportOptions
{
    public bool UseProxy { get; init; }
    public IWebProxy? Proxy { get; init; }
    public ICredentials? DefaultProxyCredentials { get; init; }
    public bool AutoReconnect { get; init; }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build may fail if existing IPoolingStrategy implementations don't have the new methods. Fix any existing implementations to add the new methods (return `PoolAction.Dispose` as default).

- [ ] **Step 4: Commit**

```bash
git add src/Servus.Akka/Transport/IPoolingStrategy.cs src/Servus.Akka/Transport/TcpTransportOptions.cs
git commit -m "feat(transport): expand IPoolingStrategy with lifecycle methods, add AutoReconnect"
```

---

### Task 3: Create ITransportOperations and TcpTransportEvent

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/ITransportOperations.cs`
- Create: `src/Servus.Akka/Transport/Tcp/TcpTransportEvent.cs`

- [ ] **Step 1: Create ITransportOperations**

```csharp
using Akka.Event;

namespace Servus.Akka.Transport.Tcp;

public interface ITransportOperations
{
    void OnPushInbound(ITransportInbound item);
    void OnSignalPullOutbound();
    void OnCompleteStage();
    void OnScheduleTimer(string key, TimeSpan delay);
    void OnCancelTimer(string key);
    ILoggingAdapter Log { get; }
}
```

- [ ] **Step 2: Create ITcpTransportEvent hierarchy**

```csharp
namespace Servus.Akka.Transport.Tcp;

internal interface ITcpTransportEvent;

internal readonly record struct LeaseAcquired(ConnectionLease Lease) : ITcpTransportEvent;

internal readonly record struct AcquisitionFailed(Exception Error) : ITcpTransportEvent;

internal readonly record struct InboundBatch(ITransportInbound[] Batch, int Count, int Gen) : ITcpTransportEvent;

internal readonly record struct InboundComplete(DisconnectReason Reason, int Gen) : ITcpTransportEvent;

internal readonly record struct InboundPumpFailed(Exception Error) : ITcpTransportEvent;

internal readonly record struct OutboundWriteDone(int Gen) : ITcpTransportEvent;

internal readonly record struct OutboundWriteFailed(Exception Error) : ITcpTransportEvent;
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/
git commit -m "feat(transport/tcp): add ITransportOperations and ITcpTransportEvent"
```

---

### Task 4: Create ConnectionHandle (behavior-oriented)

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/ConnectionHandle.cs`
- Create: `src/TurboHTTP.Tests/Transport/Tcp/ConnectionHandleSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Channels;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class ConnectionHandleSpec
{
    [Fact(Timeout = 5000)]
    public void Write_should_push_buffer_to_outbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        handle.Write(buffer);

        Assert.True(outbound.Reader.TryRead(out var read));
        Assert.Equal(4, read!.Length);
        read.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void TryRead_should_return_buffer_from_inbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 8;
        inbound.Writer.TryWrite(buffer);

        Assert.True(handle.TryRead(out var read));
        Assert.Equal(8, read!.Length);
        read.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void SignalClose_should_complete_outbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        handle.SignalClose();

        Assert.False(outbound.Writer.TryWrite(TransportBuffer.Rent(1)));
    }

    [Fact(Timeout = 5000)]
    public void IsCancelled_should_reflect_cancellation_token()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, cts.Token);

        Assert.False(handle.IsCancelled);
        cts.Cancel();
        Assert.True(handle.IsCancelled);
        cts.Dispose();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.ConnectionHandleSpec"`
Expected: FAIL — `ConnectionHandle` constructor does not exist with new signature

- [ ] **Step 3: Implement ConnectionHandle**

```csharp
using System.Threading.Channels;

namespace Servus.Akka.Transport.Tcp;

public sealed class ConnectionHandle
{
    private readonly ChannelWriter<TransportBuffer> _outboundWriter;
    private readonly ChannelReader<TransportBuffer> _inboundReader;
    private readonly CancellationToken _token;

    public ConnectionHandle(
        ChannelWriter<TransportBuffer> outboundWriter,
        ChannelReader<TransportBuffer> inboundReader,
        CancellationToken token)
    {
        _outboundWriter = outboundWriter;
        _inboundReader = inboundReader;
        _token = token;
    }

    public void Write(TransportBuffer buffer)
    {
        if (!_outboundWriter.TryWrite(buffer))
        {
            buffer.Dispose();
        }
    }

    public bool TryRead(out TransportBuffer? buffer)
    {
        return _inboundReader.TryRead(out buffer);
    }

    public void SignalClose()
    {
        _outboundWriter.TryComplete();
    }

    public bool IsCancelled => _token.IsCancellationRequested;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.ConnectionHandleSpec"`
Expected: All 4 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/ConnectionHandle.cs src/TurboHTTP.Tests/Transport/Tcp/ConnectionHandleSpec.cs
git commit -m "feat(transport/tcp): add behavior-oriented ConnectionHandle"
```

---

### Task 5: Create ConnectionLease

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/ConnectionLease.cs`
- Create: `src/TurboHTTP.Tests/Transport/Tcp/ConnectionLeaseSpec.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Channels;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class ConnectionLeaseSpec
{
    private static (ConnectionLease lease, ConnectionHandle handle) CreateLease()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, cts.Token);
        var state = new ClientState(Stream.Null, PipeMode.Bidirectional);
        var lease = new ConnectionLease(handle, state, cts);
        return (lease, handle);
    }

    [Fact(Timeout = 5000)]
    public void IsAlive_should_return_true_initially()
    {
        var (lease, _) = CreateLease();
        Assert.True(lease.IsAlive());
        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void IsAlive_should_return_false_after_dispose()
    {
        var (lease, _) = CreateLease();
        lease.Dispose();
        Assert.False(lease.IsAlive());
    }

    [Fact(Timeout = 5000)]
    public void IsExpired_should_return_false_for_infinite_lifetime()
    {
        var (lease, _) = CreateLease();
        Assert.False(lease.IsExpired(Timeout.InfiniteTimeSpan));
        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_cancel_connection_handle()
    {
        var (lease, handle) = CreateLease();
        Assert.False(handle.IsCancelled);
        lease.Dispose();
        Assert.True(handle.IsCancelled);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.ConnectionLeaseSpec"`
Expected: FAIL — `ConnectionLease` does not exist in new namespace / `ClientState` does not exist yet

- [ ] **Step 3: Create ClientState**

```csharp
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace Servus.Akka.Transport.Tcp;

internal sealed class ClientState : IDisposable
{
    private static readonly PipeOptions InboundPipeOptions = new(
        pool: MemoryPool<byte>.Shared,
        minimumSegmentSize: 4096,
        pauseWriterThreshold: 0,
        resumeWriterThreshold: 0,
        useSynchronizationContext: false);

    private static readonly PipeOptions OutboundPipeOptions = new(
        pool: MemoryPool<byte>.Shared,
        minimumSegmentSize: 4096,
        pauseWriterThreshold: 1024 * 1024,
        resumeWriterThreshold: 512 * 1024,
        useSynchronizationContext: false);

    private static readonly UnboundedChannelOptions ChannelOptions = new()
    {
        SingleReader = true,
        SingleWriter = true
    };

    public Stream Stream { get; }
    public PipeMode Direction { get; }

    public Pipe InboundPipe { get; }
    public Pipe OutboundPipe { get; }

    private readonly Channel<TransportBuffer> _inboundChannel;
    private readonly Channel<TransportBuffer> _outboundChannel;

    public ChannelReader<TransportBuffer> InboundReader => _inboundChannel.Reader;
    public ChannelWriter<TransportBuffer> InboundWriter => _inboundChannel.Writer;
    public ChannelReader<TransportBuffer> OutboundReader => _outboundChannel.Reader;
    public ChannelWriter<TransportBuffer> OutboundWriter => _outboundChannel.Writer;

    public Action? OnWritesComplete { get; init; }

    public ClientState(Stream stream, PipeMode direction = PipeMode.Bidirectional)
    {
        Stream = stream;
        Direction = direction;
        InboundPipe = new Pipe(InboundPipeOptions);
        OutboundPipe = new Pipe(OutboundPipeOptions);
        _inboundChannel = Channel.CreateUnbounded<TransportBuffer>(ChannelOptions);
        _outboundChannel = Channel.CreateUnbounded<TransportBuffer>(ChannelOptions);
    }

    public void Dispose()
    {
        _inboundChannel.Writer.TryComplete();
        _outboundChannel.Writer.TryComplete();

        while (_inboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }
        while (_outboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }

        try { InboundPipe.Writer.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { InboundPipe.Reader.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { OutboundPipe.Writer.Complete(); }
        catch (InvalidOperationException) { _ = 0; }
        try { OutboundPipe.Reader.Complete(); }
        catch (InvalidOperationException) { _ = 0; }

        Stream.Dispose();
    }
}
```

- [ ] **Step 4: Implement ConnectionLease**

```csharp
namespace Servus.Akka.Transport.Tcp;

public sealed class ConnectionLease : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ClientState _state;
    private readonly long _createdTicks = Environment.TickCount64;
    private bool _alive = true;

    public ConnectionLease(ConnectionHandle handle, ClientState state, CancellationTokenSource cts)
    {
        Handle = handle;
        _state = state;
        _cts = cts;
    }

    public ConnectionHandle Handle { get; }

    public bool IsAlive() => _alive;

    public bool IsExpired(TimeSpan maxLifetime)
    {
        if (maxLifetime == Timeout.InfiniteTimeSpan)
        {
            return false;
        }

        return Environment.TickCount64 - _createdTicks > (long)maxLifetime.TotalMilliseconds;
    }

    public void Dispose()
    {
        if (!_alive)
        {
            return;
        }

        _alive = false;
        _cts.Cancel();
        _cts.Dispose();
        _state.Dispose();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.ConnectionLeaseSpec"`
Expected: All 4 tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/ClientState.cs src/Servus.Akka/Transport/Tcp/ConnectionLease.cs src/TurboHTTP.Tests/Transport/Tcp/ConnectionLeaseSpec.cs
git commit -m "feat(transport/tcp): add ClientState, ConnectionLease"
```

---

### Task 6: Create ClientByteMover

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/ClientByteMover.cs`

- [ ] **Step 1: Implement ClientByteMover**

Port from `src/Servus.Akka/IO/ClientByteMover.cs`, replacing `NetworkBuffer` with `TransportBuffer`:

```csharp
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;

namespace Servus.Akka.Transport.Tcp;

internal static class ClientByteMover
{
    public static Task MoveStreamToChannel(ClientState state, Action onClose, CancellationToken ct)
    {
        var fillTask = FillPipeFromStream(state.Stream, state.InboundPipe.Writer, ct);
        var drainTask = DrainPipeToChannel(state.InboundPipe.Reader, state.InboundWriter, onClose, ct);
        return Task.WhenAll(fillTask, drainTask);
    }

    public static Task MoveChannelToStream(ClientState state, Action onClose, CancellationToken ct)
    {
        var fillTask = FillPipeFromChannel(state.OutboundReader, state.OutboundPipe.Writer, ct);
        var drainTask = DrainPipeToStream(state.OutboundPipe.Reader, state.Stream, state.OnWritesComplete, onClose, ct);
        return Task.WhenAll(fillTask, drainTask);
    }

    private static async Task FillPipeFromStream(Stream stream, PipeWriter writer, CancellationToken ct)
    {
        Exception? error = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var mem = writer.GetMemory(512 * 1024);
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(mem, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { error = new AbruptCloseException(); return; }

                if (bytesRead == 0) { return; }

                writer.Advance(bytesRead);
                var flush = await writer.FlushAsync(ct).ConfigureAwait(false);
                if (flush.IsCompleted || flush.IsCanceled) { break; }
            }
        }
        finally
        {
            try { writer.Complete(error); }
            catch (InvalidOperationException) { _ = 0; }
        }
    }

    private static async Task DrainPipeToChannel(
        PipeReader reader, ChannelWriter<TransportBuffer> channel, Action onClose, CancellationToken ct)
    {
        var abrupt = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                {
                    var tb = TransportBuffer.Rent(segment.Length);
                    segment.Span.CopyTo(tb.FullMemory.Span);
                    tb.Length = segment.Length;
                    if (!channel.TryWrite(tb))
                    {
                        tb.Dispose();
                    }
                }

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    if (reader.TryRead(out var final) && !final.Buffer.IsEmpty)
                    {
                        reader.AdvanceTo(final.Buffer.End);
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            onClose();
            return;
        }
        catch (AbruptCloseException)
        {
            abrupt = true;
            onClose();
            return;
        }
        catch (Exception)
        {
            abrupt = true;
            onClose();
            return;
        }
        finally
        {
            try { reader.Complete(); }
            catch (InvalidOperationException) { _ = 0; }
            if (abrupt)
            {
                channel.TryComplete(new AbruptCloseException());
            }
            else
            {
                channel.TryComplete();
            }
        }

        onClose();
    }

    private static async Task FillPipeFromChannel(
        ChannelReader<TransportBuffer> channel, PipeWriter writer, CancellationToken ct)
    {
        try
        {
            while (await channel.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (channel.TryRead(out var buf))
                {
                    try
                    {
                        var span = writer.GetSpan(buf.Length);
                        buf.Span.CopyTo(span);
                        writer.Advance(buf.Length);
                    }
                    finally
                    {
                        buf.Dispose();
                    }
                }

                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { _ = 0; }
        catch (Exception) { _ = 0; }
        finally
        {
            try { writer.Complete(); }
            catch (InvalidOperationException) { _ = 0; }
        }
    }

    private static async Task DrainPipeToStream(
        PipeReader reader, Stream stream, Action? onWritesComplete, Action onClose, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                ReadResult result;
                try
                {
                    result = await reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { onClose(); return; }
                catch (Exception) { onClose(); return; }

                var buffer = result.Buffer;
                try
                {
                    if (!buffer.IsEmpty)
                    {
                        if (buffer.IsSingleSegment)
                        {
                            await stream.WriteAsync(buffer.First, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            using var owner = MemoryPool<byte>.Shared.Rent((int)buffer.Length);
                            buffer.CopyTo(owner.Memory.Span);
                            await stream.WriteAsync(owner.Memory[..(int)buffer.Length], ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { reader.AdvanceTo(buffer.End); onClose(); return; }
                catch (Exception) { reader.AdvanceTo(buffer.End); onClose(); return; }

                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted) { break; }
            }
        }
        finally
        {
            try { reader.Complete(); }
            catch (InvalidOperationException) { _ = 0; }
        }

        onWritesComplete?.Invoke();
    }
}
```

- [ ] **Step 2: Create AbruptCloseException if not shared**

Check if `AbruptCloseException` exists in `Transport/` namespace. If not, create it:

```csharp
namespace Servus.Akka.Transport.Tcp;

internal sealed class AbruptCloseException : Exception
{
    public AbruptCloseException() : base("Connection closed abruptly.") { }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/ClientByteMover.cs src/Servus.Akka/Transport/Tcp/AbruptCloseException.cs
git commit -m "feat(transport/tcp): add ClientByteMover with TransportBuffer"
```

---

### Task 7: Create TcpPumpManager

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/TcpPumpManager.cs`

- [ ] **Step 1: Implement TcpPumpManager**

```csharp
using System.Buffers;
using Akka.Actor;

namespace Servus.Akka.Transport.Tcp;

internal sealed class TcpPumpManager
{
    private readonly IActorRef _self;
    private CancellationTokenSource? _pumpsCts;

    public TcpPumpManager(IActorRef self)
    {
        _self = self;
    }

    public void StartPumps(ClientState state, int gen)
    {
        _pumpsCts?.Cancel();
        _pumpsCts?.Dispose();
        _pumpsCts = new CancellationTokenSource();

        var ct = _pumpsCts.Token;

        _ = RunInboundPump(state, gen, ct);
        _ = ClientByteMover.MoveChannelToStream(state, () =>
        {
            _self.Tell(new OutboundWriteDone(gen));
        }, ct);
    }

    public void StopPumps()
    {
        _pumpsCts?.Cancel();
        _pumpsCts?.Dispose();
        _pumpsCts = null;
    }

    private async Task RunInboundPump(ClientState state, int gen, CancellationToken ct)
    {
        _ = ClientByteMover.MoveStreamToChannel(state, () => { }, ct);

        var closeKind = DisconnectReason.Graceful;
        try
        {
            while (await state.InboundReader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                var batch = ArrayPool<ITransportInbound>.Shared.Rent(32);
                var count = 0;

                while (count < batch.Length && state.InboundReader.TryRead(out var buf))
                {
                    batch[count++] = new TransportData(buf);
                }

                if (count > 0)
                {
                    _self.Tell(new InboundBatch(batch, count, gen));
                }
                else
                {
                    ArrayPool<ITransportInbound>.Shared.Return(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _self.Tell(new InboundPumpFailed(ex));
            return;
        }

        _self.Tell(new InboundComplete(closeKind, gen));
    }
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/TcpPumpManager.cs
git commit -m "feat(transport/tcp): add TcpPumpManager with bidirectional pump lifecycle"
```

---

### Task 8: Create TcpTransportStateMachine

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/TcpTransportStateMachine.cs`
- Create: `src/TurboHTTP.Tests/Transport/Tcp/TcpTransportStateMachineSpec.cs`

- [ ] **Step 1: Write failing tests for the state machine**

```csharp
using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class TcpTransportStateMachineSpec
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

    private sealed class StubPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionLifetime => TimeSpan.FromMinutes(5);
        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Reuse;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_ConnectTransport_should_schedule_connect_timeout()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);
        var options = new TcpTransportOptions { Host = "localhost", Port = 8080 };

        sm.HandlePush(new ConnectTransport(options));

        Assert.Contains("connect-timeout", ops.Timers.Keys);
        Assert.True(ops.PullCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandlePush_TransportData_should_enqueue_when_not_connected()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        sm.HandlePush(new TransportData(buffer));

        Assert.True(ops.PullCount > 0);
        Assert.Empty(ops.PushedInbound);
    }

    [Fact(Timeout = 5000)]
    public void HandleUpstreamFinish_should_complete_when_no_connection()
    {
        var ops = new StubOps();
        var sm = new TcpTransportStateMachine(ops, ActorRefs.Nobody, new StubPoolingStrategy(), ActorRefs.Nobody);

        sm.HandleUpstreamFinish();

        Assert.True(ops.Completed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.TcpTransportStateMachineSpec"`
Expected: FAIL — `TcpTransportStateMachine` does not exist

- [ ] **Step 3: Implement TcpTransportStateMachine**

```csharp
using System.Buffers;
using Akka.Actor;

namespace Servus.Akka.Transport.Tcp;

public sealed class TcpTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _connectionManager;
    private readonly IPoolingStrategy _poolingStrategy;
    private readonly IActorRef _self;

    private ConnectionHandle? _handle;
    private ConnectionLease? _currentLease;
    private bool _leaseReturned;
    private int _connectionGen;
    private ConnectTransport? _pendingConnect;
    private bool _autoReconnect;

    private readonly Queue<TransportBuffer> _pendingWrites = new();

    private bool _upstreamFinished;
    private bool _isReconnecting;
    private TcpPumpManager? _pumpManager;
    private CancellationTokenSource? _acquireCts;

    public TcpTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        IPoolingStrategy poolingStrategy,
        IActorRef self)
    {
        _ops = ops;
        _connectionManager = connectionManager;
        _poolingStrategy = poolingStrategy;
        _self = self;
    }

    public void Dispatch(ITcpTransportEvent evt)
    {
        switch (evt)
        {
            case LeaseAcquired e:
                OnLeaseAcquired(e.Lease);
                break;
            case AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case InboundBatch e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundBatch(e.Batch, e.Count);
                }
                else
                {
                    ArrayPool<ITransportInbound>.Shared.Return(e.Batch);
                }
                break;
            case InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.Reason);
                }
                break;
            case InboundPumpFailed e:
                _ops.Log.Warning("TcpTransportStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(DisconnectReason.Error);
                break;
            case OutboundWriteDone:
                break;
            case OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
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
            case TransportData data:
                HandleTransportData(data);
                break;
            case DisconnectTransport disconnect:
                HandleDisconnectTransport(disconnect);
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        _upstreamFinished = true;
        if (_handle is null)
        {
            _ops.OnCompleteStage();
        }
        else if (_pendingWrites.Count == 0)
        {
            _connectionGen++;
            _pumpManager?.StopPumps();
            ReturnLeaseToPool(_poolingStrategy.OnUpstreamFinish(_currentLease!));
            _handle = null;
            _currentLease = null;
            _ops.OnCompleteStage();
        }
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

        _ops.Log.Warning("TcpTransportStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Options.Host, _pendingConnect.Options.Port);
        _pendingConnect = null;

        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Timeout));
        _ops.OnSignalPullOutbound();
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        CleanupTransport();

        while (_pendingWrites.TryDequeue(out var orphan))
        {
            orphan.Dispose();
        }
    }

    private void HandleConnectTransport(ConnectTransport connect)
    {
        if (connect.Options is TcpTransportOptions tcpOpts)
        {
            _autoReconnect = tcpOpts.AutoReconnect;
        }

        if (_currentLease is not null)
        {
            _isReconnecting = true;
        }

        CleanupTransport();
        _pendingConnect = connect;
        AcquireConnection(connect);
        _ops.OnSignalPullOutbound();
    }

    private void HandleTransportData(TransportData data)
    {
        if (_handle is null)
        {
            _pendingWrites.Enqueue(data.Buffer);
            _ops.OnSignalPullOutbound();
            return;
        }

        _handle.Write(data.Buffer);
        _ops.OnSignalPullOutbound();
    }

    private void HandleDisconnectTransport(DisconnectTransport disconnect)
    {
        var action = _poolingStrategy.OnRelease(_pendingConnect?.Options ?? _currentLease?.Handle is not null
            ? _pendingConnect!.Options
            : new TcpTransportOptions { Host = "", Port = 0 });
        CleanupTransport();
        _ops.OnSignalPullOutbound();
    }

    private void OnLeaseAcquired(ConnectionLease lease)
    {
        _ops.OnCancelTimer(ConnectTimerKey);

        _pendingConnect = null;
        _connectionGen++;
        _leaseReturned = false;
        _currentLease = lease;
        _handle = lease.Handle;

        _pumpManager = new TcpPumpManager(_self);
        // Note: StartPumps requires access to the internal ClientState.
        // The factory creates the lease with a ClientState; the pump manager
        // receives it via an internal accessor on the lease.

        if (_isReconnecting)
        {
            _isReconnecting = false;
            _ops.OnPushInbound(new TransportConnected(default!));
        }

        FlushPendingWrites();
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        _ops.OnCancelTimer(ConnectTimerKey);
        _ops.Log.Warning("TcpTransportStage: Connection acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        _pendingConnect = null;
        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Error));
        _ops.OnSignalPullOutbound();
    }

    private void OnInboundBatch(ITransportInbound[] batch, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _ops.OnPushInbound(batch[i]);
            batch[i] = null!;
        }

        ArrayPool<ITransportInbound>.Shared.Return(batch);
    }

    private void OnInboundComplete(DisconnectReason reason)
    {
        var poolAction = _poolingStrategy.OnDisconnect(_currentLease!, reason);

        if (_autoReconnect && _pendingConnect is null && !_upstreamFinished)
        {
            _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Transient));
            _isReconnecting = true;

            while (_pendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }

            var options = _currentLease is not null
                ? new TcpTransportOptions { Host = "", Port = 0, AutoReconnect = true }
                : new TcpTransportOptions { Host = "", Port = 0 };

            _leaseReturned = false;
            ReturnLeaseToPool(poolAction);
            _handle = null;
            _currentLease = null;

            // Re-acquire will be triggered by the engine sending a new ConnectTransport
            // or the SM could auto-reconnect here depending on design.
            _ops.OnSignalPullOutbound();
            return;
        }

        _ops.OnPushInbound(new TransportDisconnected(reason));

        _leaseReturned = false;
        ReturnLeaseToPool(poolAction);
        _pumpManager?.StopPumps();
        _handle = null;
        _currentLease = null;

        if (_upstreamFinished)
        {
            _ops.OnCompleteStage();
        }
        else
        {
            _ops.OnSignalPullOutbound();
        }
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        _ops.Log.Warning("TcpTransportStage: Outbound write failed — {0}", ex.Message);

        var poolAction = _poolingStrategy.OnDisconnect(_currentLease!, DisconnectReason.Error);
        _leaseReturned = false;
        ReturnLeaseToPool(poolAction);

        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Error));
        _pumpManager?.StopPumps();
        _handle = null;
        _currentLease = null;
        _ops.OnSignalPullOutbound();
    }

    private void AcquireConnection(ConnectTransport connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        _connectionManager.Tell(new TcpConnectionManagerActor.Acquire(
            connect.Options, new TaskCompletionSource<ConnectionLease>(), _acquireCts.Token));

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void ReturnLeaseToPool(PoolAction action)
    {
        if (_leaseReturned || _currentLease is null)
        {
            return;
        }

        _leaseReturned = true;
        var canReuse = action == PoolAction.Reuse;
        _connectionManager.Tell(new TcpConnectionManagerActor.Release(_currentLease, canReuse));
    }

    private void CleanupTransport()
    {
        _connectionGen++;
        _pumpManager?.StopPumps();

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        if (_currentLease is not null)
        {
            _leaseReturned = false;
            ReturnLeaseToPool(PoolAction.Dispose);
            _currentLease.Dispose();
            _currentLease = null;
            _handle = null;
        }
    }

    private void FlushPendingWrites()
    {
        while (_pendingWrites.TryDequeue(out var buffer))
        {
            if (_handle is not null)
            {
                _handle.Write(buffer);
            }
            else
            {
                buffer.Dispose();
            }
        }

        _ops.OnSignalPullOutbound();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Transport.Tcp.TcpTransportStateMachineSpec"`
Expected: All 3 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/TcpTransportStateMachine.cs src/TurboHTTP.Tests/Transport/Tcp/TcpTransportStateMachineSpec.cs
git commit -m "feat(transport/tcp): add TcpTransportStateMachine with IPoolingStrategy"
```

---

### Task 9: Create TcpConnectionStage

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/TcpConnectionStage.cs`

- [ ] **Step 1: Implement TcpConnectionStage**

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Servus.Akka.Transport.Tcp;

public sealed class TcpConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly IActorRef _connectionManager;
    private readonly IPoolingStrategy _poolingStrategy;

    private readonly Inlet<ITransportOutbound> _in = new("TcpConnection.In");
    private readonly Outlet<ITransportInbound> _out = new("TcpConnection.Out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public TcpConnectionStage(IActorRef connectionManager, IPoolingStrategy poolingStrategy)
    {
        _connectionManager = connectionManager;
        _poolingStrategy = poolingStrategy;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, ITransportOperations
    {
        private readonly TcpConnectionStage _stage;
        private readonly Queue<ITransportInbound> _pendingReads = new();
        private TcpTransportStateMachine _sm = null!;

        public Logic(TcpConnectionStage stage) : base(stage.Shape)
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
            _sm = new TcpTransportStateMachine(
                this,
                _stage._connectionManager,
                _stage._poolingStrategy,
                stageActor.Ref);
            Pull(_stage._in);
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            if (args.message is ITcpTransportEvent evt)
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
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/TcpConnectionStage.cs
git commit -m "feat(transport/tcp): add TcpConnectionStage GraphStage"
```

---

### Task 10: Create TcpConnectionManagerActor

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/TcpConnectionManagerActor.cs`

- [ ] **Step 1: Implement TcpConnectionManagerActor**

Port from `src/Servus.Akka/IO/Tcp/TcpConnectionManagerActor.cs` with these changes:
- `Acquire` takes `TransportOptions` instead of `(ITransportOptions, RequestEndpoint)`
- Pool keyed by `TransportOptions` instead of `RequestEndpoint`
- `HostState` uses `IPoolingStrategy` for limits
- Remove `IsHttp10` and version-specific logic
- `ConnectionLease` is the new slim type from `Transport.Tcp`

```csharp
using Akka.Actor;

namespace Servus.Akka.Transport.Tcp;

public sealed class TcpConnectionManagerActor : ReceiveActor, IWithTimers
{
    public sealed record Acquire(
        TransportOptions Options,
        TaskCompletionSource<ConnectionLease> Tcs,
        CancellationToken Token);

    public sealed record Release(ConnectionLease Lease, bool CanReuse);

    private sealed record Established(ConnectionLease Lease, Acquire Original);
    private sealed record EstablishFailed(Exception Ex, Acquire Original);

    private sealed class Evict
    {
        public static readonly Evict Instance = new();
    }

    private sealed class HostState(TransportOptions options, int maxConnections)
    {
        public readonly TransportOptions Options = options;
        public readonly int MaxConnections = maxConnections;
        public readonly List<ConnectionLease> Leases = [];
        public readonly Queue<ConnectionLease> Idle = new();
        public readonly Queue<Acquire> Pending = new();
        public int Establishing;
    }

    private readonly Dictionary<TransportOptions, HostState> _hosts = new();
    private readonly IConnectionFactory<ConnectionLease> _factory;
    private readonly IPoolingStrategy _poolingStrategy;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    public static Task<ConnectionLease> AcquireAsync(
        IActorRef actor, TransportOptions options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ConnectionLease>();

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<ConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }

        actor.Tell(new Acquire(options, tcs, ct));
        return tcs.Task;
    }

    public TcpConnectionManagerActor(IConnectionFactory<ConnectionLease> factory, IPoolingStrategy poolingStrategy)
    {
        _factory = factory;
        _poolingStrategy = poolingStrategy;

        Receive<Acquire>(OnAcquire);
        Receive<Release>(OnRelease);
        Receive<Established>(OnEstablished);
        Receive<EstablishFailed>(OnFailed);
        Receive<Evict>(_ => OnEvict());
    }

    protected override void PreStart()
    {
        if (_poolingStrategy.IdleTimeout > TimeSpan.Zero)
        {
            Timers.StartPeriodicTimer(EvictTimerKey, Evict.Instance,
                _poolingStrategy.IdleTimeout, _poolingStrategy.IdleTimeout);
        }
    }

    private void OnAcquire(Acquire msg)
    {
        if (msg.Tcs.Task.IsCompleted) return;

        var host = GetOrCreateHost(msg.Options);

        while (host.Idle.TryDequeue(out var idle))
        {
            if (idle.IsAlive() && !idle.IsExpired(_poolingStrategy.ConnectionLifetime))
            {
                if (msg.Tcs.TrySetResult(idle))
                {
                    return;
                }
            }
            else
            {
                host.Leases.Remove(idle);
                idle.Dispose();
            }
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
        var options = msg.Lease.Handle is not null
            ? _hosts.Keys.FirstOrDefault(k => _hosts[k].Leases.Contains(msg.Lease))
            : null;

        if (options is null || !_hosts.TryGetValue(options, out var host))
        {
            msg.Lease.Dispose();
            return;
        }

        if (!msg.CanReuse || !msg.Lease.IsAlive())
        {
            host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            ServeNextPending(host);
            return;
        }

        while (host.Pending.TryDequeue(out var pending))
        {
            if (!pending.Tcs.Task.IsCompleted)
            {
                if (pending.Tcs.TrySetResult(msg.Lease))
                {
                    return;
                }
            }
        }

        host.Idle.Enqueue(msg.Lease);
    }

    private void OnEstablished(Established msg)
    {
        var host = GetOrCreateHost(msg.Original.Options);
        host.Establishing--;
        host.Leases.Add(msg.Lease);

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

        if (host is not null)
        {
            ServeNextPending(host);
        }
    }

    private void OnEvict()
    {
        var now = DateTime.UtcNow;
        foreach (var host in _hosts.Values)
        {
            var toRemove = new List<ConnectionLease>();
            var newIdle = new Queue<ConnectionLease>();

            while (host.Idle.TryDequeue(out var lease))
            {
                if (!lease.IsAlive() || lease.IsExpired(_poolingStrategy.ConnectionLifetime))
                {
                    toRemove.Add(lease);
                }
                else
                {
                    newIdle.Enqueue(lease);
                }
            }

            while (newIdle.TryDequeue(out var kept))
            {
                host.Idle.Enqueue(kept);
            }

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
                pending.Tcs.TrySetException(new ObjectDisposedException(
                    nameof(TcpConnectionManagerActor)));
            }

            foreach (var lease in host.Leases)
            {
                lease.Dispose();
            }
        }

        _hosts.Clear();
    }

    private HostState GetOrCreateHost(TransportOptions options)
    {
        if (!_hosts.TryGetValue(options, out var state))
        {
            state = new HostState(options, _poolingStrategy.MaxConnectionsPerHost);
            _hosts[options] = state;
        }
        return state;
    }

    private void Establish(HostState host, Acquire msg)
    {
        host.Establishing++;
        _factory
            .EstablishAsync(msg.Options, msg.Token)
            .PipeTo(Self,
                success: lease => new Established(lease, msg),
                failure: ex => new EstablishFailed(ex, msg));
    }

    private void ServeNextPending(HostState host)
    {
        while (host.Pending.TryDequeue(out var next))
        {
            if (!next.Tcs.Task.IsCompleted)
            {
                if (host.Leases.Count + host.Establishing < host.MaxConnections)
                {
                    Establish(host, next);
                    return;
                }

                host.Pending.Enqueue(next);
                return;
            }
        }
    }
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/TcpConnectionManagerActor.cs
git commit -m "feat(transport/tcp): add TcpConnectionManagerActor with IPoolingStrategy"
```

---

### Task 11: Create TcpConnectionFactory, TcpTransportFactory, providers

**Files:**
- Create: `src/Servus.Akka/Transport/Tcp/TcpConnectionFactory.cs`
- Create: `src/Servus.Akka/Transport/Tcp/TcpTransportFactory.cs`
- Create: `src/Servus.Akka/Transport/Tcp/TcpClientProvider.cs`
- Create: `src/Servus.Akka/Transport/Tcp/TlsClientProvider.cs`
- Create: `src/Servus.Akka/Transport/Tcp/DnsCache.cs`

- [ ] **Step 1: Port TcpClientProvider, TlsClientProvider, DnsCache**

Copy from `src/Servus.Akka/IO/Tcp/` to `src/Servus.Akka/Transport/Tcp/`. Change namespace from `Servus.Akka.IO.Tcp` to `Servus.Akka.Transport.Tcp`. Replace any `ITransportOptions` references with `TransportOptions`, `TcpOptions` with `TcpTransportOptions`, `TlsOptions` with `TlsTransportOptions`. These files have minimal changes — they're socket-level plumbing.

- [ ] **Step 2: Implement TcpConnectionFactory**

Port from `src/Servus.Akka/IO/Tcp/TcpConnectionFactory.cs`:
- Implements `IConnectionFactory<ConnectionLease>`
- Uses new `ClientState`, `ConnectionHandle`, `ConnectionLease` from `Transport.Tcp`
- Launches `ClientByteMover` tasks
- No `RequestEndpoint` parameter

```csharp
namespace Servus.Akka.Transport.Tcp;

internal sealed class TcpConnectionFactory : IConnectionFactory<ConnectionLease>
{
    public static readonly TcpConnectionFactory Instance = new();

    public async Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct)
    {
        var tcpOptions = (TcpTransportOptions)options;
        IClientProvider provider = tcpOptions is TlsTransportOptions tlsOpts
            ? new TlsClientProvider(tlsOpts)
            : new TcpClientProvider(tcpOptions);

        var stream = await provider.GetStreamAsync(ct).ConfigureAwait(false);
        var state = new ClientState(stream, PipeMode.Bidirectional);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        var lease = new ConnectionLease(handle, state, cts);

        _ = ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);
        _ = ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        return lease;
    }
}
```

- [ ] **Step 3: Implement TcpTransportFactory**

```csharp
using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Servus.Akka.Transport.Tcp;

public sealed class TcpTransportFactory
{
    private readonly IActorRef _connectionManager;
    private readonly IPoolingStrategy _poolingStrategy;

    public TcpTransportFactory(IActorRef connectionManager, IPoolingStrategy poolingStrategy)
    {
        _connectionManager = connectionManager;
        _poolingStrategy = poolingStrategy;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Create()
    {
        return Flow.FromGraph(new TcpConnectionStage(_connectionManager, _poolingStrategy));
    }
}
```

- [ ] **Step 4: Verify build compiles**

Run: `dotnet build --configuration Release ./src/Servus.Akka/Servus.Akka.csproj`
Expected: Build succeeds (may need to fix IClientProvider interface references)

- [ ] **Step 5: Commit**

```bash
git add src/Servus.Akka/Transport/Tcp/
git commit -m "feat(transport/tcp): add TcpConnectionFactory, TcpTransportFactory, providers"
```

---

### Task 12: Integration test — TcpConnectionStage end-to-end

**Files:**
- Create: `src/TurboHTTP.StreamTests/Transport/Tcp/TcpConnectionStageSpec.cs`

- [ ] **Step 1: Write the integration test**

```csharp
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Transport.Tcp;

public sealed class TcpConnectionStageSpec : StreamTestBase
{
    private sealed class StubPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(30);
        public TimeSpan ConnectionLifetime => TimeSpan.FromMinutes(5);
        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Reuse;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }

    private sealed class StubConnectionManagerActor : ReceiveActor
    {
        public StubConnectionManagerActor(ConnectionLease? lease)
        {
            Receive<TcpConnectionManagerActor.Acquire>(msg =>
            {
                if (lease is not null)
                {
                    msg.Tcs.TrySetResult(lease);
                }
            });

            Receive<TcpConnectionManagerActor.Release>(_ => { });
        }

        public static Props Props(ConnectionLease? lease)
            => Akka.Actor.Props.Create(() => new StubConnectionManagerActor(lease));
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_push_TransportData_when_inbound_arrives()
    {
        var state = new ClientState(Stream.Null);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        var lease = new ConnectionLease(handle, state, cts);
        var manager = Sys.ActorOf(StubConnectionManagerActor.Props(lease));

        var stage = new TcpConnectionStage(manager, new StubPoolingStrategy());
        var flow = Flow.FromGraph(stage);

        var options = new TcpTransportOptions { Host = "localhost", Port = 8080 };
        var connectMsg = new ConnectTransport(options);

        var (pub, sub) = Source.Single<ITransportOutbound>(connectMsg)
            .Via(flow)
            .ToMaterialized(Sink.Seq<ITransportInbound>(), Keep.Both)
            .Run(Materializer);

        // Write inbound data to simulate network arrival
        var buf = TransportBuffer.Rent(16);
        buf.Length = 5;
        buf.FullMemory.Span[..5].Fill(0xAB);
        state.InboundWriter.TryWrite(buf);
        state.InboundWriter.TryComplete();

        var results = await sub;
        Assert.Contains(results, r => r is TransportData);
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet run --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -class "TurboHTTP.StreamTests.Transport.Tcp.TcpConnectionStageSpec"`
Expected: Test passes (may need adjustments based on actual materialization timing)

- [ ] **Step 3: Commit**

```bash
git add src/TurboHTTP.StreamTests/Transport/Tcp/TcpConnectionStageSpec.cs
git commit -m "test(transport/tcp): add TcpConnectionStage integration test"
```

---

### Task 13: Verify full build and test suite

- [ ] **Step 1: Build entire solution**

Run: `dotnet build --configuration Release ./src/TurboHTTP.slnx`
Expected: Build succeeds. If there are compile errors from the old IO/ code conflicting with new Transport/ code (duplicate type names), resolve by using explicit namespaces or adjusting imports. The old IO/ code stays untouched — this is a new parallel implementation.

- [ ] **Step 2: Run new transport tests**

Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Transport.Tcp"`
Expected: All transport tests pass

- [ ] **Step 3: Run existing tests to verify no regression**

Run: `dotnet test --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Run: `dotnet test --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`
Expected: All existing tests still pass — new code is additive, doesn't modify old IO/ types

- [ ] **Step 4: Commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix(transport/tcp): resolve build and test issues"
```
