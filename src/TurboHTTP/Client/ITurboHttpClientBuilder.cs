using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Client;

/// <summary>
/// Configuration builder for a named TurboHttp client.
/// Returned by <c>AddTurboHttpClient</c> extension methods and passed to fluent configuration
/// extensions such as <c>WithCookies</c>, <c>WithRetry</c>, and <c>AddHandler</c>.
/// </summary>
public interface ITurboHttpClientBuilder
{
    /// <summary>Gets the logical name of the client being configured.</summary>
    string Name { get; }
    /// <summary>Gets the service collection the client registrations are added to.</summary>
    IServiceCollection Services { get; }
}
