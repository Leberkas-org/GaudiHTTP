using GaudiHTTP.IntegrationTests.Server.Shared;

[assembly: AssemblyFixture(typeof(TurboServerFixture))]

namespace GaudiHTTP.IntegrationTests.Server.Shared;

[CollectionDefinition("ServerStress", DisableParallelization = true)]
public sealed class ServerStressCollection;

[CollectionDefinition("Infrastructure", DisableParallelization = true)]
public sealed class InfrastructureCollection;
