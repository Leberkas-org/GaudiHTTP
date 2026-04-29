namespace Servus.Akka.Transport.Tcp;

internal sealed class TcpConnectionFactory : IConnectionFactory<ConnectionLease>
{
    public static readonly TcpConnectionFactory Instance = new();

    public async Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct)
    {
        IAsyncDisposable provider;
        Stream stream;

        if (options is TlsTransportOptions tlsOpts)
        {
            var tlsProvider = new TlsClientProvider(tlsOpts);
            provider = tlsProvider;
            stream = await tlsProvider.GetStreamAsync(ct).ConfigureAwait(false);
        }
        else if (options is TcpTransportOptions tcpOpts)
        {
            var tcpProvider = new TcpClientProvider(tcpOpts);
            provider = tcpProvider;
            stream = await tcpProvider.GetStreamAsync(ct).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException($"Unsupported options type: {options.GetType()}", nameof(options));
        }

        var state = new ClientState(stream, PipeMode.Bidirectional);
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(state.OutboundWriter, state.InboundReader, cts.Token);
        var lease = new ConnectionLease(handle, state, cts);

        _ = ClientByteMover.MoveStreamToChannel(state, () => { }, cts.Token);
        _ = ClientByteMover.MoveChannelToStream(state, () => { }, cts.Token);

        return lease;
    }
}
