namespace TurboHTTP.Protocol.Syntax.Http2;

// HTTP/2 Frame Types  —  RFC 9113 §6
//
// Frame-Header (9 Bytes, RFC 9113 §4.1):
//   +-----------------------------------------------+
//   |                 Length (24)                    |
//   +---------------+---------------+---------------+
//   |   Type (8)    |   Flags (8)   |
//   +-+-------------+---------------+-------------------------------+
//   |R|                 Stream Identifier (31)                      |
//   +=+=============================================================+
//   |                   Frame Payload (0...)                        |
//   +---------------------------------------------------------------+
internal enum FrameType : byte
{
    Data = 0x0,
    Headers = 0x1,
    Priority = 0x2,
    RstStream = 0x3,
    Settings = 0x4,
    PushPromise = 0x5,
    Ping = 0x6,
    GoAway = 0x7,
    WindowUpdate = 0x8,
    Continuation = 0x9,
}

[Flags]
internal enum DataFlags : byte
{
    None = 0x0,
    EndStream = 0x1,
    Padded = 0x8,
}

[Flags]
internal enum Headers : byte
{
    None = 0x0,
    EndStream = 0x1,
    EndHeaders = 0x4,
    Padded = 0x8,
    Priority = 0x20,
}

[Flags]
internal enum Settings : byte
{
    None = 0x0,
    Ack = 0x1,
}

[Flags]
internal enum PingFlags : byte
{
    None = 0x0,
    Ack = 0x1,
}

[Flags]
internal enum ContinuationFlags : byte
{
    None = 0x0,
    EndHeaders = 0x4,
}

internal enum SettingsParameter : ushort
{
    HeaderTableSize = 0x1,
    EnablePush = 0x2,
    MaxConcurrentStreams = 0x3,
    InitialWindowSize = 0x4,
    MaxFrameSize = 0x5,
    MaxHeaderListSize = 0x6,
}

internal enum Http2ErrorCode : uint
{
    NoError = 0x0,
    ProtocolError = 0x1,
    InternalError = 0x2,
    FlowControlError = 0x3,
    SettingsTimeout = 0x4,
    StreamClosed = 0x5,
    FrameSizeError = 0x6,
    RefusedStream = 0x7,
    Cancel = 0x8,
    CompressionError = 0x9,
    ConnectError = 0xa,
    EnhanceYourCalm = 0xb,
    InadequateSecurity = 0xc,
    Http11Required = 0xd,
}

internal abstract class Http2Frame(int streamId)
{
    public int StreamId { get; } = streamId >= 0
        ? streamId
        : throw new ArgumentOutOfRangeException(nameof(streamId), streamId, "Stream ID must be non-negative.");

    public abstract FrameType Type { get; }

    public abstract int SerializedSize { get; }

    public abstract void WriteTo(ref Span<byte> span);

    public byte[] Serialize()
    {
        var buf = new byte[SerializedSize];
        var span = buf.AsSpan();
        WriteTo(ref span);
        return buf;
    }

    protected static void WriteHeader(ref SpanWriter w, int payloadLength, FrameType type, byte flags, int streamId)
    {
        w.WriteUInt24BigEndian(payloadLength);
        w.WriteByte((byte)type);
        w.WriteByte(flags);
        w.WriteUInt32BigEndian((uint)streamId & 0x7FFFFFFFu);
    }

    protected const int FrameHeaderSize = 9;
}

internal sealed class DataFrame(int streamId, ReadOnlyMemory<byte> data, bool endStream = false)
    : Http2Frame(streamId)
{
    public override FrameType Type => FrameType.Data;
    public ReadOnlyMemory<byte> Data { get; } = data;
    public bool EndStream { get; } = endStream;

    public override int SerializedSize => FrameHeaderSize + Data.Length;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var flags = EndStream ? (byte)DataFlags.EndStream : (byte)DataFlags.None;
        WriteHeader(ref w, Data.Length, FrameType.Data, flags, StreamId);
        w.WriteBytes(Data.Span);
        span = span[w.BytesWritten..];
    }
}

internal sealed class HeadersFrame(
    int streamId,
    ReadOnlyMemory<byte> headerBlock,
    bool endStream = false,
    bool endHeaders = true)
    : Http2Frame(streamId)
{
    public override FrameType Type => FrameType.Headers;
    public ReadOnlyMemory<byte> HeaderBlockFragment { get; } = headerBlock;
    public bool EndStream { get; } = endStream;
    public bool EndHeaders { get; } = endHeaders;

    public override int SerializedSize => FrameHeaderSize + HeaderBlockFragment.Length;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var flags = Headers.None;
        if (EndStream)
        {
            flags |= Headers.EndStream;
        }

        if (EndHeaders)
        {
            flags |= Headers.EndHeaders;
        }

        WriteHeader(ref w, HeaderBlockFragment.Length, FrameType.Headers, (byte)flags, StreamId);
        w.WriteBytes(HeaderBlockFragment.Span);
        span = span[w.BytesWritten..];
    }
}

internal sealed class ContinuationFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endHeaders = true)
    : Http2Frame(streamId)
{
    public override FrameType Type => FrameType.Continuation;
    public ReadOnlyMemory<byte> HeaderBlockFragment { get; } = headerBlock;
    public bool EndHeaders { get; } = endHeaders;

    public override int SerializedSize => FrameHeaderSize + HeaderBlockFragment.Length;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var flags = EndHeaders ? (byte)ContinuationFlags.EndHeaders : (byte)0;
        WriteHeader(ref w, HeaderBlockFragment.Length, FrameType.Continuation, flags, StreamId);
        w.WriteBytes(HeaderBlockFragment.Span);
        span = span[w.BytesWritten..];
    }
}

internal sealed class RstStreamFrame(int streamId, Http2ErrorCode errorCode) : Http2Frame(streamId)
{
    public override FrameType Type => FrameType.RstStream;
    public Http2ErrorCode ErrorCode { get; } = errorCode;

    public override int SerializedSize => FrameHeaderSize + 4;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        WriteHeader(ref w, 4, FrameType.RstStream, 0, StreamId);
        w.WriteUInt32BigEndian((uint)ErrorCode);
        span = span[w.BytesWritten..];
    }
}

internal sealed class SettingsFrame(IReadOnlyList<(SettingsParameter Key, uint Value)> parameters, bool isAck = false)
    : Http2Frame(0)
{
    public override FrameType Type => FrameType.Settings;
    public IReadOnlyList<(SettingsParameter, uint)> Parameters { get; } = parameters;
    public bool IsAck { get; } = isAck;

    public override int SerializedSize => FrameHeaderSize + (IsAck ? 0 : Parameters.Count * 6);

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var payloadSize = IsAck ? 0 : Parameters.Count * 6;
        var flags = IsAck ? (byte)Settings.Ack : (byte)0;
        WriteHeader(ref w, payloadSize, FrameType.Settings, flags, 0);

        foreach (var (key, val) in Parameters)
        {
            w.WriteUInt16BigEndian((ushort)key);
            w.WriteUInt32BigEndian(val);
        }

        span = span[w.BytesWritten..];
    }

    public static byte[] SettingsAck()
    {
        var buf = new byte[FrameHeaderSize];
        var span = buf.AsSpan();
        var w = SpanWriter.Create(span);
        WriteHeader(ref w, 0, FrameType.Settings, (byte)Settings.Ack, 0);
        return buf;
    }
}

internal sealed class PingFrame : Http2Frame
{
    public override FrameType Type => FrameType.Ping;
    public ReadOnlyMemory<byte> Data { get; }
    public bool IsAck { get; }

    public PingFrame(ReadOnlyMemory<byte> data, bool isAck = false) : base(0)
    {
        if (data.Length != 8)
        {
            throw new ArgumentException("PING data must be exactly 8 bytes.", nameof(data));
        }

        Data = data;
        IsAck = isAck;
    }

    public override int SerializedSize => FrameHeaderSize + 8;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var flags = IsAck ? (byte)PingFlags.Ack : (byte)0;
        WriteHeader(ref w, 8, FrameType.Ping, flags, 0);
        w.WriteBytes(Data.Span);
        span = span[w.BytesWritten..];
    }
}

internal sealed class GoAwayFrame : Http2Frame
{
    public override FrameType Type => FrameType.GoAway;
    public int LastStreamId { get; }
    public Http2ErrorCode ErrorCode { get; }
    public ReadOnlyMemory<byte> DebugData { get; }

    public GoAwayFrame(int lastStreamId, Http2ErrorCode errorCode, ReadOnlyMemory<byte> debugData = default) : base(0)
    {
        if (lastStreamId < 0)
        {
            throw new HttpProtocolException("Invalid LastStreamId");
        }

        LastStreamId = lastStreamId;
        ErrorCode = errorCode;
        DebugData = debugData;
    }

    public override int SerializedSize => FrameHeaderSize + 8 + DebugData.Length;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var payloadSize = 8 + DebugData.Length;
        WriteHeader(ref w, payloadSize, FrameType.GoAway, 0, 0);
        w.WriteUInt32BigEndian((uint)LastStreamId & 0x7FFFFFFFu);
        w.WriteUInt32BigEndian((uint)ErrorCode);
        w.WriteBytes(DebugData.Span);
        span = span[w.BytesWritten..];
    }
}

internal sealed class WindowUpdateFrame : Http2Frame
{
    public override FrameType Type => FrameType.WindowUpdate;
    public int Increment { get; }

    public WindowUpdateFrame(int streamId, int increment) : base(streamId)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        Increment = increment;
    }

    public override int SerializedSize => FrameHeaderSize + 4;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        WriteHeader(ref w, 4, FrameType.WindowUpdate, 0, StreamId);
        w.WriteUInt32BigEndian((uint)Increment & 0x7FFFFFFFu);
        span = span[w.BytesWritten..];
    }
}

internal sealed class PushPromiseFrame(
    int streamId,
    int promisedStreamId,
    ReadOnlyMemory<byte> headerBlock,
    bool endHeaders = true)
    : Http2Frame(streamId)
{
    public override FrameType Type => FrameType.PushPromise;
    public int PromisedStreamId { get; } = promisedStreamId;
    private ReadOnlyMemory<byte> HeaderBlockFragment { get; } = headerBlock;
    public bool EndHeaders { get; } = endHeaders;

    public override int SerializedSize => FrameHeaderSize + 4 + HeaderBlockFragment.Length;

    public override void WriteTo(ref Span<byte> span)
    {
        var w = SpanWriter.Create(span);
        var payloadSize = 4 + HeaderBlockFragment.Length;
        var flags = EndHeaders ? (byte)Headers.EndHeaders : (byte)0;
        WriteHeader(ref w, payloadSize, FrameType.PushPromise, flags, StreamId);
        w.WriteUInt32BigEndian((uint)PromisedStreamId & 0x7FFFFFFFu);
        w.WriteBytes(HeaderBlockFragment.Span);
        span = span[w.BytesWritten..];
    }
}