using System;
using System.Collections.Generic;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Enforces the HTTP/2 MAX_CONCURRENT_STREAMS limit by queueing requests that exceed the limit
/// and releasing them as active streams complete. Sits before <see cref="Http20StreamIdAllocatorStage"/>
/// in the pipeline so that stream IDs are not allocated until the request is ready to be sent.
/// <para>
/// RFC 9113 §5.1.2: A peer can limit the number of concurrently active streams using the
/// SETTINGS_MAX_CONCURRENT_STREAMS parameter. An endpoint MUST NOT exceed the limit set by its peer.
/// </para>
/// </summary>
internal sealed class Http20StreamLimiterStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    /// <summary>Default maximum number of pending requests in the queue before rejecting new ones.</summary>
    internal const int DefaultMaxPendingQueueSize = 100;

    /// <summary>Default timeout for requests waiting in the queue.</summary>
    internal static readonly TimeSpan DefaultQueueTimeout = TimeSpan.FromSeconds(30);

    private readonly Inlet<HttpRequestMessage> _in = new("StreamLimiter.In");
    private readonly Outlet<HttpRequestMessage> _out = new("StreamLimiter.Out");

    private readonly StreamLimiterHandle _handle;
    private readonly int _initialMaxConcurrentStreams;
    private readonly int _maxPendingQueueSize;
    private readonly TimeSpan _queueTimeout;

    public Http20StreamLimiterStage(
        StreamLimiterHandle handle,
        int initialMaxConcurrentStreams,
        TimeSpan? queueTimeout = null,
        int maxPendingQueueSize = DefaultMaxPendingQueueSize)
    {
        _handle = handle;
        _initialMaxConcurrentStreams = initialMaxConcurrentStreams;
        _maxPendingQueueSize = maxPendingQueueSize;
        _queueTimeout = queueTimeout ?? DefaultQueueTimeout;
        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_in, _out);
    }

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private readonly Http20StreamLimiterStage _stage;
        private int _maxConcurrentStreams;
        private int _activeStreams;

        private readonly Queue<(HttpRequestMessage Request, long TimerKey)> _pendingQueue = new();
        private long _nextTimerKey;

        public Logic(Http20StreamLimiterStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _maxConcurrentStreams = stage._initialMaxConcurrentStreams;

            SetHandler(stage._in, onPush: () =>
            {
                var request = Grab(stage._in);

                if (_activeStreams < _maxConcurrentStreams)
                {
                    EmitRequest(request);
                }
                else if (_pendingQueue.Count < _stage._maxPendingQueueSize)
                {
                    EnqueueRequest(request);

                    // Always continue pulling upstream so we can detect queue overflow
                    // on the next push. Without this pull, overflow items would be
                    // backpressured by Akka.Streams and never reach the stage.
                    if (!HasBeenPulled(stage._in) && !IsClosed(stage._in))
                    {
                        Pull(stage._in);
                    }
                }
                else
                {
                    Log.Warning(
                        "Http20StreamLimiterStage: RFC 9113 §5.1.2 — pending queue full ({0} requests). Rejecting request.",
                        _stage._maxPendingQueueSize);
                    FailStage(new Http2Exception(
                        $"Connection limit exceeded: {_stage._maxPendingQueueSize} requests pending and {_activeStreams} streams active (max {_maxConcurrentStreams})",
                        Http2ErrorCode.RefusedStream,
                        Http2ErrorScope.Stream));
                }
            }, onUpstreamFinish: () =>
            {
                if (_pendingQueue.Count == 0)
                {
                    CompleteStage();
                }
                // else: keep alive until queue drains
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http20StreamLimiterStage: Upstream failure: {0}", ex.Message);
                FailStage(ex);
            });

            SetHandler(stage._out, onPull: () =>
            {
                // Only pull upstream if we have capacity (no items queued, under stream limit).
                // If items are queued, they will be emitted by TryDrainQueue.
                if (_pendingQueue.Count == 0 && !HasBeenPulled(stage._in) && !IsClosed(stage._in))
                {
                    Pull(stage._in);
                }
            });
        }

        public override void PreStart()
        {
            _stage._handle.OnStreamClosed = GetAsyncCallback(HandleStreamClosed);
            _stage._handle.OnMaxConcurrentStreamsChanged = GetAsyncCallback<int>(HandleMaxConcurrentStreamsChanged);
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is long key)
            {
                HandleRequestTimeout(key);
            }
        }

        private void EmitRequest(HttpRequestMessage request)
        {
            _activeStreams++;
            Push(_stage._out, request);

            // Pull next if still under limit and downstream demanded
            if (_activeStreams < _maxConcurrentStreams
                && !HasBeenPulled(_stage._in)
                && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void EnqueueRequest(HttpRequestMessage request)
        {
            var timerKey = _nextTimerKey++;
            _pendingQueue.Enqueue((request, timerKey));
            ScheduleOnce(timerKey, _stage._queueTimeout);

            Log.Debug(
                "Http20StreamLimiterStage: Request queued (pending={0}, active={1}/{2}, timerKey={3}).",
                _pendingQueue.Count, _activeStreams, _maxConcurrentStreams, timerKey);
        }

        private void HandleStreamClosed()
        {
            if (_activeStreams > 0)
            {
                _activeStreams--;
            }

            TryDrainQueue();
        }

        private void HandleMaxConcurrentStreamsChanged(int newMax)
        {
            var oldMax = _maxConcurrentStreams;
            _maxConcurrentStreams = newMax;

            Log.Info(
                "Http20StreamLimiterStage: MAX_CONCURRENT_STREAMS updated from {0} to {1} (active={2}, pending={3}).",
                oldMax, newMax, _activeStreams, _pendingQueue.Count);

            // New limit may allow queued requests to proceed
            TryDrainQueue();
        }

        private void HandleRequestTimeout(long timerKey)
        {
            // Find and remove the timed-out request from the queue
            var count = _pendingQueue.Count;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var item = _pendingQueue.Dequeue();
                if (item.TimerKey == timerKey)
                {
                    found = true;
                    Log.Warning(
                        "Http20StreamLimiterStage: Request timed out after {0}s while waiting in queue (active={1}/{2}, pending={3}).",
                        _stage._queueTimeout.TotalSeconds, _activeStreams, _maxConcurrentStreams, _pendingQueue.Count);
                    // Don't re-enqueue the timed-out request
                    continue;
                }

                _pendingQueue.Enqueue(item);
            }

            if (found)
            {
                FailStage(new Http2Exception(
                    $"Request timed out after {_stage._queueTimeout.TotalSeconds}s waiting for available HTTP/2 stream (active={_activeStreams}, max={_maxConcurrentStreams})",
                    Http2ErrorCode.RefusedStream,
                    Http2ErrorScope.Stream));
            }
        }

        private void TryDrainQueue()
        {
            while (_pendingQueue.Count > 0
                   && _activeStreams < _maxConcurrentStreams
                   && IsAvailable(_stage._out))
            {
                var (request, timerKey) = _pendingQueue.Dequeue();
                CancelTimer(timerKey);
                EmitRequest(request);
                return; // Emit pushes downstream; wait for next pull
            }

            // If queue is empty and upstream finished, complete the stage
            if (_pendingQueue.Count == 0 && IsClosed(_stage._in))
            {
                CompleteStage();
                return;
            }

            // If queue drained below capacity and we need more requests, pull upstream
            if (_pendingQueue.Count < _stage._maxPendingQueueSize
                && _activeStreams < _maxConcurrentStreams
                && !HasBeenPulled(_stage._in)
                && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}
