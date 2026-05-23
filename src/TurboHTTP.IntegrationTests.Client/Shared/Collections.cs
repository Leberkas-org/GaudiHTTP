using TurboHTTP.Tests.Shared;
using TurboHTTP.IntegrationTests.Client.Shared;

[assembly: AssemblyFixture(typeof(ServerContainerFixture))]
[assembly: AssemblyFixture(typeof(ActorSystemFixture))]

namespace TurboHTTP.IntegrationTests.Client.Shared;

[CollectionDefinition("H10")]
public sealed class H10IntegrationCollection;

[CollectionDefinition("H11")]
public sealed class H11IntegrationCollection;

[CollectionDefinition("H2")]
public sealed class H2IntegrationCollection;

[CollectionDefinition("H3")]
public sealed class H3IntegrationCollection;

[CollectionDefinition("Features")]
public sealed class FeaturesIntegrationCollection;