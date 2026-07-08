using System.Buffers;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Owns the outbound wire-emission concerns shared by the HTTP/3 client and server session
/// managers: control-preface construction, DATA-frame framing, and the multiplexed body pump
/// together with its per-stream outbound byte budget. Client/server differences (throw-vs-null
/// preface handling, <c>CompleteWrites</c>/rate-observation on DATA emission) stay in the
/// session managers, which call into this writer for the shared mechanics.
/// </summary>
internal sealed class Http3OutboundWriter
{
    // Per-stream outbound byte budget for the multiplexed body pump. Each emitted DATA frame debits
    // the bytes it carries; the transport credits the bytes it actually flushed back per stream via
    // MultiplexedDataFlushed -> OnCapacityAvailable. Caps the in-flight (emitted-but-unflushed) body
    // bytes PER STREAM, so one slow reader parks only itself and the shared array pool stays bounded
    // under concurrent uploads.
    public const int OutboundBodyByteBudget = 256 * 1024;

    private readonly IMultiplexedBodyDrainTarget _target;
    private readonly CancellationTokenSource _connectionCts;
    private readonly int _bodyChunkSize;
    private MultiplexedBodyPump? _pump;

    public Http3OutboundWriter(
        IMultiplexedBodyDrainTarget target, CancellationTokenSource connectionCts, int bodyChunkSize)
    {
        _target = target;
        _connectionCts = connectionCts;
        _bodyChunkSize = bodyChunkSize;
    }

    /// <summary>
    /// Builds the control-stream preface (stream-type byte + SETTINGS frame). Stateless — callers
    /// track their own "already sent" flag and decide whether a repeat call should return null
    /// (client) or throw (server).
    /// </summary>
    public static MultiplexedData BuildControlPreface(
        int qpackMaxTableCapacity, int qpackBlockedStreams, int maxFieldSectionSize)
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, qpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, qpackBlockedStreams);
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, maxFieldSectionSize);
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        var buf = WireBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;

        return MultiplexedData.Rent(buf, CriticalStreamId.Control);
    }

    /// <summary>
    /// Encodes a DATA frame for <paramref name="body"/> and hands it to <paramref name="emit"/>.
    /// No-op for an empty body — callers decide separately whether to still emit
    /// <c>CompleteWrites</c> for the end-of-stream case.
    /// </summary>
    public static void EmitDataFrame(Action<ITransportOutbound> emit, long streamId, ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return;
        }

        var typeVarIntLen = QuicVarInt.EncodedLength((long)FrameType.Data);
        var payloadVarIntLen = QuicVarInt.EncodedLength(body.Length);
        var prefixSize = typeVarIntLen + payloadVarIntLen;
        var totalWireSize = prefixSize + body.Length;

        var buf = WireBuffer.Rent(totalWireSize);
        var span = buf.FullMemory.Span;

        QuicVarInt.Encode((long)FrameType.Data, span);
        span = span[typeVarIntLen..];
        QuicVarInt.Encode(body.Length, span);
        span = span[payloadVarIntLen..];
        body.Span.CopyTo(span);

        buf.Length = totalWireSize;
        emit(MultiplexedData.Rent(buf, streamId));
    }

    /// <summary>
    /// Lazily creates the shared multiplexed body pump (each stream bounded by
    /// <see cref="OutboundBodyByteBudget"/>) and registers a stream's body for draining.
    /// </summary>
    public void Register(long streamId, Stream bodyStream, CancellationToken cancellationToken)
    {
        _pump ??= new MultiplexedBodyPump(_target, _connectionCts, _bodyChunkSize, OutboundBodyByteBudget);
        _pump.Register(streamId, bodyStream, contentLength: null, cancellationToken);
    }

    public void HandleReadComplete(long streamId, int bytesRead)
    {
        _pump?.HandleReadComplete(streamId, bytesRead);
    }

    public void HandleReadFailed(long streamId, Exception reason)
    {
        _pump?.HandleReadFailed(streamId, reason);
    }

    public void OnCapacityAvailable(long streamId, int bytes)
    {
        _pump?.OnCapacityAvailable(streamId, bytes);
    }

    public void Cancel(long streamId)
    {
        _pump?.Cancel(streamId);
    }

    public void Cleanup()
    {
        _pump?.Cleanup();
    }
}
