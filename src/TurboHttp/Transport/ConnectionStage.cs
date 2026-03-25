using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Pooling;

// QuicConnectionManager is platform-guarded; ConnectionStage gates QUIC usage
// at runtime via `connect.Options is QuicOptions`.
#pragma warning disable CA1416

namespace TurboHttp.Transport;

/// <summary>
/// Unified transport stage for ALL HTTP versions.
/// <para><b>TCP single-stream</b> (HTTP/1.0, 1.1, 2.0): acquires a <see cref="ConnectionLease"/>
/// from <see cref="ConnectionPool"/> via <see cref="ConnectionPool.AcquireAsync"/>,
/// manages a single <see cref="ConnectionHandle"/> for outbound writes and inbound reads.</para>
/// <para><b>QUIC multi-stream</b> (HTTP/3): creates a <see cref="QuicConnectionManager"/>
/// and opens typed streams (Request, Control, QpackEncoder) each with its own
/// <see cref="ConnectionHandle"/>. Unwraps <see cref="Http3TaggedItem"/> to route
/// outbound data to the correct stream, and tags inbound data with
/// <see cref="InputStreamType"/> for the engine pipeline.</para>
/// </summary>
internal sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private ConnectionPool Pool { get; }

    private readonly Inlet<IOutputItem> _in = new("Connection.In");
    private readonly Outlet<IInputItem> _out = new("Connection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public ConnectionStage(ConnectionPool pool)
    {
        Pool = pool;
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        /// <summary>Timer key for connection acquisition timeout.</summary>
        private const string ConnectTimerKey = "connect-timeout";

        private readonly ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        /// <summary>Outbound items received before the ConnectionHandle was available (e.g. HTTP/2 preface).</summary>
        private readonly Queue<IOutputItem> _pendingWrites = new();

        // ── TCP single-stream state (HTTP/1.x, HTTP/2) ──

        /// <summary>Current connection handle providing direct channel I/O.</summary>
        private ConnectionHandle? _handle;

        /// <summary>Current connection lease wrapping the handle with lifecycle management.</summary>
        private ConnectionLease? _currentLease;

        // ── QUIC multi-stream state (HTTP/3) ──

        /// <summary>Whether the current connection is QUIC-based (HTTP/3).</summary>
        private bool _isQuic;

        /// <summary>QUIC connection manager — created per ConnectItem when options are QuicOptions.</summary>
        private QuicConnectionManager? _quicManager;

        /// <summary>QUIC request stream handle.</summary>
        private ConnectionHandle? _requestHandle;

        /// <summary>QUIC control stream handle.</summary>
        private ConnectionHandle? _controlHandle;

        /// <summary>QUIC QPACK encoder stream handle.</summary>
        private ConnectionHandle? _encoderHandle;

        /// <summary>Pending control items buffered before control stream is ready.</summary>
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingControlItems =
            new();

        /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
        private readonly Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> _pendingEncoderItems =
            new();

        /// <summary>All active leases for QUIC streams (disposed on PostStop).</summary>
        private readonly List<ConnectionLease> _activeLeases = [];

        /// <summary>Cancellation tokens for all QUIC inbound pumps.</summary>
        private readonly List<CancellationTokenSource> _quicPumpCancellations = [];

        /// <summary>Pending typed stream type being opened (Control or QpackEncoder).</summary>
        private OutputStreamType? _pendingTypedStreamType;

        // ── Async callbacks ──

        /// <summary>Callback bridging async channel reads into the stage event loop.</summary>
        private Action<IInputItem>? _onInboundData;

        /// <summary>Callback bridging async channel write completion into the stage event loop.</summary>
        private Action? _onOutboundWriteDone;

        /// <summary>Callback invoked when an outbound channel write fails (e.g. channel closed).</summary>
        private Action<Exception>? _onOutboundWriteFailed;

        /// <summary>Callback invoked when a <see cref="ConnectionLease"/> is acquired from the pool (TCP path).</summary>
        private Action<ConnectionLease>? _onLeaseAcquired;

        /// <summary>Callback invoked when a QUIC request stream lease is acquired.</summary>
        private Action<ConnectionLease>? _onRequestLeaseAcquired;

        /// <summary>Callback invoked when a QUIC typed stream lease is acquired.</summary>
        private Action<ConnectionLease>? _onTypedLeaseAcquired;

        /// <summary>Callback invoked when connection acquisition fails.</summary>
        private Action<Exception>? _onAcquisitionFailed;

        /// <summary>Callback invoked when the inbound channel completes (connection closed).
        /// Tuple: (closeKind, connectionGeneration) — stale generations are ignored.</summary>
        private Action<(TlsCloseKind CloseKind, int Gen)>? _onInboundComplete;

        /// <summary>Callback bridging async flush-next completion into the stage event loop.</summary>
        private Action? _onFlushNext;

        /// <summary>Callback invoked when a server-initiated QUIC inbound stream is ready.</summary>
        private Action<QuicConnectionManager.InboundStream>? _onInboundStreamReady;

        private CancellationTokenSource? _pumpCts;

        /// <summary>Set when upstream finishes; defers stage completion until the inbound pump drains.</summary>
        private bool _upstreamFinished;

        /// <summary>The RequestEndpoint from the most recent ConnectItem — used to tag inbound DataItems.</summary>
        private RequestEndpoint _currentKey;

        /// <summary>The ConnectItem currently awaiting a ConnectionLease.</summary>
        private ConnectItem? _pendingConnect;

        /// <summary>
        /// Whether the lease has been returned to the pool for the current connection lifecycle.
        /// Prevents double-release when both HandlePush(ConnectionReuseItem) and
        /// _onInboundComplete fire for the same connection lifecycle. TCP path only.
        /// </summary>
        private bool _leaseReturned;

        /// <summary>
        /// Monotonically increasing generation counter for TCP connections.
        /// Incremented each time a new <see cref="ConnectionLease"/> is acquired.
        /// Stale inbound pump completions (from a prior generation) are ignored
        /// to prevent them from destroying the new connection's state.
        /// </summary>
        private int _connectionGen;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    if (_isQuic)
                    {
                        StopAllQuicPumps();
                        CompleteStage();
                    }
                    else
                    {
                        _upstreamFinished = true;
                        if (_handle is null)
                        {
                            CompleteStage();
                        }
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._out, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    StopInboundPump();
                    StopAllQuicPumps();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onInboundData = GetAsyncCallback<IInputItem>(item =>
            {
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, item);
                }
                else
                {
                    _pendingReads.Enqueue(item);
                }
            });

            _onOutboundWriteDone = GetAsyncCallback(() =>
            {
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onOutboundWriteFailed = GetAsyncCallback<Exception>(ex =>
            {
                Log.Warning("ConnectionStage: Outbound write failed — {0}", ex.Message);

                if (_isQuic)
                {
                    // Emit close signal downstream so decoder stages know the connection is dead.
                    var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, signal);
                    }
                    else
                    {
                        _pendingReads.Enqueue(signal);
                    }

                    // Clear handles so the next ConnectItem re-acquires a fresh connection.
                    _requestHandle = null;
                    _controlHandle = null;
                    _encoderHandle = null;
                }
                else
                {
                    // Mark lease as non-reusable and release it back to the pool.
                    if (_currentLease is { } lease)
                    {
                        lease.MarkNoReuse();
                    }

                    ReturnLeaseToPool(canReuse: false);

                    // Emit close signal downstream so decoder stages know the connection is dead.
                    var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, signal);
                    }
                    else
                    {
                        _pendingReads.Enqueue(signal);
                    }

                    // Connection is dead — clear handle so next ConnectItem re-acquires.
                    StopInboundPump();
                    _handle = null;
                    _currentLease = null;

                    // Accept next element (e.g. a new ConnectItem for reconnection).
                    TryPull();
                }
            });

            _onLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
            {
                CancelTimer(ConnectTimerKey);

                // Guard: if _pendingConnect is already null, the lease was already
                // received (e.g. duplicate). Skip to avoid restarting the inbound pump concurrently.
                if (_pendingConnect is null && _handle is not null)
                {
                    return;
                }

                _pendingConnect = null;

                // Increment generation BEFORE resetting _leaseReturned so that any stale
                // _onInboundComplete from the prior pump is ignored (it carries the old gen).
                _connectionGen++;
                _leaseReturned = false;

                // Discard any stale inbound items (DataItem / CloseSignalItem) that the
                // prior connection's pump pushed into the queue before it was cancelled.
                // Without this, a stale CloseSignalItem(AbruptClose) could reach the
                // decoder after the new connection is established and FailStage it.
                _pendingReads.Clear();

                _currentLease = lease;
                _handle = lease.Handle;
                _currentKey = lease.Key;
                StartInboundPump();

                // Flush items that arrived before the handle was available
                // (e.g. HTTP/2 preface buffered during connection setup).
                FlushPendingWrites();
            });

            _onRequestLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
            {
                CancelTimer(ConnectTimerKey);

                if (_pendingConnect is null && _requestHandle is not null)
                {
                    return;
                }

                _pendingConnect = null;

                _activeLeases.Add(lease);
                _requestHandle = lease.Handle;
                _currentKey = lease.Key;
                StartQuicInboundPump(lease.Handle, InputStreamType.Request);

                // Open control and QPACK encoder streams via QuicConnectionManager.
                OpenTypedStream(OutputStreamType.Control);
                OpenTypedStream(OutputStreamType.QpackEncoder);

                // Subscribe to server-initiated inbound streams.
                _quicManager?.StartInboundAcceptLoop(inbound => _onInboundStreamReady!(inbound));

                // Ready to process data items — pull next element.
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onTypedLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
            {
                _activeLeases.Add(lease);
                var streamType = _pendingTypedStreamType;
                _pendingTypedStreamType = null;

                switch (streamType)
                {
                    case OutputStreamType.Control:
                        _controlHandle = lease.Handle;
                        FlushPendingQuicItems(_pendingControlItems, lease.Handle);
                        break;

                    case OutputStreamType.QpackEncoder:
                        _encoderHandle = lease.Handle;
                        FlushPendingQuicItems(_pendingEncoderItems, lease.Handle);
                        break;
                }
            });

            _onAcquisitionFailed = GetAsyncCallback<Exception>(ex =>
            {
                CancelTimer(ConnectTimerKey);

                Log.Warning("ConnectionStage: Connection acquisition failed — {0}", ex.Message);

                if (_pendingConnect is null)
                {
                    return;
                }

                // Emit close signal so the decoder/correlation stage fails the pending request.
                var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
                _pendingConnect = null;

                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

                // Accept next element from upstream.
                TryPull();
            });

            _onInboundStreamReady = GetAsyncCallback<QuicConnectionManager.InboundStream>(inbound =>
            {
                _activeLeases.Add(inbound.Lease);
                StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
            });

            _onInboundComplete = GetAsyncCallback<(TlsCloseKind CloseKind, int Gen)>(tuple =>
            {
                var (closeKind, gen) = tuple;

                if (_isQuic)
                {
                    // Emit close signal to downstream decoder stages before clearing the handle.
                    var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, signal);
                    }
                    else
                    {
                        _pendingReads.Enqueue(signal);
                    }

                    // Connection closed — clear handles so next ConnectItem re-acquires.
                    _requestHandle = null;
                    _controlHandle = null;
                    _encoderHandle = null;
                }
                else
                {
                    // Guard: ignore stale pump completions from a prior connection generation.
                    // This prevents the old pump's completion from destroying a newly-acquired
                    // connection when the events race (old pump completes after new lease acquired).
                    if (gen != _connectionGen)
                    {
                        return;
                    }

                    // Emit close signal to downstream decoder stages before clearing the handle.
                    var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, signal);
                    }
                    else
                    {
                        _pendingReads.Enqueue(signal);
                    }

                    // Mark lease as non-reusable and release back to pool.
                    if (_currentLease is { } lease)
                    {
                        lease.MarkNoReuse();
                    }

                    ReturnLeaseToPool(canReuse: false);

                    // Connection closed — clear the handle so next ConnectItem re-acquires.
                    _handle = null;
                    _currentLease = null;

                    if (_upstreamFinished)
                    {
                        CompleteStage();
                    }
                    else
                    {
                        // Maintain demand on the inlet so that upstream stages (e.g. Broadcast
                        // feeding both ExtractOptionsStage and ConnectionStage) are not blocked.
                        // Without this, the Broadcast requires ALL outputs to have demand before
                        // pushing — if ConnectionStage has no demand, the reconnection signal
                        // never reaches ExtractOptionsStage.InReuse, causing HTTP/1.0 requests
                        // that need reconnection (redirect/retry) to deadlock.
                        TryPull();
                    }
                }
            });

            _onFlushNext = GetAsyncCallback(FlushNext);

            // Ready to accept ConnectItem immediately.
            Pull(_stage._in);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);

            // ── QUIC path: Http3TaggedItem routing ──
            if (item is Http3TaggedItem tagged)
            {
                HandleTaggedItem(tagged);
                return;
            }

            var lease = _currentLease;

            if (item is MaxConcurrentStreamsItem maxStreams)
            {
                if (!_isQuic)
                {
                    lease?.UpdateMaxConcurrentStreams(maxStreams.MaxStreams);
                }

                // HTTP/3 does not use MAX_CONCURRENT_STREAMS via frames — QUIC transport handles this.
                TryPull();
                return;
            }

            if (item is StreamAcquireItem)
            {
                if (!_isQuic)
                {
                    lease?.MarkBusy();
                }

                // Stream acquisition for QUIC is handled by QuicConnectionManager.
                TryPull();
                return;
            }

            if (item is ConnectionReuseItem reuseItem)
            {
                if (!_isQuic)
                {
                    if (!reuseItem.Decision.CanReuse)
                    {
                        lease?.MarkNoReuse();
                    }

                    ReturnLeaseToPool(reuseItem.Decision.CanReuse);
                }

                // HTTP/3 connections are managed by QuicConnectionManager lifecycle.
                TryPull();
                return;
            }

            if (item is ConnectItem connect)
            {
                _pendingConnect = connect;

                if (connect.Options is QuicOptions quicOptions)
                {
                    // QUIC multi-stream path (HTTP/3).
                    _isQuic = true;
                    _quicManager = new QuicConnectionManager(quicOptions, connect.Key);
                    AcquireQuicConnection(connect);
                }
                else
                {
                    // TCP single-stream path (HTTP/1.x, HTTP/2).
                    _isQuic = false;
                    AcquireConnection(connect);
                }

                // Do NOT pull — wait for ConnectionLease before accepting data.
                return;
            }

            if (item is not DataItem dataItem) return;

            if (_isQuic)
            {
                // Untagged DataItem defaults to request stream in QUIC mode.
                WriteToHandle(_requestHandle, dataItem.Memory, dataItem.Length);
            }
            else
            {
                var handle = _handle;
                if (handle is null)
                {
                    // Buffer items that arrive before the connection is established
                    // (e.g. HTTP/2 preface from PrependPrefaceStage racing ahead of ConnectItem).
                    _pendingWrites.Enqueue(dataItem);
                    TryPull();
                    return;
                }

                // Write directly to the connection's outbound channel.
                var writeTask = handle.OutboundWriter
                    .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(dataItem.Memory, dataItem.Length))
                    .AsTask();

                writeTask.ContinueWith(
                    _ => _onOutboundWriteDone!(),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

                writeTask.ContinueWith(
                    t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  QUIC multi-stream helpers
        // ══════════════════════════════════════════════════════════════════

        private void HandleTaggedItem(Http3TaggedItem tagged)
        {
            if (tagged.Inner is not DataItem dataItem)
            {
                // Non-data tagged items (control signals) — no routing needed.
                TryPull();
                return;
            }

            switch (tagged.StreamType)
            {
                case OutputStreamType.Request:
                    WriteToHandle(_requestHandle, dataItem.Memory, dataItem.Length);
                    break;

                case OutputStreamType.Control:
                    if (_controlHandle is not null)
                    {
                        WriteToHandle(_controlHandle, dataItem.Memory, dataItem.Length);
                    }
                    else
                    {
                        _pendingControlItems.Enqueue((dataItem.Memory, dataItem.Length, dataItem.Key));
                    }

                    break;

                case OutputStreamType.QpackEncoder:
                    if (_encoderHandle is not null)
                    {
                        WriteToHandle(_encoderHandle, dataItem.Memory, dataItem.Length);
                    }
                    else
                    {
                        _pendingEncoderItems.Enqueue((dataItem.Memory, dataItem.Length, dataItem.Key));
                    }

                    break;
            }
        }

        private void WriteToHandle(ConnectionHandle? handle, IMemoryOwner<byte> memory, int length)
        {
            if (handle is null)
            {
                Log.Warning(
                    "ConnectionStage: Data received but no ConnectionHandle is available — dropping element.");
                TryPull();
                return;
            }

            var writeTask = handle.OutboundWriter
                .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(memory, length))
                .AsTask();

            writeTask.ContinueWith(
                _ => _onOutboundWriteDone!(),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            writeTask.ContinueWith(
                t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Flushes buffered QUIC items to a newly-available handle, then pulls for more input.
        /// </summary>
        private void FlushPendingQuicItems(
            Queue<(IMemoryOwner<byte> Memory, int Length, RequestEndpoint Key)> pending,
            ConnectionHandle handle)
        {
            while (pending.TryDequeue(out var item))
            {
                var writeTask = handle.OutboundWriter
                    .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(item.Memory, item.Length))
                    .AsTask();

                writeTask.ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted)
                        {
                            _onOutboundWriteFailed!(t.Exception!.GetBaseException());
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously);
            }

            // If we were waiting to pull because items were buffered, do so now.
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        /// <summary>
        /// Acquires a QUIC request stream from <see cref="QuicConnectionManager"/>.
        /// </summary>
        private void AcquireQuicConnection(ConnectItem connect)
        {
            var manager = _quicManager;
            if (manager is null)
            {
                _onAcquisitionFailed!(new InvalidOperationException("QuicConnectionManager not initialized"));
                return;
            }

            var acquireTask = manager.OpenStreamAsync(OutputStreamType.Request);

            acquireTask.ContinueWith(
                t => _onRequestLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            acquireTask.ContinueWith(
                t => _onAcquisitionFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

            var timeout = connect.Options.ConnectTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

            ScheduleOnce(ConnectTimerKey, timeout);
        }

        /// <summary>
        /// Opens a typed QUIC stream (Control or QpackEncoder) via <see cref="QuicConnectionManager"/>.
        /// </summary>
        private void OpenTypedStream(OutputStreamType streamType)
        {
            var manager = _quicManager;
            if (manager is null)
            {
                return;
            }

            _pendingTypedStreamType = streamType;
            var openTask = manager.OpenStreamAsync(streamType);

            openTask.ContinueWith(
                t => _onTypedLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            openTask.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        Log.Warning("ConnectionStage: Failed to open {0} stream — {1}",
                            streamType, t.Exception!.GetBaseException().Message);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Starts an async pump for a QUIC stream that reads from <see cref="ConnectionHandle.InboundReader"/>
        /// and pushes each chunk into the stage, tagged with the appropriate <see cref="InputStreamType"/>.
        /// </summary>
        private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType)
        {
            var cts = new CancellationTokenSource();
            _quicPumpCancellations.Add(cts);

            var ct = cts.Token;
            var reader = handle.InboundReader;
            var key = _currentKey;
            var gen = _connectionGen;
            var onData = _onInboundData!;
            var onComplete = _onInboundComplete!;

            _ = Task.Run(async () =>
            {
                var closeKind = TlsCloseKind.CleanClose;
                try
                {
                    while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var chunk))
                        {
                            var dataItem = new DataItem(chunk.Buffer, chunk.ReadableBytes) { Key = key };

                            IInputItem outputItem = streamType == InputStreamType.Request
                                ? dataItem
                                : new Http3InputTaggedItem(dataItem, streamType);

                            onData(outputItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stage shutdown — do not emit close signal.
                    return;
                }
                catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
                {
                    closeKind = TlsCloseKind.AbruptClose;
                }

                // Only emit close signal for the request stream (main connection lifecycle).
                if (streamType == InputStreamType.Request)
                {
                    onComplete((closeKind, gen));
                }
            }, ct);
        }

        private void StopAllQuicPumps()
        {
            foreach (var cts in _quicPumpCancellations)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _quicPumpCancellations.Clear();
        }

        // ══════════════════════════════════════════════════════════════════
        //  TCP single-stream helpers
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Writes all buffered outbound items to the connection and then pulls upstream.
        /// Called after a <see cref="ConnectionLease"/> is acquired.
        /// </summary>
        private void FlushPendingWrites()
        {
            if (_pendingWrites.Count == 0)
            {
                TryPull();
                return;
            }

            FlushNext();
        }

        private void FlushNext()
        {
            if (!_pendingWrites.TryDequeue(out var item))
            {
                // All buffered items flushed — resume normal upstream pulls.
                TryPull();
                return;
            }

            if (item is DataItem dataItem && _handle is { } handle)
            {
                var writeTask = handle.OutboundWriter
                    .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(dataItem.Memory, dataItem.Length))
                    .AsTask();

                writeTask.ContinueWith(
                    _ => _onFlushNext!(),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

                writeTask.ContinueWith(
                    t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
            }
            else
            {
                // Non-data items shouldn't be buffered, but handle gracefully.
                FlushNext();
            }
        }

        /// <summary>
        /// Releases the current lease back to the <see cref="ConnectionPool"/> exactly once per
        /// connection lifecycle. Idempotent — safe to call from both HandlePush
        /// (ConnectionReuseItem) and <see cref="_onInboundComplete"/>.
        /// </summary>
        private void ReturnLeaseToPool(bool canReuse)
        {
            if (_leaseReturned || _currentLease is null)
            {
                return;
            }

            _leaseReturned = true;
            _stage.Pool.Release(_currentLease, canReuse);
        }

        /// <summary>
        /// Acquires a TCP connection from the <see cref="ConnectionPool"/> and schedules a timeout.
        /// If the pool returns a <see cref="ConnectionLease"/> before the timer fires,
        /// the stage starts I/O. Otherwise, a <see cref="CloseSignalItem"/> is emitted.
        /// </summary>
        private void AcquireConnection(ConnectItem connect)
        {
            var acquireTask = _stage.Pool.AcquireAsync(connect.Options, connect.Key);

            acquireTask.ContinueWith(
                t => _onLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            acquireTask.ContinueWith(
                t => _onAcquisitionFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

            var timeout = connect.Options.ConnectTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromSeconds(10);
            }

            ScheduleOnce(ConnectTimerKey, timeout);
        }

        // ══════════════════════════════════════════════════════════════════
        //  Shared helpers
        // ══════════════════════════════════════════════════════════════════

        private void TryPull()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != ConnectTimerKey)
            {
                return;
            }

            if (_pendingConnect is null)
            {
                return;
            }

            Log.Warning(
                "ConnectionStage: Connection acquisition timed out for {0}:{1}",
                _pendingConnect.Key.Host,
                _pendingConnect.Key.Port);

            // Emit close signal so the decoder/correlation stage fails the pending request.
            // The stream stays alive — future ConnectItems can still succeed.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, signal);
            }
            else
            {
                _pendingReads.Enqueue(signal);
            }

            // Accept next element from upstream.
            TryPull();
        }

        /// <summary>
        /// Starts an async loop that reads from <see cref="ConnectionHandle.InboundReader"/>
        /// and pushes each chunk into the stage via <see cref="_onInboundData"/>. TCP path only.
        /// </summary>
        private void StartInboundPump()
        {
            StopInboundPump();

            var handle = _handle;
            if (handle is null)
            {
                return;
            }

            _pumpCts = new CancellationTokenSource();
            var ct = _pumpCts.Token;
            var reader = handle.InboundReader;
            var key = _currentKey;
            var gen = _connectionGen;
            var onData = _onInboundData!;
            var onComplete = _onInboundComplete!;

            _ = Task.Run(async () =>
            {
                var closeKind = TlsCloseKind.CleanClose;
                try
                {
                    while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (reader.TryRead(out var chunk))
                        {
                            var dataItem = new DataItem(chunk.Buffer, chunk.ReadableBytes) { Key = key };
                            onData(dataItem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on stage shutdown — do not emit close signal.
                    return;
                }
                catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
                {
                    closeKind = TlsCloseKind.AbruptClose;
                }

                onComplete((closeKind, gen));
            }, ct);
        }

        private void StopInboundPump()
        {
            if (_pumpCts is null) return;
            _pumpCts.Cancel();
            _pumpCts.Dispose();
            _pumpCts = null;
        }

        public override void PostStop()
        {
            CancelTimer(ConnectTimerKey);
            StopInboundPump();
            StopAllQuicPumps();

            // Dispose the current TCP lease if still held.
            if (_currentLease is { } lease)
            {
                lease.Dispose();
                _currentLease = null;
                _handle = null;
            }

            // Dispose all active QUIC leases.
            foreach (var quicLease in _activeLeases)
            {
                quicLease.Dispose();
            }

            _activeLeases.Clear();

            // Dispose the QuicConnectionManager.
            if (_quicManager is { } manager)
            {
                _ = manager.DisposeAsync();
                _quicManager = null;
            }
        }
    }
}
