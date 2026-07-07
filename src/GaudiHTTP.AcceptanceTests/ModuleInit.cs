using System.Runtime.CompilerServices;
using Servus.Akka.Transport;

namespace GaudiHTTP.AcceptanceTests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        WireBuffer.ConfigureWrapperPool(0);
    }
}
