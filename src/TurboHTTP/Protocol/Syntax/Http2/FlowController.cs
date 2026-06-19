using TurboHTTP.Protocol.Multiplexed;

namespace TurboHTTP.Protocol.Syntax.Http2;

internal sealed class FlowController : IFlowController<int>
{
    private readonly Dictionary<int, int> _recvStreamWindows = new();
    private int _pendingConnIncrement;
    private readonly Dictionary<int, int> _pendingStreamIncrements = new();
    private int _windowUpdateThreshold;

    private int _initialRecvStreamWindow;
    private readonly WindowScaler? _scaler;
    private readonly TimeProvider? _clock;
    private readonly RttEstimator? _rtt;
    private readonly Dictionary<int, long> _deliveredSinceSample = new();
    private readonly Dictionary<int, long> _lastSampleTimestamp = new();

    private static readonly TimeSpan MeasurementPingInterval = TimeSpan.FromMilliseconds(100);

    public int RecvConnectionWindow { get; private set; }

    /// <summary>Current per-stream receive window size (grows under adaptive scaling).</summary>
    public int CurrentStreamWindow => _initialRecvStreamWindow;

    /// <summary>Latest measured min-RTT. Zero = unknown / scaling disabled.</summary>
    public TimeSpan MinRtt => _rtt?.MinRtt ?? TimeSpan.Zero;

    private long _connectionSendWindow;
    private long _initialSendStreamWindow;
    private readonly Dictionary<int, long> _streamSendWindows = new();

    public FlowController(
        int connectionWindowSize,
        int streamWindowSize,
        WindowScaler? scaler = null,
        TimeProvider? clock = null)
    {
        RecvConnectionWindow = connectionWindowSize;
        _initialRecvStreamWindow = streamWindowSize;
        _connectionSendWindow = 65535;
        _initialSendStreamWindow = 65535;
        _scaler = scaler;
        _clock = clock;

        if (_scaler is not null && _clock is not null)
        {
            _rtt = new RttEstimator(_clock, MeasurementPingInterval);
        }

        const int minWindowUpdateThreshold = 8 * 1024;
        _windowUpdateThreshold = Math.Max(minWindowUpdateThreshold, streamWindowSize / 4);
    }

    public long ConnectionSendWindow => _connectionSendWindow;

    public bool GoAwayReceived { get; private set; }

    /// <summary>True when a measurement PING is due (scaling on, window below cap, estimator ready).</summary>
    public bool ShouldSendMeasurementPing() =>
        _rtt is not null && _scaler is not null
                         && _initialRecvStreamWindow < _scaler.MaxWindow
                         && _rtt.ShouldSendPing();

    public void OnMeasurementPingSent() => _rtt?.OnPingSent();

    public void OnMeasurementPingAck() => _rtt?.OnPingAck();

    public long GetSendWindow(int streamId)
    {
        var streamWindow = _streamSendWindows.GetValueOrDefault(streamId, _initialSendStreamWindow);
        return Math.Max(0L, Math.Min(_connectionSendWindow, streamWindow));
    }

    public long GetStreamSendWindow(int streamId)
        => _streamSendWindows.GetValueOrDefault(streamId, _initialSendStreamWindow);

    public void OnDataSent(int streamId, int length)
    {
        _connectionSendWindow -= length;
        _streamSendWindows.TryAdd(streamId, _initialSendStreamWindow);
        _streamSendWindows[streamId] -= length;
    }

    public void OnSendWindowUpdate(int streamId, int increment)
    {
        const long maxWindow = int.MaxValue;

        if (streamId == 0)
        {
            var updated = _connectionSendWindow + increment;
            if (updated > maxWindow)
            {
                throw new HttpProtocolException(
                    "RFC 9113 §6.9.1: WINDOW_UPDATE would exceed maximum flow-control window size.");
            }

            _connectionSendWindow = updated;
        }
        else
        {
            var current = _streamSendWindows.GetValueOrDefault(streamId, _initialSendStreamWindow);
            var updated = current + increment;
            if (updated > maxWindow)
            {
                throw new HttpProtocolException(
                    "RFC 9113 §6.9.1: WINDOW_UPDATE would exceed maximum flow-control window size.");
            }

            _streamSendWindows[streamId] = updated;
        }
    }

    public FlowControlResult<int> OnInboundData(int streamId, int dataLength)
    {
        RecvConnectionWindow -= dataLength;

        _recvStreamWindows.TryAdd(streamId, _initialRecvStreamWindow);
        _recvStreamWindows[streamId] -= dataLength;

        if (RecvConnectionWindow < 0)
        {
            return new FlowControlResult<int> { Success = false, IsConnectionViolation = true };
        }

        if (_recvStreamWindows[streamId] < 0)
        {
            return new FlowControlResult<int>
            {
                Success = false,
                IsStreamViolation = true,
                ViolationStreamId = streamId
            };
        }

        WindowUpdateSignal<int>? connUpdate = null;
        WindowUpdateSignal<int>? streamUpdate = null;

        if (dataLength > 0)
        {
            _pendingConnIncrement += dataLength;
            _pendingStreamIncrements.TryAdd(streamId, 0);
            _pendingStreamIncrements[streamId] += dataLength;

            if (_scaler is not null)
            {
                _deliveredSinceSample.TryAdd(streamId, 0);
                _deliveredSinceSample[streamId] += dataLength;
            }

            if (_pendingConnIncrement >= _windowUpdateThreshold)
            {
                var increment = _pendingConnIncrement;
                RecvConnectionWindow += increment;
                connUpdate = new WindowUpdateSignal<int>(0, increment);
                _pendingConnIncrement = 0;
            }

            if (_pendingStreamIncrements[streamId] >= _windowUpdateThreshold)
            {
                var increment = _pendingStreamIncrements[streamId];

                if (_scaler is not null && _clock is not null && MinRtt > TimeSpan.Zero)
                {
                    var nowTicks = _clock.GetTimestamp();
                    if (_lastSampleTimestamp.TryGetValue(streamId, out var lastTicks))
                    {
                        var elapsed = _clock.GetElapsedTime(lastTicks, nowTicks);
                        var delivered = _deliveredSinceSample.GetValueOrDefault(streamId, 0);
                        var newWindow = _scaler.ComputeNewWindow(_initialRecvStreamWindow, delivered, elapsed, MinRtt);
                        if (newWindow > _initialRecvStreamWindow)
                        {
                            increment += newWindow - _initialRecvStreamWindow;
                            _initialRecvStreamWindow = newWindow;
                            _windowUpdateThreshold = Math.Max(8 * 1024, newWindow / 4);
                        }
                    }

                    _lastSampleTimestamp[streamId] = nowTicks;
                    _deliveredSinceSample[streamId] = 0;
                }

                _recvStreamWindows[streamId] += increment;
                streamUpdate = new WindowUpdateSignal<int>(streamId, increment);
                _pendingStreamIncrements[streamId] = 0;
            }
        }

        return new FlowControlResult<int>
        {
            Success = true,
            ConnectionWindowUpdate = connUpdate,
            StreamWindowUpdate = streamUpdate
        };
    }

    public void InitStreamSendWindow(int streamId)
    {
        _streamSendWindows[streamId] = _initialSendStreamWindow;
    }

    public void RemoveStreamSendWindow(int streamId)
    {
        _streamSendWindows.Remove(streamId);
    }

    public void ApplyInitialWindowSizeDelta(long delta)
    {
        _initialSendStreamWindow += delta;
        foreach (var streamId in _streamSendWindows.Keys.ToList())
        {
            _streamSendWindows[streamId] += delta;
        }
    }

    public WindowUpdateSignal<int>? OnStreamClosed(int streamId)
    {
        WindowUpdateSignal<int>? signal = null;

        if (_pendingStreamIncrements.TryGetValue(streamId, out var pending) && pending > 0)
        {
            signal = new WindowUpdateSignal<int>(streamId, pending);
        }

        _pendingStreamIncrements.Remove(streamId);
        _recvStreamWindows.Remove(streamId);
        _streamSendWindows.Remove(streamId);
        _deliveredSinceSample.Remove(streamId);
        _lastSampleTimestamp.Remove(streamId);

        return signal;
    }

    public void OnGoAway()
    {
        GoAwayReceived = true;
    }

    public void Reset(int connectionWindowSize, int streamWindowSize)
    {
        GoAwayReceived = false;
        RecvConnectionWindow = connectionWindowSize;
        _initialRecvStreamWindow = streamWindowSize;
        _connectionSendWindow = 65535;
        _initialSendStreamWindow = 65535;
        _recvStreamWindows.Clear();
        _streamSendWindows.Clear();
        _pendingConnIncrement = 0;
        _pendingStreamIncrements.Clear();
        _deliveredSinceSample.Clear();
        _lastSampleTimestamp.Clear();
        _rtt?.Reset();

        const int minWindowUpdateThreshold = 8 * 1024;
        _windowUpdateThreshold = Math.Max(minWindowUpdateThreshold, streamWindowSize / 4);
    }

    public SettingsResult OnRemoteSettings(SettingsFrame frame)
    {
        if (frame.IsAck)
        {
            return default;
        }

        int? maxConcurrentStreamsChange = null;
        int? initialWindowSizeChange = null;

        foreach (var (key, value) in frame.Parameters)
        {
            if (key == SettingsParameter.InitialWindowSize)
            {
                initialWindowSizeChange = (int)value;
                ApplyInitialWindowSizeDelta((int)value - (int)_initialSendStreamWindow);
            }

            if (key == SettingsParameter.MaxConcurrentStreams)
            {
                maxConcurrentStreamsChange = (int)value;
            }
        }

        return new SettingsResult
        {
            MaxConcurrentStreamsChange = maxConcurrentStreamsChange,
            InitialWindowSizeChange = initialWindowSizeChange,
            AckFrame = new SettingsFrame([], isAck: true)
        };
    }

    public PingFrame? OnPing(PingFrame ping)
    {
        if (!ping.IsAck)
        {
            return new PingFrame(ping.Data, true);
        }

        return null;
    }
}