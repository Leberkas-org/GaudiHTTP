using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public interface ITurboEndpointRouteBuilder
{
    IServiceProvider ServiceProvider { get; }

    [Obsolete("Use extension methods to register routes. Direct RouteTable access will be removed in 2.0.")]
    TurboRouteTable RouteTable { get; }
}
