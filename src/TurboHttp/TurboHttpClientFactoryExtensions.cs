using System;

namespace TurboHttp;

public static class TurboHttpClientFactoryExtensions
{
    public static ITurboHttpClient CreateClient(this ITurboHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient(string.Empty);
    }
}