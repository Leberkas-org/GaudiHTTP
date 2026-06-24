using Microsoft.Extensions.DependencyInjection;

namespace GaudiHTTP.Client;

internal sealed class GaudiHttpClientBuilder(string name, IServiceCollection services) : IGaudiHttpClientBuilder
{
    public string Name { get; } = name;
    public IServiceCollection Services { get; } = services;
}
