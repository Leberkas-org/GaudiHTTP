using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Encapsulates all QUIC transport state and logic — multi-stream I/O (request, control, encoder),
/// tagged item routing, and connection lifecycle management.
/// Calls back into <see cref="ITransportOperations"/> for Akka-specific operations
/// (Push, Pull, Timer, Complete, Fail).
/// Async events arrive via <see cref="Dispatch"/> after being marshaled through the StageActorRef.
/// <para>
/// Connection acquisition is delegated to <see cref="QuicConnectionManagerActor"/> (via actor tell),
/// mirroring how <see cref="TcpTransportStateMachine"/> uses <see cref="TcpConnectionManagerActor"/>.
/// </para>
/// </summary>
internal sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _self;
    private readonly IActorRef _quicManagerActor;
    private readonly TurboClientOptions _clientOptions;
    private readonly bool _allowConnectionMigration;

    private int _connectionGen;

    private QuicConnectionLease? _currentConnectionLease;
    private ConnectionHandle? _controlHandle;
    private ConnectionHandle? _encoderHandle;

    private TlsCloseKind _lastCloseKind = TlsCloseKind.CleanClose;

    /// <summary>Per-stream transport context for concurrent request streams.</summary>
    private readonly Dictionary<long, RequestStreamContext> _requestStreams = new();

    /// <summary>
    /// Stream ID of the most recently requested stream whose handle has not yet arrived.
    /// Used to associate the next <see cref="QuicTransportEvent.RequestLeaseAcquired"/> callback
    /// with the correct stream context.
    /// </summary>
    private long _pendingOpenStreamId = -1;

    /// <summary>Pending control items buffered before control stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingControlItems = new();

    /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingEncoderItems = new();

    /// <summary>All active stream leases for this connection (disposed on Cleanup).</summary>
    private readonly List<ConnectionLease> _activeLeases = [];

    /// <summary>CancellationTokenSources for all active QUIC inbound stream pumps.</summary>
    private readonly List<CancellationTokenSource> _quicPumpCancellations = [];

    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;
    private CancellationTokenSource? _acquireCts;
    private CancellationTokenSource? _inboundAcceptCts;

    /// <summary>Tracks the last observed local endpoint for connection migration detection.</summary>
    private System.Net.EndPoint? _lastLocalEndPoint;

    /// <summary>
    /// Per-stream transport state: tracks the handle, pending writes, and end-of-request flag
    /// for each concurrent request stream on the QUIC connection.
    /// </summary>
    private sealed class RequestStreamContext
    {
        public ConnectionHandle? Handle;
        public readonly Queue<NetworkBuffer> PendingWrites = new();
        public bool PendingEndOfRequest;
    }

    public QuicTransportStateMachine(ITransportOperations ops, IActorRef self, IActorRef quicManagerActor,
        TurboClientOptions clientOptions, bool allowConnectionMigration = true)
    {
        _ops = ops;
        _self = self;
        _quicManagerActor = quicManagerActor;
        _clientOptions = clientOptions;
        _allowConnectionMigration = allowConnectionMigration;
    }

    public void Dispatch(QuicTransportEvent evt)
    {
        switch (evt)
        {
            case QuicTransportEvent.ConnectionLeaseAcquired e:
                OnConnectionLeaseAcquired(e.Lease);
                break;
            case QuicTransportEvent.RequestLeaseAcquired e:
                OnRequestLeaseAcquired(e.Lease, e.StreamId);
                break;
            case QuicTransportEvent.TypedLeaseAcquired e:
                OnTypedLeaseAcquired(e.Lease, e.StreamType);
                break;
            case QuicTransportEvent.AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case QuicTransportEvent.InboundData e:
                if (e.Gen == _connectionGen)
                {
                    CheckForConnectionMigration();
                    _ops.OnPushOutput(e.Item);
                }

                break;
            case QuicTransportEvent.InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.CloseKind, e.StreamId);
                }

                break;
            case QuicTransportEvent.InboundPumpFailed e:
                _ops.Log.Warning("QuicConnectionStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(TlsCloseKind.AbruptClose, e.StreamId);
                break;
            case QuicTransportEvent.InboundStreamReady e:
                OnInboundStreamReady(e.Stream);
                break;
            case QuicTransportEvent.OutboundWriteDone:
                _ops.OnSignalPullInput();
                break;
            case QuicTransportEvent.OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
            case QuicTransportEvent.EarlyDataRejected e:
                OnEarlyDataRejected(e.Buffer);
                break;
            case QuicTransportEvent.ConnectionMigrated e:
                OnConnectionMigrated(e.OldLocalEndPoint, e.NewLocalEndPoint);
                break;
        }
    }

    public void HandlePush(IOutputItem item)
    {
        // Extract stream ID from tagged items for routing.
        var streamId = item switch
        {
            Http3OutputTaggedItem t => t.StreamId,
            Http3EndOfRequestItem e => e.StreamId,
            _ => -1L
        };

        // Auto-connect or open a new request stream on the existing connection.
        if (streamId >= 0 && !_requestStreams.ContainsKey(streamId) && _pendingConnect is null &&
            item.Key.Scheme is not null && item.Key != RequestEndpoint.Default)
        {
            if (_currentConnectionLease is not null && _controlHandle is not null)
            {
                // QUIC connection is still alive with control/encoder streams.
                // Create context and open a new request stream for this stream ID.
                _requestStreams[streamId] = new RequestStreamContext();
                OpenNewRequestStream(streamId);
            }
            else
            {
                // No connection yet — create context and initiate full connect.
                _requestStreams[streamId] = new RequestStreamContext();
                _pendingOpenStreamId = streamId;
                AutoConnect(item.Key);
            }
        }

        switch (item)
        {
            case ConnectItem connect:
                HandleConnectItem(connect);
                break;

            case Http3OutputTaggedItem tagged:
                HandleTaggedItem(tagged);
                break;

            case NetworkBuffer dataItem:
                HandleDataItem(dataItem);
                break;

            case Http3EndOfRequestItem endItem:
                HandleEndOfRequestItem(endItem);
                break;

            case ConnectionReuseItem:
            case StreamAcquireItem:
            case MaxConcurrentStreamsItem:
                // QUIC manages these internally — no-op
                _ops.OnSignalPullInput();
                break;
        }
    }

    private void HandleEndOfRequestItem(Http3EndOfRequestItem endItem)
    {
        // All request frames have been written — complete the outbound channel so
        // MoveChannelToStream drains remaining data, exits, and calls CompleteWrites()
        // to send FIN on the QUIC stream write side. RFC 9114 §4.1.
        if (_requestStreams.TryGetValue(endItem.StreamId, out var ctx) && ctx.Handle is not null)
        {
            ctx.Handle.OutboundWriter.TryComplete();
        }
        else if (_requestStreams.TryGetValue(endItem.StreamId, out var pendingCtx))
        {
            // Request handle not yet ready (async acquisition still in flight).
            // Record the pending flag; OnRequestLeaseAcquired will flush it after
            // all buffered request frames have been written to the handle.
            pendingCtx.PendingEndOfRequest = true;
        }

        _ops.OnSignalPullInput();
    }

    public void HandleUpstreamFinish()
    {
        StopAllQuicPumps();
        _ops.OnCompleteStage();
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    private void HandleConnectItem(ConnectItem connect)
    {
        _ops.Log.Debug("QuicConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

        CleanupTransport();
        _pendingConnect = connect;

        if (connect.Options is not QuicOptions quicOptions)
        {
            _self.Tell(new QuicTransportEvent.AcquisitionFailed(new InvalidOperationException(
                "QuicConnectionStage received a non-QuicOptions ConnectItem.")));
            return;
        }

        AcquireQuicConnection(quicOptions, connect);
    }

    private void AutoConnect(RequestEndpoint endpoint)
    {
        _ops.Log.Debug("QuicConnectionStage: AutoConnect for {0}:{1}", endpoint.Host, endpoint.Port);

        var options = OptionsFactory.Build(endpoint, _clientOptions);
        _pendingConnect = new ConnectItem(options) { Key = endpoint };

        if (options is not QuicOptions quicOptions)
        {
            _self.Tell(new QuicTransportEvent.AcquisitionFailed(new InvalidOperationException(
                "QuicConnectionStage: AutoConnect produced non-QuicOptions for endpoint.")));
            return;
        }

        AcquireQuicConnection(quicOptions, _pendingConnect.Value);
    }

    private void HandleDataItem(NetworkBuffer dataItem)
    {
        // Untagged NetworkBuffer — legacy path. Route to the first available request stream.
        foreach (var ctx in _requestStreams.Values)
        {
            if (ctx.Handle is not null)
            {
                WriteToHandle(ctx.Handle, dataItem);
                return;
            }

            ctx.PendingWrites.Enqueue(dataItem);
            _ops.OnSignalPullInput();
            return;
        }

        // No request streams at all — drop
        _ops.Log.Warning("QuicConnectionStage: Untagged data received but no request stream — dropping.");
        _ops.OnSignalPullInput();
    }

    private void HandleTaggedItem(Http3OutputTaggedItem outputTagged)
    {
        if (outputTagged.Inner is not NetworkBuffer dataItem)
        {
            _ops.OnSignalPullInput();
            return;
        }

        switch (outputTagged.StreamType)
        {
            case OutputStreamType.Request:
            {
                var sid = outputTagged.StreamId;
                if (sid >= 0 && _requestStreams.TryGetValue(sid, out var ctx))
                {
                    if (ctx.Handle is not null)
                    {
                        WriteToHandle(ctx.Handle, dataItem);
                    }
                    else
                    {
                        ctx.PendingWrites.Enqueue(dataItem);
                        _ops.OnSignalPullInput();
                    }
                }
                else
                {
                    // Fallback: untagged or unknown stream — route to first available
                    HandleDataItem(dataItem);
                }

                break;
            }

            case OutputStreamType.Control:
                if (_controlHandle is not null)
                {
                    WriteToHandle(_controlHandle, dataItem);
                }
                else
                {
                    _pendingControlItems.Enqueue(dataItem);
                    _ops.OnSignalPullInput();
                }

                break;

            case OutputStreamType.QpackEncoder:
                if (_encoderHandle is not null)
                {
                    WriteToHandle(_encoderHandle, dataItem);
                }
                else
                {
                    _pendingEncoderItems.Enqueue(dataItem);
                    _ops.OnSignalPullInput();
                }

                break;
        }
    }

    public void OnTimer(string? timerKey)
    {
        if (timerKey != ConnectTimerKey)
        {
            return;
        }

        if (_pendingConnect is null)
        {
            return;
        }

        _ops.Log.Warning("QuicConnectionStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Value.Key.Host, _pendingConnect.Value.Key.Port);

        var signal = new QuicCloseItem(QuicCloseKind.AcquisitionFailed) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    private void OnConnectionLeaseAcquired(QuicConnectionLease lease)
    {
        _currentConnectionLease = lease;

        // Open the request stream on the now-pooled connection.
        // The pending stream ID tells us which protocol-layer stream this belongs to.
        var streamId = _pendingOpenStreamId;
        _ = lease.Handle.OpenStreamAsLeaseAsync(OutputStreamType.Request)
            .PipeTo(_self,
                success: streamLease => new QuicTransportEvent.RequestLeaseAcquired(streamLease, streamId),
                failure: ex => new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException()));
    }

    private void OnRequestLeaseAcquired(ConnectionLease lease, long streamId)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _pendingConnect = null;

        _activeLeases.Add(lease);
        _currentKey = lease.Key;
        _lastLocalEndPoint = _currentConnectionLease?.Handle.LocalEndPoint;

        // Associate the handle with the per-stream context.
        if (!_requestStreams.TryGetValue(streamId, out var ctx))
        {
            ctx = new RequestStreamContext();
            _requestStreams[streamId] = ctx;
        }

        ctx.Handle = lease.Handle;
        StartQuicInboundPump(lease.Handle, InputStreamType.Request, streamId);

        if (_controlHandle is not null)
        {
            // Control/encoder streams already open (request stream reuse on same connection).
            // Flush any pending request data immediately — no need to wait for control stream.
            FlushPendingRequestWrites(ctx);
            _ops.OnSignalPullInput();
        }
        else
        {
            // First request on this connection — open control and QPACK encoder streams.
            // RFC 9114 §3.2: SETTINGS must be the first frame on the control stream.
            // We defer flushing request data until the control stream is ready and
            // SETTINGS have been sent — see OnTypedLeaseAcquired(Control).
            OpenTypedStream(OutputStreamType.Control);
            OpenTypedStream(OutputStreamType.QpackEncoder);

            // Start accepting server-initiated inbound streams
            StartQuicInboundAcceptLoop();
        }
    }

    private void FlushPendingRequestWrites(RequestStreamContext ctx)
    {
        while (ctx.PendingWrites.TryDequeue(out var buffered))
        {
            WriteToHandle(ctx.Handle, buffered);
        }

        if (ctx.PendingEndOfRequest)
        {
            ctx.PendingEndOfRequest = false;
            ctx.Handle!.OutboundWriter.TryComplete();
        }
    }

    private void OnTypedLeaseAcquired(ConnectionLease lease, OutputStreamType streamType)
    {
        _activeLeases.Add(lease);

        switch (streamType)
        {
            case OutputStreamType.Control:
                _controlHandle = lease.Handle;
                FlushPendingQuicItems(_pendingControlItems, lease.Handle);

                // Control stream is ready and SETTINGS have been flushed.
                // Now safe to flush buffered request data on all request streams.
                foreach (var ctx in _requestStreams.Values)
                {
                    if (ctx.Handle is not null)
                    {
                        FlushPendingRequestWrites(ctx);
                    }
                }

                _ops.OnSignalPullInput();
                break;

            case OutputStreamType.QpackEncoder:
                _encoderHandle = lease.Handle;
                FlushPendingQuicItems(_pendingEncoderItems, lease.Handle);
                break;
        }
    }

    private void OnConnectionMigrated(System.Net.EndPoint? oldEndPoint, System.Net.EndPoint? newEndPoint)
    {
        if (_allowConnectionMigration)
        {
            _ops.Log.Info(
                "QuicConnectionStage: Connection migration detected ({0} → {1}) — migration allowed, continuing transparently.",
                oldEndPoint, newEndPoint);
            _lastLocalEndPoint = newEndPoint;
            return;
        }

        _ops.Log.Warning(
            "QuicConnectionStage: Connection migration detected ({0} → {1}) — migration disallowed, closing connection for reconnect.",
            oldEndPoint, newEndPoint);

        // Close the current connection so the upstream reconnect logic kicks in
        var signal = new QuicCloseItem(QuicCloseKind.MigrationDisallowed) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        _requestStreams.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    /// <summary>
    /// Checks whether the local endpoint of the QUIC connection has changed since the last check.
    /// If it has, raises a <see cref="QuicTransportEvent.ConnectionMigrated"/> event.
    /// </summary>
    internal void CheckForConnectionMigration()
    {
        var currentLocal = _currentConnectionLease?.Handle.LocalEndPoint;
        if (currentLocal is null || _lastLocalEndPoint is null)
        {
            return;
        }

        if (!currentLocal.Equals(_lastLocalEndPoint))
        {
            var old = _lastLocalEndPoint;
            _lastLocalEndPoint = currentLocal;
            _self.Tell(new QuicTransportEvent.ConnectionMigrated(old, currentLocal));
        }
    }

    private void OnEarlyDataRejected(NetworkBuffer buffer)
    {
        _ops.Log.Warning("QuicConnectionStage: 0-RTT early data rejected — re-queuing buffer for retry after full handshake.");

        // Re-queue the rejected buffer into the first request stream context.
        // Early data rejection happens during the initial handshake when typically
        // only a single request stream is in flight.
        foreach (var ctx in _requestStreams.Values)
        {
            ctx.PendingWrites.Enqueue(buffer);
            break;
        }

        // Signal upstream to continue — the buffer will be sent after handshake.
        _ops.OnSignalPullInput();
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        _ops.Log.Warning("QuicConnectionStage: Outbound write failed — {0}", ex.Message);

        var signal = new QuicCloseItem(QuicCloseKind.WriteFailed) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        _requestStreams.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _ops.Log.Warning("QuicConnectionStage: Connection acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        var signal = new QuicCloseItem(QuicCloseKind.AcquisitionFailed) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    private void OnInboundComplete(TlsCloseKind closeKind, long streamId = -1)
    {
        _lastCloseKind = closeKind;

        if (closeKind == TlsCloseKind.CleanClose)
        {
            // Clean close = server FIN on the request stream. The response body is
            // delimited by this FIN. Signal the protocol stage to flush the response
            // without tearing down the QUIC connection (control/encoder streams stay alive).
            _ops.OnPushOutput(new QuicCloseItem(QuicCloseKind.RequestStreamComplete, streamId) { Key = _currentKey });
            _requestStreams.Remove(streamId);
        }
        else
        {
            // Abrupt close = connection-level failure. Tear down everything.
            _ops.OnPushOutput(new QuicCloseItem(QuicCloseKind.ConnectionFailure) { Key = _currentKey });
            _requestStreams.Clear();
            _controlHandle = null;
            _encoderHandle = null;
        }
    }

    private void OnInboundStreamReady(QuicConnectionHandle.InboundStream inbound)
    {
        _activeLeases.Add(inbound.Lease);
        StartQuicInboundPump(inbound.Lease.Handle, inbound.StreamType);
    }

    private void AcquireQuicConnection(QuicOptions options, ConnectItem connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        var acquireTask = QuicConnectionManagerActor.AcquireAsync(
            _quicManagerActor, options, connect.Key, _acquireCts.Token);

        acquireTask.PipeTo(_self, _self,
            connLease => new QuicTransportEvent.ConnectionLeaseAcquired(connLease),
            ex => new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException()));

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    /// <summary>
    /// Opens a new request stream on the existing QUIC connection without
    /// re-establishing control/encoder streams. Used for concurrent and sequential
    /// requests on the same QUIC connection.
    /// </summary>
    private void OpenNewRequestStream(long streamId)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _pendingOpenStreamId = streamId;
        _ = _currentConnectionLease.Handle.OpenStreamAsLeaseAsync(OutputStreamType.Request)
            .PipeTo(_self,
                success: streamLease => new QuicTransportEvent.RequestLeaseAcquired(streamLease, streamId),
                failure: ex => new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException()));
    }

    private void OpenTypedStream(OutputStreamType streamType)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _ = _currentConnectionLease.Handle.OpenStreamAsLeaseAsync(streamType)
            .PipeTo(_self,
                success: lease => new QuicTransportEvent.TypedLeaseAcquired(lease, streamType),
                failure: ex =>
                {
                    _ops.Log.Warning("QuicConnectionStage: Failed to open {0} stream — {1}",
                        streamType, ex.GetBaseException().Message);
                    return new QuicTransportEvent.AcquisitionFailed(ex.GetBaseException());
                });
    }

    private void ReturnConnectionToPool(bool canReuse)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        var lease = _currentConnectionLease;
        _currentConnectionLease = null;
        _quicManagerActor.Tell(new QuicConnectionManagerActor.Release(lease, canReuse));
    }

    private void CleanupTransport()
    {
        _connectionGen++;

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        StopAllQuicPumps();

        foreach (var lease in _activeLeases)
        {
            lease.Dispose();
        }

        _activeLeases.Clear();

        ReturnConnectionToPool(_lastCloseKind == TlsCloseKind.CleanClose);
        _lastCloseKind = TlsCloseKind.CleanClose;

        _requestStreams.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void StartQuicInboundAcceptLoop()
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = new CancellationTokenSource();

        var handle = _currentConnectionLease.Handle;
        var self = _self;
        _ = QuicInboundAcceptLoopAsync(handle, self, _inboundAcceptCts.Token);
    }

    private static async Task QuicInboundAcceptLoopAsync(QuicConnectionHandle handle, IActorRef self,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var inbound = await handle.AcceptInboundStreamAsLeaseAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                inbound?.Lease.Dispose();
                return;
            }

            if (inbound is null)
            {
                continue; // unknown stream type or transient error — try again
            }

            self.Tell(new QuicTransportEvent.InboundStreamReady(inbound));
        }
    }

    private void StartQuicInboundPump(ConnectionHandle handle, InputStreamType streamType, long streamId = -1)
    {
        var cts = new CancellationTokenSource();
        _quicPumpCancellations.Add(cts);

        var ct = cts.Token;
        var reader = handle.InboundReader;
        var key = _currentKey;
        var self = _self;
        var gen = _connectionGen;

        _ = QuicPumpAsync(reader, key, streamType, ct, self, gen, streamId);
    }

    private static async Task QuicPumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        InputStreamType streamType,
        CancellationToken ct,
        IActorRef self,
        int gen,
        long streamId = -1)
    {
        var closeKind = TlsCloseKind.CleanClose;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var chunk))
                {
                    chunk.Key = key;

                    IInputItem outputItem = streamType == InputStreamType.Request
                        ? new Http3InputTaggedItem(chunk, streamType, streamId)
                        : new Http3InputTaggedItem(chunk, streamType);

                    self.Tell(new QuicTransportEvent.InboundData(outputItem, gen));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (AbruptCloseException)
        {
            closeKind = TlsCloseKind.AbruptClose;
        }
        catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
        {
            closeKind = TlsCloseKind.AbruptClose;
        }
        catch (Exception ex)
        {
            self.Tell(new QuicTransportEvent.InboundPumpFailed(ex, streamId));
            return;
        }

        // Only emit close signal for the request stream (per-stream lifecycle)
        if (streamType == InputStreamType.Request)
        {
            self.Tell(new QuicTransportEvent.InboundComplete(closeKind, gen, streamId));
        }
    }

    private void StopAllQuicPumps()
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = null;

        foreach (var cts in _quicPumpCancellations)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _quicPumpCancellations.Clear();
    }

    private void WriteToHandle(ConnectionHandle? handle, NetworkBuffer buffer)
    {
        if (handle is null)
        {
            _ops.Log.Warning("QuicConnectionStage: Data received but no handle available — dropping element.");
            _ops.OnSignalPullInput();
            return;
        }

        _ = handle.OutboundWriter.WriteAsync(buffer)
            .PipeTo(_self,
                success: () => new QuicTransportEvent.OutboundWriteDone(),
                failure: ex => new QuicTransportEvent.OutboundWriteFailed(ex.GetBaseException()));
    }

    private void FlushPendingQuicItems(
        Queue<NetworkBuffer> pending,
        ConnectionHandle handle)
    {
        while (pending.TryDequeue(out var item))
        {
            _ = handle.OutboundWriter.WriteAsync(item)
                .PipeTo(_self,
                    success: () => new QuicTransportEvent.OutboundWriteDone(),
                    failure: ex => new QuicTransportEvent.OutboundWriteFailed(ex.GetBaseException()));
        }

        _ops.OnSignalPullInput();
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);

        // Dispose any pending writes in per-stream contexts before cleanup
        foreach (var ctx in _requestStreams.Values)
        {
            while (ctx.PendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }
        }

        CleanupTransport();
    }
}