namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class StreamingBodyDecoder(long maxBodySize = long.MaxValue) : IBodyDecoder
{
    private readonly BodyHandle _handle = new(maxBodySize);

    public bool IsBuffered => false;
    public bool IsComplete { get; private set; }

    public void Feed(ReadOnlySpan<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _handle.Feed(data);
        }

        if (!endStream) return;
        IsComplete = true;
        _handle.Complete();
    }

    public Stream GetBodyStream()
    {
        return _handle.AsStream();
    }

    public void Abort()
    {
        _handle.Abort(new OperationCanceledException());
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}