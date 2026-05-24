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

[CollectionDefinition("Auth")]
public sealed class AuthIntegrationCollection;

[CollectionDefinition("Cache")]
public sealed class CacheIntegrationCollection;

[CollectionDefinition("Compression")]
public sealed class CompressionIntegrationCollection;

[CollectionDefinition("Cookies")]
public sealed class CookiesIntegrationCollection;

[CollectionDefinition("Interaction")]
public sealed class InteractionIntegrationCollection;

[CollectionDefinition("Range")]
public sealed class RangeIntegrationCollection;

[CollectionDefinition("Redirect")]
public sealed class RedirectIntegrationCollection;

[CollectionDefinition("Sse")]
public sealed class SseIntegrationCollection;

[CollectionDefinition("Streaming")]
public sealed class StreamingIntegrationCollection;

[CollectionDefinition("Timing")]
public sealed class TimingIntegrationCollection;