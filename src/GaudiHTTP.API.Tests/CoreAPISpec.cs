using PublicApiGenerator;
using GaudiHTTP.Client;

namespace GaudiHTTP.API.Tests;

public class CoreAPISpec
{
    private static ApiGeneratorOptions MakeApiOptions() => new()
    {
        ExcludeAttributes =
        [
            "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
            "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
            "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
        ]
    };

    private static Task VerifyAssembly<T>()
    {
        return Verify(typeof(T).Assembly.GeneratePublicApi(MakeApiOptions()));
    }

    [Fact(Timeout = 5000)]
    public Task ApproveCore()
    {
        return VerifyAssembly<IGaudiHttpClient>();
    }
}