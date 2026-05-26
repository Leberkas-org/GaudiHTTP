using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public static class TurboHttpContextExtensions
{
    public static TurboEndpointMetadata? GetEndpointMetadata(this TurboHttpContext context)
        => context.EndpointMetadata;

    public static bool HasEndpointMetadata<T>(this TurboHttpContext context) where T : class
        => context.EndpointMetadata?.HasMetadata<T>() == true;
}
