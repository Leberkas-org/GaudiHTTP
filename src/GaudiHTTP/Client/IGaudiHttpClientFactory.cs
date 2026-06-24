namespace GaudiHTTP.Client;

/// <summary>
/// Creates named <see cref="IGaudiHttpClient"/> instances.
/// Shared runtime internals may be reused per name, while each created client handle
/// maintains its own mutable request options (BaseAddress, timeout, version defaults, headers).
/// </summary>
public interface IGaudiHttpClientFactory
{
    /// <summary>Creates (or retrieves) an <see cref="IGaudiHttpClient"/> for the given <paramref name="name"/>.</summary>
    IGaudiHttpClient CreateClient(string name);
}
