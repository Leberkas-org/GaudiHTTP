using System.Runtime.CompilerServices;

namespace GaudiHTTP.IntegrationTests.Client;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        ThreadPool.SetMinThreads(512, 512);
    }
}