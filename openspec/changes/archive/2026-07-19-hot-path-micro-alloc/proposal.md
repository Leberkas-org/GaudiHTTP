## Why

Four independent micro-allocation sources on hot paths, identified from the Artery comparison and alloc-trace data. Individually small, cumulatively measurable — especially for Fortunes (high-RPS, small bodies) and H1.1/H2 uploads (many chunks). All are mechanical optimizations with no behavioral change, low risk.

## What Changes

- **stackalloc for small fixed-size buffers**: H2 9-byte frame headers, H3 varints, HPACK/QPACK integer encoding — currently zero `stackalloc` in the entire project. All under 64 bytes, synchronous on the actor thread, hot path.
- **Int-keyed timer map**: Eliminate 3x `string.Concat` per H2/H3 stream open (`"body-consumption:" + streamId.ToString()`). Switch timer keys to `(TimerKind, StreamId)` struct or int-keyed map.
- **SerialBodyPump buffer reuse**: Replace fresh `WireBuffer.Rent(chunkSize)` per body read with PumpSlot-style `EnsureBuffer` (rent once, reuse). FlowControlledBodyPump already has this pattern.
- **Server body-read ValueTask**: Replace `Task.FromResult(chunk)` in `GaudiHttpResponseBodyFeature` with `ValueTask<T>` — eliminates one Task object allocation per response body chunk.

## Capabilities

### New Capabilities
- `stackalloc-small-buffers`: stackalloc for H2/H3 frame header encoding and varint writing in encoder/decoder hot paths
- `timer-key-optimization`: Int-based timer keys instead of string concat in H2/H3 StreamState
- `serial-pump-buffer-reuse`: Buffer reuse in SerialBodyPump (H1.0/H1.1) following the FlowControlledBodyPump pattern
- `server-body-valuetask`: ValueTask instead of Task.FromResult for synchronous server body reads

### Modified Capabilities

## Impact

- `src/GaudiHTTP/Protocol/Syntax/Http2/Http2ClientEncoder.cs` — stackalloc for frame headers
- `src/GaudiHTTP/Protocol/Syntax/Http2/Http2ServerEncoder.cs` — stackalloc for frame headers
- `src/GaudiHTTP/Protocol/Syntax/Http3/Http3ClientEncoder.cs` — stackalloc for varints
- `src/GaudiHTTP/Protocol/Syntax/Http3/Http3ServerEncoder.cs` — stackalloc for varints
- `src/GaudiHTTP/Protocol/Syntax/Http2/Hpack/HpackEncoder.cs` — stackalloc for integer encoding
- `src/GaudiHTTP/Protocol/Syntax/Http3/Qpack/QpackEncoder.cs` — stackalloc for integer encoding
- `src/GaudiHTTP/Protocol/Syntax/Http2/StreamState.cs` — timer key conversion
- `src/GaudiHTTP/Protocol/Syntax/Http3/StreamState.cs` — timer key conversion
- `src/GaudiHTTP/Streams/Stages/Client/HttpClientConnectionStageLogic.cs` — timer map adaptation
- `src/GaudiHTTP/Streams/Stages/Server/HttpServerConnectionStageLogic.cs` — timer map adaptation
- `src/GaudiHTTP/Protocol/Body/SerialBodyPump.cs` — EnsureBuffer pattern
- `src/GaudiHTTP/Server/Features/GaudiHttpResponseBodyFeature.cs` — ValueTask return
- All changes internal, no public API impact
