using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Protocol.Syntax.Http3;

internal sealed class QpackStreamManager(
    IClientStageOperations ops,
    Client.Http3ClientEncoder requestEncoder,
    Client.Http3ClientDecoder responseDecoder,
    QpackTableSync tableSync)
{
    private bool _encoderPrefaceSent;
    private bool _decoderPrefaceSent;

    public QpackTableSync TableSync { get; } = tableSync;

    public static void OpenCriticalStreams(Action<ITransportOutbound> emit)
    {
        emit(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        emit(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        emit(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));
    }

    // RFC 9204 §2.2: a malformed QPACK encoder/decoder instruction is a connection error
    // (QPACK_ENCODER_STREAM_ERROR / QPACK_DECODER_STREAM_ERROR). The dynamic table is desynchronized,
    // so the connection cannot continue - let QpackException/HuffmanException propagate to the caller,
    // which tears the connection down instead of decoding subsequent header blocks against a corrupt table.
    public void ProcessEncoderInstructions(ReadOnlySpan<byte> data)
    {
        TableSync.ProcessEncoderInstructions(data);
    }

    public void ProcessDecoderInstructions(ReadOnlySpan<byte> data)
    {
        TableSync.ProcessDecoderInstructions(data);
    }

    public IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> ProcessEncoderInstructionsAndResolveBlocked(ReadOnlySpan<byte> data)
    {
        TableSync.ProcessEncoderInstructions(data);
        return TableSync.ResolveBlockedStreams();
    }

    public void FlushPendingInstructions()
    {
        FlushDecoderInstructions();
        FlushEncoderInstructions();
    }

    public void FlushEncoderInstructions()
    {
        var instructions = requestEncoder.EncoderInstructions;
        if (instructions.Length == 0)
        {
            return;
        }

        int totalLength;
        using var owner = MemoryPool<byte>.Shared.Rent(1 + instructions.Length);
        var span = owner.Memory.Span;

        if (!_encoderPrefaceSent)
        {
            _encoderPrefaceSent = true;
            span[0] = (byte)StreamType.QpackEncoder;
            instructions.Span.CopyTo(span[1..]);
            totalLength = 1 + instructions.Length;
        }
        else
        {
            instructions.Span.CopyTo(span);
            totalLength = instructions.Length;
        }

        var buf = TransportBuffer.Rent(totalLength);
        owner.Memory.Span[..totalLength].CopyTo(buf.FullMemory.Span);
        buf.Length = totalLength;

        ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.QpackEncoder));
    }

    public void FlushDecoderInstructions()
    {
        var sectionAck = responseDecoder.DecoderInstructions;

        var buf = TransportBuffer.Rent(1 + sectionAck.Length + 16);
        var dest = buf.FullMemory.Span;
        var offset = 0;

        if (!_decoderPrefaceSent)
        {
            dest[offset++] = (byte)StreamType.QpackDecoder;
        }

        if (sectionAck.Length > 0)
        {
            sectionAck.Span.CopyTo(dest[offset..]);
            offset += sectionAck.Length;
        }

        var icrWriter = SpanWriter.Create(dest[offset..]);
        TableSync.WriteInsertCountIncrement(ref icrWriter);
        offset += icrWriter.BytesWritten;

        if (offset == 0 || (offset == 1 && !_decoderPrefaceSent))
        {
            buf.Dispose();
            return;
        }

        _decoderPrefaceSent = true;
        buf.Length = offset;
        ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.QpackDecoder));
    }

    public void ApplyPeerSettings(Settings settings)
    {
        var peerQpackCapacity = settings.QpackMaxTableCapacity;
        if (peerQpackCapacity > 0)
        {
            TableSync.UpdateEncoderCapacity((int)peerQpackCapacity);
            FlushEncoderInstructions();
        }

        TableSync.RemoteMaxFieldSectionSize = settings.MaxFieldSectionSize;
    }

    public void Reset()
    {
        _encoderPrefaceSent = false;
        _decoderPrefaceSent = false;
    }
}
