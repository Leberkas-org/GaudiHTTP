namespace Servus.Akka.Transport;

public interface ITransportOutbound;

public sealed record ConnectTransport(TransportOptions Options) : ITransportOutbound;

public sealed record DisconnectTransport(DisconnectReason Reason) : ITransportOutbound;

public sealed record OpenStream(long StreamId, StreamDirection Direction) : ITransportOutbound;

public sealed record CloseStream(long StreamId) : ITransportOutbound;

public sealed record ConnectionReuse(PoolAction Action) : ITransportOutbound;

public sealed record TransportData(TransportBuffer Buffer) : ITransportOutbound, ITransportInbound;
