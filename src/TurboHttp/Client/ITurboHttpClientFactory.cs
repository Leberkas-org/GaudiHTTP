using System;

namespace TurboHttp.Client;

/// <summary>
/// Creates <see cref="ITurboHttpClient"/> instances with optional per-call configuration overrides.
/// </summary>
public interface ITurboHttpClientFactory
{
    /// <summary>
    /// Creates a new <see cref="ITurboHttpClient"/> instance, optionally overriding
    /// individual <see cref="TurboClientOptions"/> values for this client.
    /// </summary>
    /// <param name="configure">Optional delegate to override option values for this client only.</param>
    /// <returns>A configured <see cref="ITurboHttpClient"/> instance.</returns>
    ITurboHttpClient CreateClient(Action<TurboClientOptions>? configure = null);
}