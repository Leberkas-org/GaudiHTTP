namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class ReconnectionManager(int maxAttempts, int maxBufferSize = int.MaxValue)
{
    private readonly List<HttpRequestMessage> _buffer = [];
    private int _attempts;

    public bool IsReconnecting { get; private set; }
    public int BufferedCount => _buffer.Count;

    public void OnConnectionLost(IReadOnlyList<HttpRequestMessage> replayableRequests)
    {
        IsReconnecting = true;
        _attempts = 1;
        _buffer.Clear();
        _buffer.AddRange(replayableRequests);
    }

    public IReadOnlyList<HttpRequestMessage> OnConnectionRestored()
    {
        IsReconnecting = false;
        _attempts = 0;
        var result = _buffer.ToList();
        _buffer.Clear();
        return result;
    }

    public bool OnReconnectAttemptFailed()
    {
        if (_attempts >= maxAttempts)
        {
            IsReconnecting = false;
            _attempts = 0;
            return false;
        }

        _attempts++;
        return true;
    }

    public bool Buffer(HttpRequestMessage request)
    {
        if (_buffer.Count >= maxBufferSize)
        {
            return false;
        }

        _buffer.Add(request);
        return true;
    }

    public void FailAllBuffered(Exception reason)
    {
        foreach (var request in _buffer)
        {
            request.Fail(reason);
        }

        _buffer.Clear();
    }

    public void Reset()
    {
        IsReconnecting = false;
        _buffer.Clear();
        _attempts = 0;
    }
}
