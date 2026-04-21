# Protocol-Agnostic QUIC Transport Layer

## Goal

Remove all HTTP/3 protocol knowledge from the QUIC transport layer. Replace `QuicStreamKind` enum with opaque `long streamTypeValue` flowing through transport. The transport distinguishes only bidirectional (request) vs unidirectional (typed) streams. Protocol interpretation happens exclusively in `Http30ConnectionStage`.

## Core Concepts

- **Request streams**: bidirectional, identified by `streamTypeValue = -1` (sentinel)
- **Typed streams**: unidirectional, identified by their wire byte value (opaque `long`)
- **TypedStreamDescriptor**: configuration record passed to transport at construction — `(long StreamTypeValue, long SyntheticStreamId)`
- Transport opens typed streams eagerly from configuration, without knowing what the values mean

## New Transport Type

```csharp
internal readonly record struct TypedStreamDescriptor(long StreamTypeValue, long SyntheticStreamId);
```

Http3 layer provides at construction:
```csharp
[new(0x00, -2), new(0x02, -3)]  // Control, QpackEncoder — transport doesn't know names
```

## Typed Stream State

Replaces the six hardcoded fields (`_controlHandle`, `_encoderHandle`, `_pendingControlItems`, `_pendingEncoderItems`, `_controlStreamId`, `_encoderStreamId`):

```csharp
private sealed class TypedStreamState
{
    public ConnectionHandle? Handle;
    public readonly Queue<NetworkBuffer> PendingItems = new();
    public long StreamId;
}
```

Stored in `Dictionary<long, TypedStreamState> _typedStreams` keyed by `streamTypeValue`.

## File-by-File Changes

### Delete

- `QuicStreamKind.cs` — enum and `QuicStreamKindMapper` removed entirely

### QuicConnectionHandle

- `OpenStreamAsLeaseAsync(bool bidirectional)` — no stream type knowledge, just direction
- `InboundStream(ConnectionLease, long StreamTypeValue, long StreamId)` — raw wire value
- `AcceptInboundStreamAsLeaseAsync` — reads wire byte, returns as `long`, no interpretation, accepts all streams
- Remove `MapStreamKind` — replace with `bidirectional ? Bidirectional+GetStream : WriteOnly+GetUnidirectional`

### IQuicTransportEvent

- `TypedLeaseAcquired(ConnectionLease, long StreamTypeValue, long StreamId)`
- `InboundStreamReady` carries `InboundStream` which now has `long StreamTypeValue`

### QuicPumpManager

- `StartInboundPump(handle, long streamTypeValue, key, gen, streamId)`
- `PumpAsync`: sets `h3Buf.StreamTypeValue = streamTypeValue` instead of `ApplyToBuffer`
- Close signal: only for request streams (`streamTypeValue < 0`)

### QuicStreamRouter

- `RouteTaggedItem(buffer, long streamTypeValue, Dictionary<long, TypedStreamState> typedStreams)` — looks up by value, falls through to request routing for unknown/request type
- Remove `QuicStreamKind` from all method signatures

### QuicTransportStateMachine

- Constructor receives `TypedStreamDescriptor[]`, initializes `_typedStreams` dictionary
- Remove constants `ControlStreamSyntheticId`, `QpackEncoderStreamSyntheticId`, `QpackDecoderStreamSyntheticId`
- `OnRequestLeaseAcquired` — iterates descriptors to open typed streams
- `OnTypedLeaseAcquired(lease, long streamTypeValue, long streamId)` — looks up in `_typedStreams`
- `OnInboundStreamReady` — maps `streamTypeValue` to synthetic ID via descriptors (or real stream ID for unconfigured types)
- `HandlePush` — reads `StreamTypeValue` from buffer for routing instead of `Http3StreamType`

### Http3NetworkBuffer (Internal/Messages.cs)

- Add `public long StreamTypeValue { get; set; } = -1;`
- `Http3StreamType StreamType` stays as plain settable property (no auto-conversion)
- Transport only touches `StreamTypeValue`; protocol layer uses both

### Http30ConnectionStage (Protocol Layer)

- **Inbound**: maps `StreamTypeValue` to `Http3StreamType` in `HandleTaggedStreamData`
- **Outbound**: sets `StreamTypeValue` on buffers (0x00 for Control, 0x02 for Encoder, 0x03 for Decoder)
- This is the single place where wire values get protocol meaning

### QpackStreamHandler

- Sets `StreamTypeValue` on outbound buffers (instead of / alongside `StreamType`)

## What Stays the Same

- `Http3StreamType` enum stays (protocol-internal concern)
- `Http3NetworkBuffer.StreamType` stays (used by protocol layer)
- Synthetic stream IDs stay (configured instead of hardcoded)
- All buffering/flush logic stays (same patterns, keyed by `long` instead of enum)

## Test Impact

- Transport specs (`QuicPumpManagerSpec`, `QuicStreamRouterSpec`, `QuicStreamRouterEnhancedSpec`, `QuicTransportStateMachineSpec`, `QuicTransportStateMachineLifecycleSpec`, `QuicConnectionHandleSpec`, `QuicConnectionManagerSpec`) — update to use `long` values instead of `QuicStreamKind`
- Protocol specs using `Http3StreamType` — unchanged
