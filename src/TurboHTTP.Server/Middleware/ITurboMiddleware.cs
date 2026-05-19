namespace TurboHTTP.Server.Middleware;

public interface ITurboMiddleware
{
    Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next);
}