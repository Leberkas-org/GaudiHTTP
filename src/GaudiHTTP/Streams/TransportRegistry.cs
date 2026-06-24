using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace GaudiHTTP.Streams;

internal sealed class TransportRegistry
{
    private readonly Dictionary<Version, Flow<ITransportOutbound, ITransportInbound, NotUsed>> _transports = new();

    public TransportRegistry Register(Version version, Flow<ITransportOutbound, ITransportInbound, NotUsed> flow)
    {
        _transports[version] = flow ?? throw new ArgumentNullException(nameof(flow));
        return this;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Get(Version version)
    {
        if (_transports.TryGetValue(version, out var flow))
        {
            return flow;
        }

        throw new InvalidOperationException(
            $"No transport factory registered for HTTP version {version}. " +
            $"Registered versions: {string.Join(", ", _transports.Keys)}");
    }
}