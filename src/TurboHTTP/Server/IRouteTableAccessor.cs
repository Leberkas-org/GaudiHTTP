using TurboHTTP.Routing;

namespace TurboHTTP.Server;

internal interface IRouteTableAccessor
{
    TurboRouteTable RouteTable { get; }
}
