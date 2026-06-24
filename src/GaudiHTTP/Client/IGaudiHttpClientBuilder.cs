using Microsoft.Extensions.DependencyInjection;

namespace GaudiHTTP.Client;

/// <summary>
/// Configuration builder for a named GaudiHttp client.
/// Returned by <c>AddGaudiHttpClient</c> extension methods and passed to fluent configuration
/// extensions such as <c>WithCookies</c>, <c>WithRetry</c>, and <c>AddHandler</c>.
/// </summary>
public interface IGaudiHttpClientBuilder
{
    /// <summary>Gets the logical name of the client being configured.</summary>
    string Name { get; }
    /// <summary>Gets the service collection the client registrations are added to.</summary>
    IServiceCollection Services { get; }
}
