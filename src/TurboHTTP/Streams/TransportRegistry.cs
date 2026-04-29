using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal sealed class TransportRegistry
{
    private readonly Dictionary<Version, Func<Flow<ITransportOutbound, ITransportInbound, NotUsed>>> _transports = new();

    public TransportRegistry Register(Version version,
        Func<Flow<ITransportOutbound, ITransportInbound, NotUsed>> factory)
    {
        _transports[version] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Get(Version version)
    {
        if (_transports.TryGetValue(version, out var factory))
        {
            return factory();
        }

        throw new InvalidOperationException(
            $"No transport factory registered for HTTP version {version}. " +
            $"Registered versions: {string.Join(", ", _transports.Keys)}");
    }
}
