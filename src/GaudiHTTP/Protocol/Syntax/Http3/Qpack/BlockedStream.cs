namespace TurboHTTP.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// Represents a blocked stream waiting for dynamic table updates.
/// </summary>
internal sealed class BlockedStream(int streamId, int requiredInsertCount, ReadOnlyMemory<byte> data)
{
    /// <summary>The stream ID that is blocked.</summary>
    public int StreamId { get; } = streamId;

    /// <summary>The Required Insert Count that must be reached to unblock.</summary>
    public int RequiredInsertCount { get; } = requiredInsertCount;

    /// <summary>The raw header block data to decode once unblocked.</summary>
    public ReadOnlyMemory<byte> Data { get; } = data;
}
