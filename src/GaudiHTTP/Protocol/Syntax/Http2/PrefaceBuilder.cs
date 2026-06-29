using System.Buffers;

namespace GaudiHTTP.Protocol.Syntax.Http2;

internal static class PrefaceBuilder
{
    private const int DefaultInitialWindowSize = 65535;

    /// <summary>
    /// Builds the HTTP/2 client connection preface (magic + SETTINGS [+ optional connection
    /// WINDOW_UPDATE]).
    /// </summary>
    /// <param name="streamInitialWindowSize">
    /// Per-stream receive window advertised via SETTINGS_INITIAL_WINDOW_SIZE (RFC 9113 §6.5.2).
    /// This must match the credit the local FlowController grants each stream - advertising the
    /// connection window here lets the peer overrun a stream and trips a false FLOW_CONTROL_ERROR.
    /// </param>
    /// <param name="connectionInitialWindowSize">
    /// Desired connection-level receive window. SETTINGS cannot change the connection window, so any
    /// amount above the protocol default (65535) is granted via a stream-0 WINDOW_UPDATE (RFC 9113 §6.9).
    /// </param>
    public static (IMemoryOwner<byte> Owner, int Length) Build(
        int streamInitialWindowSize,
        int connectionInitialWindowSize,
        int headerTableSize,
        int maxFrameSize,
        int maxHeaderListSize)
    {
        const int frameHeaderSize = 9;
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        var settingsParams = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.HeaderTableSize, (uint)headerTableSize),
            (SettingsParameter.EnablePush, 0),
            (SettingsParameter.InitialWindowSize, (uint)streamInitialWindowSize),
            (SettingsParameter.MaxFrameSize, (uint)maxFrameSize),
            // RFC 9113 §6.5.2: advise the peer of the largest header list we will accept so it can
            // pre-trim oversized header blocks instead of having them rejected after the fact.
            (SettingsParameter.MaxHeaderListSize, (uint)maxHeaderListSize)
        };

        var settingsPayloadSize = settingsParams.Length * 6;
        var needsWindowUpdate = connectionInitialWindowSize > DefaultInitialWindowSize;
        const int windowUpdatePayloadSize = 4;
        var totalSize = magic.Length + frameHeaderSize + settingsPayloadSize;
        if (needsWindowUpdate)
        {
            totalSize += frameHeaderSize + windowUpdatePayloadSize;
        }

        var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var w = SpanWriter.Create(owner.Memory.Span);

        w.WriteBytes(magic);

        w.WriteUInt24BigEndian(settingsPayloadSize);
        w.WriteByte((byte)FrameType.Settings);
        w.WriteByte(0);
        w.WriteUInt32BigEndian(0);

        foreach (var (key, val) in settingsParams)
        {
            w.WriteUInt16BigEndian((ushort)key);
            w.WriteUInt32BigEndian(val);
        }

        if (!needsWindowUpdate) return (owner, totalSize);

        var windowUpdateIncrement = connectionInitialWindowSize - DefaultInitialWindowSize;
        w.WriteUInt24BigEndian(windowUpdatePayloadSize);
        w.WriteByte((byte)FrameType.WindowUpdate);
        w.WriteByte(0);
        w.WriteUInt32BigEndian(0);
        w.WriteUInt32BigEndian((uint)windowUpdateIncrement);

        return (owner, totalSize);
    }
}