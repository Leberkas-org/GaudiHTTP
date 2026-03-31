using System.Diagnostics;
using System.Diagnostics.Metrics;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Callback delegate invoked when a pipeline stall is detected.
/// Preserved for backwards compatibility with the former <c>DeadlockWatchdogStage</c>.
/// </summary>
/// <param name="stageName">Name of the stage where the stall was detected.</param>
/// <param name="direction">Direction of the stall (<c>"in"</c> or <c>"out"</c>).</param>
/// <param name="stallDuration">How long the stall has lasted so far.</param>
public delegate void DeadlockStallDetected(string stageName, string direction, TimeSpan stallDuration);

/// <summary>
/// Production-ready transparent pass-through stage that detects pipeline stalls.
/// Replaces the former DEBUG-only <c>DeadlockWatchdogStage</c>.
/// <para>
/// When no element passes through the stage within the configured timeout,
/// a stall event is fired: the <see cref="DeadlockStallDetected"/> callback is invoked,
/// the <c>turbohttp.pipeline.stall</c> OTel counter is incremented, and a structured
/// warning is logged via <see cref="TurboTrace"/>.
/// </para>
/// <para>
/// Set <see cref="TurboClientOptions.PipelineStallTimeout"/> to <see cref="TimeSpan.Zero"/>
/// to disable monitoring entirely; the stage becomes a zero-overhead pass-through.
/// </para>
/// </summary>
/// <typeparam name="T">The element type flowing through the stage.</typeparam>
internal sealed class PipelineHealthMonitorStage<T> : GraphStage<FlowShape<T, T>>
{
    private readonly Inlet<T> _in = new("PipelineHealthMonitor.In");
    private readonly Outlet<T> _out = new("PipelineHealthMonitor.Out");

    private readonly TimeSpan _stallTimeout;
    private readonly string _stageName;
    private readonly DeadlockStallDetected? _stallCallback;

    public override FlowShape<T, T> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="PipelineHealthMonitorStage{T}"/>.
    /// </summary>
    /// <param name="stallTimeout">
    /// Time without element movement before a stall is reported.
    /// <see cref="TimeSpan.Zero"/> disables monitoring.
    /// </param>
    /// <param name="stageName">
    /// Label used in metrics and log messages. Defaults to <c>"Pipeline"</c>.
    /// </param>
    /// <param name="stallCallback">
    /// Optional callback invoked on each stall event. Provides backwards compatibility
    /// with the former <c>DeadlockWatchdogStage</c>.
    /// </param>
    public PipelineHealthMonitorStage(
        TimeSpan stallTimeout,
        string stageName = "Pipeline",
        DeadlockStallDetected? stallCallback = null)
    {
        _stallTimeout = stallTimeout;
        _stageName = stageName;
        _stallCallback = stallCallback;
        Shape = new FlowShape<T, T>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => _stallTimeout <= TimeSpan.Zero
            ? new PassThroughLogic(this)
            : new MonitorLogic(this);

    /// <summary>
    /// Zero-overhead pass-through when monitoring is disabled.
    /// </summary>
    private sealed class PassThroughLogic : GraphStageLogic
    {
        public PassThroughLogic(PipelineHealthMonitorStage<T> stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () => Push(stage._out, Grab(stage._in)),
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._out,
                onPull: () => Pull(stage._in));
        }
    }

    /// <summary>
    /// Monitoring logic that detects stalls using a repeating timer.
    /// </summary>
    private sealed class MonitorLogic : TimerGraphStageLogic
    {
        private const string StallTimerKey = "stall-check";

        private readonly PipelineHealthMonitorStage<T> _stage;

        // Per-port counters
        private long _elementsPassed;
        private long _stallEvents;
        private long _lastElementTimestamp;

        // Stall duration tracking (ticks)
        private long _stallDurationMin = long.MaxValue;
        private long _stallDurationMax;
        private long _stallDurationTotal;

        // Track whether we are in an active stall to avoid double-counting
        private bool _inStall;
        private long _stallStartTimestamp;

        public MonitorLogic(PipelineHealthMonitorStage<T> stage) : base(stage.Shape)
        {
            _stage = stage;
            _lastElementTimestamp = Stopwatch.GetTimestamp();

            SetHandler(stage._in,
                onPush: () =>
                {
                    RecordElementPassed();
                    Push(stage._out, Grab(stage._in));
                },
                onUpstreamFinish: () =>
                {
                    CancelTimer(StallTimerKey);
                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    CancelTimer(StallTimerKey);
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (!HasBeenPulled(stage._in))
                    {
                        Pull(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            ScheduleRepeatedly(StallTimerKey, _stage._stallTimeout, _stage._stallTimeout);
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != StallTimerKey)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastElementTimestamp, now);

            if (elapsed < _stage._stallTimeout)
            {
                // Element moved recently — reset stall state
                if (_inStall)
                {
                    RecordStallEnd(now);
                }
                return;
            }

            if (!_inStall)
            {
                _inStall = true;
                _stallStartTimestamp = _lastElementTimestamp;
            }

            var stallDuration = Stopwatch.GetElapsedTime(_stallStartTimestamp, now);

            _stallEvents++;

            // OTel counter
            var tags = new TagList
            {
                { "stage", _stage._stageName },
                { "direction", "in" }
            };
            TurboHttpMetrics.PipelineStall.Add(1, tags);

            // Structured warning via TurboTrace
            TurboTrace.Stream.Warning(this,
                "Pipeline stall detected in stage '{0}': no elements for {1:F1}s (total stalls: {2})",
                _stage._stageName, stallDuration.TotalSeconds, _stallEvents);

            // Backwards-compatible callback
            _stage._stallCallback?.Invoke(_stage._stageName, "in", stallDuration);
        }

        private void RecordElementPassed()
        {
            _elementsPassed++;
            var now = Stopwatch.GetTimestamp();

            if (_inStall)
            {
                RecordStallEnd(now);
            }

            _lastElementTimestamp = now;
        }

        private void RecordStallEnd(long now)
        {
            _inStall = false;
            var duration = now - _stallStartTimestamp;

            if (duration < _stallDurationMin)
            {
                _stallDurationMin = duration;
            }
            if (duration > _stallDurationMax)
            {
                _stallDurationMax = duration;
            }
            _stallDurationTotal += duration;
        }
    }
}
