namespace TurboHTTP.Server.Routing;

internal interface IRouteDispatcher
{
    Task DispatchAsync(TurboHttpContext context, CancellationToken ct);
}
