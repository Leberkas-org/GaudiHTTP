namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Encapsulates all HTTP/3 connection-level state in a single class.
/// Manages GoAway, Settings, idle timeout, and push state.
/// </summary>
internal sealed class ConnectionState(TimeSpan idleTimeout, int maxPushCount = 0)
{
    public bool GoAwayReceived { get; set; }
    public long LastGoAwayStreamId { get; private set; } = -1;
    public bool RemoteSettingsReceived { get; private set; }
    public Settings? RemoteSettings { get; private set; }
    public long? RemoteMaxFieldSectionSize => RemoteSettings?.MaxFieldSectionSize;

    private long _lastActivity = Environment.TickCount64;

    public int ActiveStreamCount { get; private set; }
    public bool IsTimeoutDisabled => idleTimeout == TimeSpan.Zero;

    public void OnServerGoAway(GoAwayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var streamId = frame.StreamId;

        if (streamId % 4 != 0)
        {
            throw new HttpProtocolException(
                $"Server GOAWAY stream ID {streamId} is not a valid client-initiated bidirectional stream ID (must be divisible by 4, RFC 9114 §5.2).");
        }

        if (LastGoAwayStreamId >= 0 && streamId > LastGoAwayStreamId)
        {
            throw new HttpProtocolException(
                $"Server GOAWAY stream ID {streamId} must not increase beyond previous value {LastGoAwayStreamId} (RFC 9114 §5.2).");
        }

        LastGoAwayStreamId = streamId;
        GoAwayReceived = true;
    }

    public void OnRemoteSettings(SettingsFrame settingsFrame)
    {
        if (RemoteSettingsReceived)
        {
            throw new HttpProtocolException(
                "A second SETTINGS frame on the control stream is a connection error (RFC 9114 §7.2.4).");
        }

        var settings = new Settings();
        foreach (var (id, val) in settingsFrame.Parameters)
        {
            settings.Set(id, val);
        }

        RemoteSettings = settings;
        RemoteSettingsReceived = true;
    }

    public void RecordActivity()
    {
        _lastActivity = Environment.TickCount64;
    }

    public void OnStreamOpened()
    {
        ActiveStreamCount++;
        RecordActivity();
    }

    public void OnStreamClosed()
    {
        if (ActiveStreamCount > 0)
        {
            ActiveStreamCount--;
        }

        RecordActivity();
    }

    public bool IsIdleTimeoutExpired()
    {
        if (IsTimeoutDisabled)
        {
            return false;
        }

        return Environment.TickCount64 - _lastActivity >= (long)idleTimeout.TotalMilliseconds;
    }

    public TimeSpan TimeUntilExpiry()
    {
        if (IsTimeoutDisabled)
        {
            return TimeSpan.MaxValue;
        }

        var remainingMs = (long)idleTimeout.TotalMilliseconds - (Environment.TickCount64 - _lastActivity);
        return remainingMs > 0 ? TimeSpan.FromMilliseconds(remainingMs) : TimeSpan.Zero;
    }

    public void OnReceivedCancelPush(CancelPushFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
    }

    public void Reset()
    {
        GoAwayReceived = false;
        LastGoAwayStreamId = -1;
        RemoteSettingsReceived = false;
        RemoteSettings = null;
        ActiveStreamCount = 0;
        RecordActivity();
    }
}
