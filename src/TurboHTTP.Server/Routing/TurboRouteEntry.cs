namespace TurboHTTP.Server.Routing;

internal sealed record TurboRouteEntry(
    string Method,
    string Pattern,
    Func<TurboHttpContext, Task<HttpResponseMessage>> Handler);
