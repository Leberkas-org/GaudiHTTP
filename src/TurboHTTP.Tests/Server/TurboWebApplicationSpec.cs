using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Tests.Server;

public sealed class TurboWebApplicationSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_with_instance_should_register_same_instance()
    {
        var options = new TurboServerOptions();
        options.HandlerTimeout = TimeSpan.FromSeconds(99);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboKestrel(options);
        var host = builder.Build();

        var resolved = host.Services.GetRequiredService<TurboServerOptions>();
        Assert.Same(options, resolved);
        Assert.Equal(TimeSpan.FromSeconds(99), resolved.HandlerTimeout);
    }
}
