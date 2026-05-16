namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class ReconnectionManager
{
    private readonly int _maxAttempts;
    private readonly List<HttpRequestMessage> _buffer = [];
    private int _attempts;

    public ReconnectionManager(int maxAttempts)
    {
        _maxAttempts = maxAttempts;
    }

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
        if (_attempts >= _maxAttempts)
        {
            IsReconnecting = false;
            _attempts = 0;
            return false;
        }

        _attempts++;
        return true;
    }

    public void Buffer(HttpRequestMessage request)
    {
        _buffer.Add(request);
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
