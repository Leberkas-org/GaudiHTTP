namespace TurboHTTP.Server.Routing;

internal sealed record TurboRouteEntry(
    HttpMethod Method,
    string Pattern,
    IRouteDispatcher Dispatcher);
