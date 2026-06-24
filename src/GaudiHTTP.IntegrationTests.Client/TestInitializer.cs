using System.Runtime.CompilerServices;

namespace GaudiHTTP.IntegrationTests.Client;

internal static class TestInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ThreadPool.SetMinThreads(256, 256);
    }
}
