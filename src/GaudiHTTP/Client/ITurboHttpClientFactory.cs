namespace TurboHTTP.Client;

/// <summary>
/// Creates named <see cref="ITurboHttpClient"/> instances.
/// Shared runtime internals may be reused per name, while each created client handle
/// maintains its own mutable request options (BaseAddress, timeout, version defaults, headers).
/// </summary>
public interface ITurboHttpClientFactory
{
    /// <summary>Creates (or retrieves) an <see cref="ITurboHttpClient"/> for the given <paramref name="name"/>.</summary>
    ITurboHttpClient CreateClient(string name);
}
