# Remove IoBuffer Indirection & NetworkBufferBatchStage

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the IoBuffer conversion layer and redundant NetworkBufferBatchStage — let System.IO.Pipelines handle batching, Channel<NetworkBuffer> handle async bridging.

**Architecture:** The current pipeline has double buffering: `Stream → Pipe → IoBuffer → Channel → NetworkBuffer.Wrap → Actor` (inbound) and the reverse outbound. Every chunk gets converted twice. The fix: change `Channel<IoBuffer>` to `Channel<NetworkBuffer>`, write `NetworkBuffer` directly from PipeReader segments, remove the `IoBuffer` type entirely. The `Pipe` already batches reads/writes at the byte level — `NetworkBufferBatchStage` in the Akka graph is redundant.

**Tech Stack:** C# (.NET 10), System.IO.Pipelines, System.Threading.Channels, Akka.Streams

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `Servus.Akka/IO/Messages.cs` | Delete `IoBuffer` struct, delete `DetachAsIoBuffer()`, keep `Wrap()` |
| Modify | `Servus.Akka/IO/ClientState.cs` | `Channel<IoBuffer>` → `Channel<NetworkBuffer>` |
| Modify | `Servus.Akka/IO/ConnectionHandle.cs` | Remove IoBuffer from generics, simplify `WriteAsync` |
| Modify | `Servus.Akka/IO/ClientByteMover.cs` | `DrainPipeToChannel` writes `NetworkBuffer`, `FillPipeFromChannel` reads `NetworkBuffer` |
| Modify | `Servus.Akka/IO/Tcp/TcpPumpManager.cs` | Read `NetworkBuffer` from channel directly |
| Modify | `Servus.Akka/IO/Quic/QuicPumpManager.cs` | Read `NetworkBuffer` from channel, set routing metadata |
| Modify | `TurboHTTP/Streams/Http10Engine.cs` | Remove `NetworkBufferBatchStage` |
| Modify | `TurboHTTP/Streams/Http11Engine.cs` | Remove `NetworkBufferBatchStage` |
| Modify | `TurboHTTP/Streams/Http20Engine.cs` | Remove `NetworkBufferBatchStage` |
| Modify | `TurboHTTP/Streams/Http30Engine.cs` | Remove `NetworkBufferBatchStage` |
| Delete | `TurboHTTP/Streams/Stages/Internal/NetworkBufferBatchStage.cs` | No longer needed |
| Modify | `TurboHTTP/Http1Options.cs` | Remove `MaxBatchWeight` |
| Modify | `TurboHTTP/Http2Options.cs` | Remove `MaxBatchWeight` |
| Modify | `TurboHTTP/Http3Options.cs` | Remove `MaxBatchWeight` |
| Modify | `Servus.Akka.Tests/IO/ClientByteMoverSpec.cs` | Update to use `NetworkBuffer` |
| Modify | `Servus.Akka.Tests/IO/ConnectionHandleSpec.cs` | Update channel types |
| Modify | `Servus.Akka.Tests/IO/Tcp/TcpPumpManagerSpec.cs` | Update channel types |
| Modify | `Servus.Akka.Tests/IO/Quic/QuicPumpManagerSpec.cs` | Update channel types |
| Delete | `TurboHTTP.StreamTests/Streams/Internal/NetworkBufferBatchStageSpec.cs` | Stage deleted |
| Modify | `TurboHTTP.StreamTests/Streams/ConnectionStageSpec.cs` | Update ClientState usage |
| Modify | `TurboHTTP.Tests/Http2/Connection/Http2StateMachineSpec.cs` | Remove `MaxBatchWeight` |
| Modify | `TurboHTTP.API.Tests/verify/CoreAPISpec.ApproveCore.DotNet.verified.txt` | Remove `MaxBatchWeight` from API surface |

---

### Task 1: Channel<IoBuffer> → Channel<NetworkBuffer> in ClientState

**Files:**
- Modify: `src/Servus.Akka/IO/ClientState.cs`

- [ ] **Step 1: Change channel types**

```csharp
// ClientState.cs — change lines 37-44
private readonly Channel<NetworkBuffer> _inboundChannel;
private readonly Channel<NetworkBuffer> _outboundChannel;

public ChannelReader<NetworkBuffer> InboundReader => _inboundChannel.Reader;
public ChannelWriter<NetworkBuffer> InboundWriter => _inboundChannel.Writer;

public ChannelReader<NetworkBuffer> OutboundReader => _outboundChannel.Reader;
public ChannelWriter<NetworkBuffer> OutboundWriter => _outboundChannel.Writer;
```

Constructor (line 52-53):
```csharp
_inboundChannel = Channel.CreateUnbounded<NetworkBuffer>(ChannelOptions);
_outboundChannel = Channel.CreateUnbounded<NetworkBuffer>(ChannelOptions);
```

Dispose (lines 61-62):
```csharp
while (_inboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }
while (_outboundChannel.Reader.TryRead(out var buf)) { buf.Dispose(); }
```

- [ ] **Step 2: Verify build errors cascade to expected files**

Run: `dotnet build src/Servus.Akka/Servus.Akka.csproj 2>&1 | grep error`

Expected: Errors in `ClientByteMover.cs`, `ConnectionHandle.cs`, and possibly pump managers (they reference `ChannelReader<IoBuffer>` types through `ConnectionHandle`).

---

### Task 2: ConnectionHandle — remove IoBuffer dependency

**Files:**
- Modify: `src/Servus.Akka/IO/ConnectionHandle.cs`

- [ ] **Step 1: Change generic parameters and simplify WriteAsync**

```csharp
public sealed record ConnectionHandle(
    ChannelWriter<NetworkBuffer> OutboundWriter,
    ChannelReader<NetworkBuffer> InboundReader,
    RequestEndpoint Key,
    IActorRef ConnectionActor)
{
    public int MaxConcurrentStreams { get; private set; } = 100;

    public void UpdateMaxConcurrentStreams(int value) => MaxConcurrentStreams = value;

    public TlsCloseKind CloseKind { get; private set; }

    public void SetCloseKind(TlsCloseKind value) => CloseKind = value;

    public ValueTask WriteAsync(NetworkBuffer buffer)
    {
        return OutboundWriter.WriteAsync(buffer);
    }

    public bool TryCompleteOutbound(Exception? error = null)
    {
        return OutboundWriter.TryComplete(error);
    }

    public static ConnectionHandle CreateDirect(
        ChannelWriter<NetworkBuffer> outboundWriter,
        ChannelReader<NetworkBuffer> inboundReader,
        RequestEndpoint key)
    {
        return new ConnectionHandle(outboundWriter, inboundReader, key, ActorRefs.Nobody);
    }

    public bool Equals(ConnectionHandle? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityContract == other.EqualityContract
            && EqualityComparer<ChannelWriter<NetworkBuffer>>.Default.Equals(OutboundWriter, other.OutboundWriter)
            && EqualityComparer<ChannelReader<NetworkBuffer>>.Default.Equals(InboundReader, other.InboundReader)
            && Key.Equals(other.Key)
            && EqualityComparer<IActorRef>.Default.Equals(ConnectionActor, other.ConnectionActor);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EqualityContract, OutboundWriter, InboundReader, Key, ConnectionActor);
    }
}
```

Key change: `WriteAsync` no longer calls `buffer.DetachAsIoBuffer()` — writes `NetworkBuffer` directly to the channel.

---

### Task 3: ClientByteMover — write NetworkBuffer to channel, read NetworkBuffer from channel

**Files:**
- Modify: `src/Servus.Akka/IO/ClientByteMover.cs`

- [ ] **Step 1: DrainPipeToChannel — write NetworkBuffer directly**

Change signature (line 53) and body:
```csharp
private static async Task DrainPipeToChannel(
    PipeReader reader, ChannelWriter<NetworkBuffer> channel, Action onClose, CancellationToken ct)
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
                var nb = NetworkBuffer.Rent(segment.Length);
                segment.Span.CopyTo(nb.FullMemory.Span);
                nb.Length = segment.Length;
                if (!channel.TryWrite(nb))
                {
                    nb.Dispose();
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
        try { reader.Complete(); } catch (InvalidOperationException) { }
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
```

Key change: `new IoBuffer(owner, segment.Length)` → `NetworkBuffer.Rent(segment.Length)` + copy + set `Length`. Uses the existing `NetworkBuffer` pool.

- [ ] **Step 2: FillPipeFromChannel — read NetworkBuffer directly**

Change signature (line 119-120) and body:
```csharp
private static async Task FillPipeFromChannel(
    ChannelReader<NetworkBuffer> channel, PipeWriter writer, CancellationToken ct)
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
    catch (OperationCanceledException) { }
    catch (Exception) { }
    finally
    {
        try { writer.Complete(); } catch (InvalidOperationException) { }
    }
}
```

This is structurally identical but reads `NetworkBuffer` instead of `IoBuffer`. The `buf.Span` and `buf.Length` properties already exist on `NetworkBuffer`.

- [ ] **Step 3: Build Servus.Akka**

Run: `dotnet build src/Servus.Akka/Servus.Akka.csproj`

Expected: Success (ClientState, ConnectionHandle, ClientByteMover all consistent now).

---

### Task 4: TcpPumpManager — read NetworkBuffer directly

**Files:**
- Modify: `src/Servus.Akka/IO/Tcp/TcpPumpManager.cs`

- [ ] **Step 1: Change PumpAsync to read NetworkBuffer**

Change `ChannelReader<IoBuffer>` to `ChannelReader<NetworkBuffer>` on line 37. Remove the `NetworkBuffer.Wrap()` call (line 62) — the chunk IS already a NetworkBuffer:

```csharp
private static async Task PumpAsync(
    ChannelReader<NetworkBuffer> reader,
    RequestEndpoint key,
    int gen,
    CancellationToken ct,
    IActorRef self)
{
    IInputItem[]? batch = null;
    var count = 0;
    var closeKind = TlsCloseKind.GracefulClose;

    try
    {
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out var chunk))
            {
                if (ct.IsCancellationRequested)
                {
                    chunk.Dispose();
                    if (batch is not null)
                    {
                        DisposeAndReturnBatch(ref batch, count);
                    }
                    return;
                }

                chunk.Key = key;
                batch ??= ArrayPool<IInputItem>.Shared.Rent(32);

                if (count == batch.Length)
                {
                    self.Tell(new InboundBatch(batch, count, gen));
                    batch = ArrayPool<IInputItem>.Shared.Rent(count * 2);
                    count = 0;
                }

                batch[count++] = chunk;
            }

            if (count > 0)
            {
                self.Tell(new InboundBatch(batch!, count, gen));
                batch = null;
                count = 0;
            }
        }
    }
    // ... catch blocks unchanged
```

Key change: `reader.TryRead(out var chunk)` now gives a `NetworkBuffer` directly — no `Wrap()` needed. Just set `chunk.Key = key` and use it.

---

### Task 5: QuicPumpManager — read NetworkBuffer directly

**Files:**
- Modify: `src/Servus.Akka/IO/Quic/QuicPumpManager.cs`

- [ ] **Step 1: Change PumpAsync to read NetworkBuffer**

Change `ChannelReader<IoBuffer>` to `ChannelReader<NetworkBuffer>` on line 68. Remove `RoutedNetworkBuffer.Wrap()`:

```csharp
private static async Task PumpAsync(
    ChannelReader<NetworkBuffer> reader,
    RequestEndpoint key,
    long streamTypeValue,
    CancellationToken ct,
    IActorRef self,
    int gen,
    long streamId)
{
    var closeKind = QuicCloseKind.RequestStreamComplete;
    try
    {
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out var chunk))
            {
                RoutedNetworkBuffer nb;
                if (chunk is RoutedNetworkBuffer routed)
                {
                    nb = routed;
                }
                else
                {
                    nb = RoutedNetworkBuffer.WrapExisting(chunk);
                }

                nb.Key = key;
                nb.StreamTypeValue = streamTypeValue;
                nb.StreamId = streamId;

                self.Tell(new InboundData(nb, gen));
            }
        }
    }
    // ... catch blocks unchanged
```

Note: QUIC pump needs `RoutedNetworkBuffer` for stream tagging. Since `DrainPipeToChannel` writes plain `NetworkBuffer`, we need `RoutedNetworkBuffer.WrapExisting()` — a method that wraps an existing `NetworkBuffer` as `RoutedNetworkBuffer` without copying the memory. Check if this exists; if not, create it in Messages.cs. Alternatively, have the QUIC `DrainPipeToChannel` variant write `RoutedNetworkBuffer` directly.

**Decision:** The simplest approach is to add a `RoutedNetworkBuffer.WrapExisting(NetworkBuffer)` static method that transfers ownership without copying. If `RoutedNetworkBuffer` already has `Wrap(IMemoryOwner, int)`, model `WrapExisting` similarly but taking the owner from the NetworkBuffer.

- [ ] **Step 2: Add RoutedNetworkBuffer.WrapExisting if missing**

In `Messages.cs`, add to `RoutedNetworkBuffer`:
```csharp
public static RoutedNetworkBuffer WrapExisting(NetworkBuffer source)
{
    var owner = source.DetachOwner();
    if (!RoutedPool.TryPop(out var buf))
    {
        return new RoutedNetworkBuffer { Owner = owner, Length = source.Length };
    }

    buf.Owner = owner;
    buf.Length = source.Length;
    buf.Key = source.Key;
    buf.StreamTypeValue = null;
    buf.StreamId = null;
    return buf;
}
```

And add `DetachOwner()` to `NetworkBuffer`:
```csharp
public IMemoryOwner<byte>? DetachOwner()
{
    var owner = Interlocked.Exchange(ref Owner, null);
    Length = 0;
    if (MaxPoolSize > 0 && WrapperPool.Count <= MaxPoolSize)
    {
        WrapperPool.Push(this);
    }
    return owner;
}
```

---

### Task 6: Delete IoBuffer and DetachAsIoBuffer

**Files:**
- Modify: `src/Servus.Akka/IO/Messages.cs`

- [ ] **Step 1: Delete IoBuffer struct (lines 66-77)**

Remove entirely:
```csharp
// DELETE THIS:
public readonly record struct IoBuffer(IMemoryOwner<byte> Owner, int Length) : IDisposable
{
    public ReadOnlyMemory<byte> Memory => Owner.Memory[..Length];
    public ReadOnlySpan<byte> Span => Owner.Memory.Span[..Length];
    public void Dispose() => Owner.Dispose();

    public static IoBuffer Rent(int dataLength)
    {
        var owner = MemoryPool<byte>.Shared.Rent(dataLength);
        return new IoBuffer(owner, dataLength);
    }
}
```

- [ ] **Step 2: Delete DetachAsIoBuffer method (lines 131-142)**

Remove entirely:
```csharp
// DELETE THIS:
public IoBuffer DetachAsIoBuffer()
{
    var owner = Interlocked.Exchange(ref Owner, null)!;
    var len = Length;
    Length = 0;
    if (MaxPoolSize > 0 && WrapperPool.Count <= MaxPoolSize)
    {
        WrapperPool.Push(this);
    }
    return new IoBuffer(owner, len);
}
```

- [ ] **Step 3: Build full solution**

Run: `dotnet build src/TurboHTTP.slnx`

Expected: Errors only in test files and engine files (addressed in Tasks 7-9).

---

### Task 7: Remove NetworkBufferBatchStage from all engines

**Files:**
- Modify: `src/TurboHTTP/Streams/Http10Engine.cs`
- Modify: `src/TurboHTTP/Streams/Http11Engine.cs`
- Modify: `src/TurboHTTP/Streams/Http20Engine.cs`
- Modify: `src/TurboHTTP/Streams/Http30Engine.cs`
- Delete: `src/TurboHTTP/Streams/Stages/Internal/NetworkBufferBatchStage.cs`

- [ ] **Step 1: Simplify Http11Engine (model for all others)**

```csharp
internal class Http11Engine : IHttpProtocolEngine
{
    private readonly TurboClientOptions _options;

    public Http11Engine(TurboClientOptions options)
    {
        _options = options;
    }

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http11ConnectionStage(_options));

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                connection.InApp,
                connection.OutNetwork,
                connection.InServer,
                connection.OutResponse);
        }));
    }
}
```

Remove the `using TurboHTTP.Streams.Stages.Internal;` import and all `NetworkBufferBatchStage` references.

- [ ] **Step 2: Apply same pattern to Http10Engine, Http20Engine, Http30Engine**

All four engines get the same simplification — just `ConnectionStage` directly wired, no batch flow. Each engine file removes the `NetworkBufferBatchStage` variable and the `b.From(...).Via(batchFlow)` line.

- [ ] **Step 3: Delete NetworkBufferBatchStage.cs**

Delete: `src/TurboHTTP/Streams/Stages/Internal/NetworkBufferBatchStage.cs`

- [ ] **Step 4: Remove MaxBatchWeight from options**

In `Http1Options.cs`, `Http2Options.cs`, `Http3Options.cs` — delete the `MaxBatchWeight` property and its XML doc.

---

### Task 8: Update tests

**Files:**
- Modify: `src/Servus.Akka.Tests/IO/ClientByteMoverSpec.cs`
- Modify: `src/Servus.Akka.Tests/IO/ConnectionHandleSpec.cs`
- Modify: `src/Servus.Akka.Tests/IO/Tcp/TcpPumpManagerSpec.cs`
- Modify: `src/Servus.Akka.Tests/IO/Quic/QuicPumpManagerSpec.cs`
- Delete: `src/TurboHTTP.StreamTests/Streams/Internal/NetworkBufferBatchStageSpec.cs`
- Modify: `src/TurboHTTP.StreamTests/Streams/ConnectionStageSpec.cs`
- Modify: `src/TurboHTTP.Tests/Http2/Connection/Http2StateMachineSpec.cs`

- [ ] **Step 1: ClientByteMoverSpec — write NetworkBuffer instead of IoBuffer**

Change all `new IoBuffer(owner, size)` to `NetworkBuffer.Wrap(owner, size)`. Change channel types from `Channel<IoBuffer>` to `Channel<NetworkBuffer>`.

- [ ] **Step 2: ConnectionHandleSpec — update channel types**

Change all `Channel.CreateUnbounded<IoBuffer>()` to `Channel.CreateUnbounded<NetworkBuffer>()`. Update constructor calls.

- [ ] **Step 3: TcpPumpManagerSpec — update channel types**

Change `Channel<IoBuffer>` to `Channel<NetworkBuffer>`. Write `NetworkBuffer.Wrap(owner, length)` instead of `new IoBuffer(owner, length)`.

- [ ] **Step 4: QuicPumpManagerSpec — update channel types**

Same pattern as TcpPumpManagerSpec.

- [ ] **Step 5: Delete NetworkBufferBatchStageSpec.cs**

Delete: `src/TurboHTTP.StreamTests/Streams/Internal/NetworkBufferBatchStageSpec.cs`

- [ ] **Step 6: ConnectionStageSpec — update ClientState usage**

Update any direct channel type references.

- [ ] **Step 7: Http2StateMachineSpec — remove MaxBatchWeight**

Remove `MaxBatchWeight = 262_144` from test options setup (line 86).

- [ ] **Step 8: Update API approval test**

Remove `MaxBatchWeight` entries from `src/TurboHTTP.API.Tests/verify/CoreAPISpec.ApproveCore.DotNet.verified.txt`.

---

### Task 9: Build, test, verify

- [ ] **Step 1: Build full solution**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`

Expected: Zero errors, zero warnings related to IoBuffer.

- [ ] **Step 2: Run unit tests**

Run: `dotnet test --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`

Expected: All pass.

- [ ] **Step 3: Run stream tests**

Run: `dotnet test --project src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj`

Expected: All pass.

- [ ] **Step 4: Run Servus.Akka tests**

Run: `dotnet test --project src/Servus.Akka.Tests/Servus.Akka.Tests.csproj`

Expected: All pass.

- [ ] **Step 5: Run API approval test**

Run: `dotnet test --project src/TurboHTTP.API.Tests/TurboHTTP.API.Tests.csproj`

Expected: Pass (after updating verified.txt).

- [ ] **Step 6: Verify with Roslyn Navigator**

Run diagnostics on all modified files to confirm no compile-time issues.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "perf: remove IoBuffer indirection and NetworkBufferBatchStage

Let System.IO.Pipelines handle batching, Channel<NetworkBuffer> handle
async bridging. Eliminates double conversion (NetworkBuffer → IoBuffer →
NetworkBuffer) on every IO operation in the hot path."
```
