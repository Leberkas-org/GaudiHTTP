using Servus.Akka.Transport;

namespace GaudiHTTP.Protocol;

internal sealed record TransportDataFlushed(int Bytes) : ITransportInbound;
