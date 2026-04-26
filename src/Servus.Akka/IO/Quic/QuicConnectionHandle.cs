using System.Runtime.Versioning;

// QUIC APIs are platform-guarded; usage is gated at runtime via QuicOptions.
#pragma warning disable CA1416

namespace Servus.Akka.IO.Quic;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionHandle : IAsyncDisposable
{
    /// <summary>
    /// Notification produced when the inbound-accept loop receives a server-initiated stream.
    /// Equivalent to the old <c>QuicConnectionManager.InboundStream</c> record.
    /// </summary>
    public sealed record InboundStream(ConnectionLease Lease, long StreamTypeValue, long StreamId);

    private readonly IClientProvider _provider;
    private readonly QuicOptions _options;

    public QuicConnectionHandle(IClientProvider provider, QuicOptions options, RequestEndpoint key)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        _provider = provider;
        _options = options;
        Key = key;
    }

    /// <summary>The connection target identity (scheme, host, port, version).</summary>
    public RequestEndpoint Key { get; }

    /// <summary>Gets the local endpoint of the underlying QUIC connection, or <see langword="null"/> if not yet connected.</summary>
    public System.Net.EndPoint? LocalEndPoint => _provider.LocalEndPoint;

    /// <summary>
    /// Opens a typed QUIC stream and returns a <see cref="ConnectionLease"/> for it.
    /// </summary>
    public async Task<ConnectionLease> OpenStreamAsLeaseAsync(
        bool bidirectional, CancellationToken ct = default)
    {
        var (direction, streamFactory) = bidirectional
            ? (StreamDirection.Bidirectional, (Func<CancellationToken, Task<Stream>>)_provider.GetStreamAsync)
            : (StreamDirection.WriteOnly, _provider.GetUnidirectionalStreamAsync);
        var stream = await streamFactory(ct).ConfigureAwait(false);
        return CreateStreamLease(stream, direction);
    }

    /// <summary>
    /// Accepts one server-initiated inbound stream, reads the HTTP/3 stream-type varint,
    /// and wraps it in an <see cref="InboundStream"/>. Returns <c>null</c> when the stream
    /// is unknown or on any error — callers loop until cancelled.
    /// </summary>
    public async Task<InboundStream?> AcceptInboundStreamAsLeaseAsync(CancellationToken ct = default)
    {
        Stream stream;
        try
        {
            stream = await _provider.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null; // connection may be dead — caller decides whether to retry
        }

        // Read the stream-type varint (first byte is sufficient for the leading octet decode)
        var typeBuf = new byte[8];
        int bytesRead;
        try
        {
            bytesRead = await stream.ReadAsync(typeBuf.AsMemory(0, 1), ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        if (bytesRead == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        long streamTypeValue = typeBuf[0];

        var lease = CreateStreamLease(stream, StreamDirection.ReadOnly);
        var streamId = stream is System.Net.Quic.QuicStream quicStream
            ? quicStream.Id
            : -1;
        return new InboundStream(lease, streamTypeValue, streamId);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>
    /// Creates a <see cref="ConnectionLease"/> for an already-opened QUIC stream,
    /// complete with channels and ByteMover pump tasks.
    /// </summary>
    private ConnectionLease CreateStreamLease(Stream stream, StreamDirection direction)
    {
        Action? onWritesComplete = null;
        if (direction == StreamDirection.Bidirectional && stream is System.Net.Quic.QuicStream qs)
        {
            onWritesComplete = () =>
            {
                try
                {
                    qs.CompleteWrites();
                }
                catch
                {
                    /* stream may already be closed — ignore */
                }
            };
        }

        var state = new ClientState(stream, direction)
        {
            OnWritesComplete = onWritesComplete,
        };

        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter,
            state.InboundReader,
            Key);

        var lease = new ConnectionLease(handle, state);

        if (direction != StreamDirection.WriteOnly)
        {
            _ = ClientByteMover.MoveStreamToChannel(state, static () => { }, lease.Token);
        }

        if (direction != StreamDirection.ReadOnly)
        {
            _ = ClientByteMover.MoveChannelToStream(state, static () => { }, lease.Token);
        }

        return lease;
    }
}