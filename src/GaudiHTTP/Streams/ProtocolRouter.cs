using GaudiHTTP.Server;

namespace GaudiHTTP.Streams;

internal static class ProtocolRouter
{
    internal static IServerProtocolEngine ResolveEngine(Version version, GaudiServerOptions options)
    {
        return version switch
        {
            { Major: 1, Minor: 0 } => new Http10ServerEngine(options),
            { Major: 1, Minor: 1 } => new Http11ServerEngine(options),
            { Major: 2, Minor: 0 } => new Http20ServerEngine(options),
            { Major: 3, Minor: 0 } => new Http30ServerEngine(options),
            _ => new Http11ServerEngine(options)
        };
    }

    internal static IServerProtocolEngine ResolveNegotiating(GaudiServerOptions options,
        HttpProtocols allowedProtocols = HttpProtocols.Http1AndHttp2)
    {
        return new NegotiatingServerEngine(options, allowedProtocols);
    }
}