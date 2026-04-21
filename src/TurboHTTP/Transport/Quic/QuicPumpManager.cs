using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Manages the lifecycle of QUIC inbound stream pumps — start, cancel, and the async read loops
/// that marshal data from QUIC streams into StageActorRef messages.
/// Extracted from <see cref="QuicTransportStateMachine"/> for single-responsibility.
/// </summary>
internal sealed class QuicPumpManager
{
    private readonly IActorRef _self;
    private readonly List<CancellationTokenSource> _pumpCancellations = [];
    private CancellationTokenSource? _inboundAcceptCts;

    public QuicPumpManager(IActorRef self)
    {
        _self = self;
    }

    /// <summary>
    /// Starts a background pump that reads from the given handle's inbound channel
    /// and marshals each chunk as a <see cref="InboundData"/> message.
    /// </summary>
    public void StartInboundPump(ConnectionHandle handle, long streamTypeValue,
        RequestEndpoint key, int connectionGen, long streamId)
    {
        var cts = new CancellationTokenSource();
        _pumpCancellations.Add(cts);

        _ = PumpAsync(handle.InboundReader, key, streamTypeValue, cts.Token, _self, connectionGen, streamId);
    }

    /// <summary>
    /// Starts the server-initiated inbound stream accept loop for the given QUIC connection.
    /// </summary>
    public void StartInboundAcceptLoop(QuicConnectionHandle connectionHandle)
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = new CancellationTokenSource();

        _ = AcceptLoopAsync(connectionHandle, _self, _inboundAcceptCts.Token);
    }

    /// <summary>
    /// Cancels all active inbound pumps and the accept loop.
    /// </summary>
    public void StopAll()
    {
        _inboundAcceptCts?.Cancel();
        _inboundAcceptCts?.Dispose();
        _inboundAcceptCts = null;

        foreach (var cts in _pumpCancellations)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _pumpCancellations.Clear();
    }

    private static async Task AcceptLoopAsync(QuicConnectionHandle handle, IActorRef self,
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

            self.Tell(new InboundStreamReady(inbound));
        }
    }

    private static async Task PumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        long streamTypeValue,
        CancellationToken ct,
        IActorRef self,
        int gen,
        long streamId)
    {
        var closeKind = TlsCloseKind.CleanClose;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var chunk))
                {
                    chunk.Key = key;

                    if (chunk is Http3NetworkBuffer h3Buf)
                    {
                        h3Buf.StreamTypeValue = streamTypeValue;
                        h3Buf.StreamId = streamId;
                    }

                    self.Tell(new InboundData(chunk, gen));
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
            self.Tell(new InboundPumpFailed(ex, streamId));
            return;
        }

        if (streamTypeValue < 0)
        {
            self.Tell(new InboundComplete(closeKind, gen, streamId));
        }
    }
}
