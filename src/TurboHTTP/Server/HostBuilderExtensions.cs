using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Routing;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public static class HostBuilderExtensions
{
    public static TurboWebApplication Build(this HostApplicationBuilder builder)
    {
        if (builder.Services.All(sd => sd.ServiceType != typeof(TurboServerOptions)))
        {
            builder.Services.AddTurboKestrel();
        }

        var host = builder.Build();
        var routeTable = host.Services.GetRequiredService<TurboRouteTable>();
        var pipelineBuilder = host.Services.GetRequiredService<TurboPipelineBuilder>();

        return new TurboWebApplication(host, routeTable, pipelineBuilder);
    }
}
