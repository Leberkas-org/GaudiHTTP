namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthStreamedDecoder : IBodyDecoder
{
    private readonly long _expected;
    private readonly BodyHandle _handle;
    private long _received;

    public bool IsBuffered => false;
    public IReadOnlyList<(string Name, string Value)> Trailers => [];
    public bool IsComplete { get; private set; }

    public ContentLengthStreamedDecoder(long expected, long maxBodySize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        _expected = expected;
        _handle = new BodyHandle(maxBodySize);
        IsComplete = expected == 0;
        if (IsComplete)
        {
            _handle.Complete();
        }
    }

    public bool Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        if (IsComplete)
        {
            consumed = 0;
            return true;
        }

        var need = (int)Math.Min(int.MaxValue, _expected - _received);
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            _handle.Feed(data[..take]);
            _received += take;
        }

        consumed = take;
        IsComplete = _received == _expected;
        if (IsComplete)
        {
            _handle.Complete();
        }

        return IsComplete;
    }

    public bool OnEof()
    {
        if (!IsComplete)
        {
            _handle.Abort(new HttpProtocolException("Connection closed before content-length satisfied."));
        }

        return IsComplete;
    }

    public int Drain(ReadOnlySpan<byte> data)
    {
        if (IsComplete)
        {
            return 0;
        }

        var need = (int)Math.Min(int.MaxValue, _expected - _received);
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            _received += take;
        }

        IsComplete = _received == _expected;
        if (IsComplete)
        {
            _handle.Complete();
        }

        return take;
    }

    public Stream GetBodyStream() => _handle.AsStream();

    public void Dispose()
    {
        _handle.Dispose();
    }
}