using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal sealed class TransportRegistry
{
    private readonly Dictionary<Version, ITransportFactory> _transports = new();

    public TransportRegistry Register(Version version, ITransportFactory factory)
    {
        _transports[version] = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Get(Version version)
    {
        if (_transports.TryGetValue(version, out var factory))
        {
            return factory.Create();
        }

        throw new InvalidOperationException(
            $"No transport factory registered for HTTP version {version}. " +
            $"Registered versions: {string.Join(", ", _transports.Keys)}");
    }
}