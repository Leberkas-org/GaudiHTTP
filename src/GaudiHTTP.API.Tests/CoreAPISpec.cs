using PublicApiGenerator;
using GaudiHTTP.Client;

namespace GaudiHTTP.API.Tests;

public class CoreAPISpec
{
    private static readonly ApiGeneratorOptions ApiOptions = new()
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
        return Verify(typeof(T).Assembly.GeneratePublicApi(ApiOptions));
    }

    [Fact]
    public Task ApproveCore()
    {
        return VerifyAssembly<IGaudiHttpClient>();
    }
}