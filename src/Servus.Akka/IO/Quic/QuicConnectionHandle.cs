using System.Runtime.Versioning;

namespace Servus.Akka.IO.Quic;

public sealed record InboundStream(QuicStreamLease Lease, long StreamTypeValue, long StreamId);

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
public sealed class QuicConnectionHandle : IAsyncDisposable
{
    

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

    public RequestEndpoint Key { get; }

    public System.Net.EndPoint? LocalEndPoint => _provider.LocalEndPoint;

    public async Task<QuicStreamLease> OpenStreamAsLeaseAsync(
        bool bidirectional, CancellationToken ct = default)
    {
        var (direction, streamFactory) = bidirectional
            ? (StreamDirection.Bidirectional, (Func<CancellationToken, Task<Stream>>)_provider.GetStreamAsync)
            : (StreamDirection.WriteOnly, _provider.GetUnidirectionalStreamAsync);
        var stream = await streamFactory(ct).ConfigureAwait(false);
        return CreateStreamLease(stream, direction);
    }

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
            return null;
        }

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

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private QuicStreamLease CreateStreamLease(Stream stream, StreamDirection direction)
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
                catch (Exception ex)
                {
                    Diagnostics.ServusTrace.Connection.Debug(null,
                        "CompleteWrites failed for QUIC stream (already closed): {0}", ex.Message);
                }
            };
        }

        var handle = new StreamHandle(stream, Key, onWritesComplete);
        return new QuicStreamLease(handle);
    }
}