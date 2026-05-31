# Changelog

## [4.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0...v4.0.0) (2026-05-31)


### ⚠ BREAKING CHANGES

* delete RequestContext, TurboHttpContext, RoutingStage, and all custom routing types
* wire IHttpApplication through actors to ApplicationBridgeStage
* rewrite ApplicationBridgeStage as generic with IHttpApplication<TContext>
* delete app-framework layer
* implement TurboServer as IServer replacement
* remove ITurbo*Feature interfaces, use IHttp*Feature only
* replace FrameworkReference with targeted NuGet packages
* pipeline
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder()
* **server:** rewrite TurboWebApplication with static factories and interfaces

* replace FrameworkReference with targeted NuGet packages ([bd25a46](https://github.com/Leberkas-org/TurboHTTP/commit/bd25a4680cf9b62e39bd0f114b2f04e0fd6b750d))


### Features

* add AddTurboServerInstrumentation registration methods ([4d156fb](https://github.com/Leberkas-org/TurboHTTP/commit/4d156fbf2d5e886f8a55f0f20cdc130ebddd234d))
* add CLI orchestration for stress benchmarks ([df87944](https://github.com/Leberkas-org/TurboHTTP/commit/df879440bfb11270c087c42cba60d6503a022498))
* add code coverage to integration tests ([2f1d7bc](https://github.com/Leberkas-org/TurboHTTP/commit/2f1d7bcff531589e13372554a70bedcfbc479f22))
* add connection metrics and tracing to ListenerActor ([9ad8c7a](https://github.com/Leberkas-org/TurboHTTP/commit/9ad8c7a41fdc9b8b4d369bd8017f3d5e88c4349c))
* add fast-path slots for IHttpMaxRequestBodySizeFeature and IHttpBodyControlFeature ([ccfde96](https://github.com/Leberkas-org/TurboHTTP/commit/ccfde961fd515e1f7eaa543904f8368bb44c98cc))
* add FeatureCollectionFactory returning IFeatureCollection ([71110c6](https://github.com/Leberkas-org/TurboHTTP/commit/71110c69eddf29485d9fbeaab071ee4473f9f3c2))
* add four stress scenarios (slow-handler, connection-storm, body-flood, memory-endurance) ([727f28a](https://github.com/Leberkas-org/TurboHTTP/commit/727f28a5041d013e91d7a9f91af6f2416ba8e66d))
* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add Host property to TurboWebApplicationBuilder for full builder parity ([7977263](https://github.com/Leberkas-org/TurboHTTP/commit/79772639541da397ddf54845ef1884634e39b5ac))
* add IHttpMaxRequestBodySizeFeature and IHttpBodyControlFeature implementations ([2e6c426](https://github.com/Leberkas-org/TurboHTTP/commit/2e6c426b1dc1e915463e1aac8af3031332678373))
* add LoadGenerator with concurrent worker loops ([5b50b78](https://github.com/Leberkas-org/TurboHTTP/commit/5b50b78839acb2e0cf17147769cc8ebe53f3048f))
* Add metadata support to TurboRouteHandlerBuilder and TurboRouteGroupBuilder ([ca7ee9c](https://github.com/Leberkas-org/TurboHTTP/commit/ca7ee9c17e5aa69ab1d4c85aa0ffa4076518d796))
* add MetricsCollector with per-second time-series aggregation ([a09b2a4](https://github.com/Leberkas-org/TurboHTTP/commit/a09b2a421bbe5958e6e12678663d3414ace6ea2f))
* add OTel-standard server metric instruments ([602eeb3](https://github.com/Leberkas-org/TurboHTTP/commit/602eeb3c0749acf7d26a77cb32a493ee9a65d9bd))
* add own collection interfaces and dual-implement on adapter classes ([7944d50](https://github.com/Leberkas-org/TurboHTTP/commit/7944d50a9ccf7f868e3aebcc85e004a4c6f8a771))
* add pipeline metrics and backpressure events to ApplicationBridgeStage ([b84bf7a](https://github.com/Leberkas-org/TurboHTTP/commit/b84bf7ad64afc25e00b0f5cd87442f3ec38d389e))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add protocol negotiation metrics to ConnectionActor ([3879a89](https://github.com/Leberkas-org/TurboHTTP/commit/3879a8915d0ec3e5d023f12ab051db68438a15d2))
* add request metrics and tracing to HttpConnectionServerStageLogic ([6049d29](https://github.com/Leberkas-org/TurboHTTP/commit/6049d29b368e8ea5c19032d4e3abe04c934cc93f))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* add RequestTimestamp and RequestActivity to TurboFeatureCollection ([bc1e98a](https://github.com/Leberkas-org/TurboHTTP/commit/bc1e98a12eabf20221df7d960951756b98220ca9))
* add ResponsePipeWriter for writer-side header commit ([93d1bb9](https://github.com/Leberkas-org/TurboHTTP/commit/93d1bb9d13336c1fefced770912931e35e7074d4))
* Add server instrumentation methods ([251fcf2](https://github.com/Leberkas-org/TurboHTTP/commit/251fcf21327e0e4b8312e34f6dd38caaf99ee13b))
* add server-side Activity lifecycle (connection + request tracing) ([6989cfe](https://github.com/Leberkas-org/TurboHTTP/commit/6989cfe13aef5cf5ed21264379553035bab4ae82))
* add ServerHarness for Turbo/Kestrel lifecycle ([95cf049](https://github.com/Leberkas-org/TurboHTTP/commit/95cf0496b5f16a579bd4102bc0f704435c0b6c09))
* add Servus.Akka.AspNetCore with AkkaResults and MapEntity ([739a1a5](https://github.com/Leberkas-org/TurboHTTP/commit/739a1a581cdb97b1547fc9e1b4a4246a398671a1))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/Leberkas-org/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/Leberkas-org/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add standalone ITurbo*Feature interfaces for ASP.NET Core decoupling ([f420515](https://github.com/Leberkas-org/TurboHTTP/commit/f4205154c11f85ee6ee95b483305c53a9688e0f2))
* add StressReport and JsonExporter for benchmark output ([855ed9d](https://github.com/Leberkas-org/TurboHTTP/commit/855ed9db414c37dbd9a23e72e6efd9e3a024243f))
* add turbo.server.* differenzierung metric instruments ([a826af1](https://github.com/Leberkas-org/TurboHTTP/commit/a826af13a351d2ac7f273b5c9cca64775734b75f))
* add TurboEndpointMetadata type with marker interfaces ([9eab9c8](https://github.com/Leberkas-org/TurboHTTP/commit/9eab9c86ecfa5b59c681c3dabd480f14641105b1))
* add TurboServer vs Kestrel server benchmarks ([a5eba3e](https://github.com/Leberkas-org/TurboHTTP/commit/a5eba3e0389fd8f602796014056f0100a644dafc))
* add TurboServerLimits, Listen(string url), ConfigureEndpointDefaults ([0a63b9d](https://github.com/Leberkas-org/TurboHTTP/commit/0a63b9dce780956cb7cd63ebc25b3d943fbd214e))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* **body:** add GetBodyStream() to LineBased IBodyDecoder implementations ([f0578b1](https://github.com/Leberkas-org/TurboHTTP/commit/f0578b1cf0804fb5b2fb3e06bbd6d06a528c2965))
* **body:** add Stream-based Start/Create overloads to LineBased body encoders ([49c2e42](https://github.com/Leberkas-org/TurboHTTP/commit/49c2e42bf1cd139231107e42d18d3e7ea1ee8eb9))
* **body:** add Stream-based Start/Create overloads to Multiplexed body encoders ([0a97365](https://github.com/Leberkas-org/TurboHTTP/commit/0a97365554ec860db479a2adf4c655ca77f8e025))
* **ci:** Add docs build workflow ([1741c44](https://github.com/Leberkas-org/TurboHTTP/commit/1741c44f4a0009cf96f040319eb199aa588fae69))
* **client:** round-robin connection routing in GroupByRequestEndpointStage ([b180d21](https://github.com/Leberkas-org/TurboHTTP/commit/b180d21f5343e63e839eade8e018d812f491ba03))
* complete ServerContextFactory migration to TurboFeatureCollection ([e9d48aa](https://github.com/Leberkas-org/TurboHTTP/commit/e9d48aab1869ab58de4732665d209d14a9469722))
* **context:** add WhenHeadersReady signal to TurboHttpResponseBodyFeature ([e604a6c](https://github.com/Leberkas-org/TurboHTTP/commit/e604a6cf146479f0c6abba289e5d5bb32f8117a8))
* **context:** create TurboRequestBodyFeature and align response body with Kestrel ([4738712](https://github.com/Leberkas-org/TurboHTTP/commit/473871230437f749c8119e6fd8459ac2ae294768))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* deprecate MapTurbo*/UseTurbo* WebApplication extensions for 2.0 removal ([f81b3ed](https://github.com/Leberkas-org/TurboHTTP/commit/f81b3ed60380ecb569a540ad8a34db56c6217823))
* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* dual-implement all feature classes with both ASP.NET Core and own interfaces ([7bdaf92](https://github.com/Leberkas-org/TurboHTTP/commit/7bdaf9201b67108b740587110308d072ca0d63f2))
* **e2e:** add End2EndSpecBase infrastructure for TurboHTTP client-server tests ([9051d71](https://github.com/Leberkas-org/TurboHTTP/commit/9051d715944b82cf8eb3a0ae778a11951921795e))
* **e2e:** add H1.1 StreamingSpec and PipeliningSpec ([8b5a32b](https://github.com/Leberkas-org/TurboHTTP/commit/8b5a32b7406cbbe291611ce18aa63500200ab31d))
* **e2e:** add H2 MultiplexingSpec, FlowControlSpec, UpgradeSpec ([156b9e1](https://github.com/Leberkas-org/TurboHTTP/commit/156b9e110444a6daf7b9a44b8b9ffe3eee7b8740))
* **e2e:** add H3 MultiplexingSpec ([85af05e](https://github.com/Leberkas-org/TurboHTTP/commit/85af05e5d4f5dfb15e2b291fb87cf60ab36b676f))
* **e2e:** add LargePayloadSpecs for all protocols ([0361829](https://github.com/Leberkas-org/TurboHTTP/commit/0361829b0dfc22eb49cbec843ef1834983d891f5))
* **e2e:** add ResilienceSpecs for all protocols ([43719c0](https://github.com/Leberkas-org/TurboHTTP/commit/43719c069d2acfe316bfeee4cd79ee57f8747e5b))
* **e2e:** add RoundtripSpecs for H1.0, H1.1, H2, H3 ([58b8a9c](https://github.com/Leberkas-org/TurboHTTP/commit/58b8a9ccf25e8a8bd4c9a915df469db9f9c60efb))
* **encoders:** add TurboHttpContext overloads to all 4 server encoders ([d314a86](https://github.com/Leberkas-org/TurboHTTP/commit/d314a86e611c3f7d60722d062e9d850fd7592538))
* **features:** add ITlsHandshakeFeature interface and implementation ([4207222](https://github.com/Leberkas-org/TurboHTTP/commit/42072228166b95f38017a154e6071a277065d75a))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **h2:** implement server-side HTTP/2 response trailers ([fc4df62](https://github.com/Leberkas-org/TurboHTTP/commit/fc4df6242b90facf9ebd010672af2450519b447e))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/Leberkas-org/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/Leberkas-org/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/Leberkas-org/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/Leberkas-org/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/Leberkas-org/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/Leberkas-org/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/Leberkas-org/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/Leberkas-org/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/Leberkas-org/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/Leberkas-org/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))
* **http10:** add Http10ServerDecoder.GetRequestFeature() ([0804b1a](https://github.com/Leberkas-org/TurboHTTP/commit/0804b1a4158582c3c4f47cade4a214f352677a7b))
* **http11:** add h2c upgrade detection with IProtocolSwitchCapable signaling ([1e60d31](https://github.com/Leberkas-org/TurboHTTP/commit/1e60d3163e1bbadf92c67491799173c4704a2f4a))
* **http11:** start body encoder in server OnResponse for streaming support ([984c503](https://github.com/Leberkas-org/TurboHTTP/commit/984c503166845e226efabe68beb8233d665b954e))
* **http2:** add Http2ServerDecoder.DecodeHeadersToFeature() ([2f990ce](https://github.com/Leberkas-org/TurboHTTP/commit/2f990cebdc0abbd9dba607485b4d937b46a663fe))
* **http3:** add Http3ServerDecoder.DecodeHeadersToFeature() ([7d6a356](https://github.com/Leberkas-org/TurboHTTP/commit/7d6a35658dd884eb510e131756e2580ab65a6cad))
* **http3:** Stream 3 is unidirectional by default ([0609bd2](https://github.com/Leberkas-org/TurboHTTP/commit/0609bd261897bacf59bf70277193225db4eb782a))
* implement TurboServer as IServer replacement ([ebfb865](https://github.com/Leberkas-org/TurboHTTP/commit/ebfb865c58457644c64d153a0e626c09ef102e0f))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **lifecycle:** extract TLS metadata ([07fd711](https://github.com/Leberkas-org/TurboHTTP/commit/07fd71114421703629103b74ddbe6639cf20cb15))
* migrate protocol layer from ASP.NET Core feature interfaces to own ITurbo* interfaces ([1b920c0](https://github.com/Leberkas-org/TurboHTTP/commit/1b920c0a67b15b520c01b009af5d353de131a1df))
* **protocol:** add HeaderRouter.ApplyToHeaderDictionary for flat header writing ([a839e74](https://github.com/Leberkas-org/TurboHTTP/commit/a839e74815d480e39d8ad763328eb815f6acb094))
* **protocol:** add ProtocolNegotiatingStateMachine with ALPN and preface detection ([befd348](https://github.com/Leberkas-org/TurboHTTP/commit/befd3489bd741ad2412b4df46f62865031d588ea))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* register body size and body control features in FeatureCollectionFactory ([8850337](https://github.com/Leberkas-org/TurboHTTP/commit/88503375ed4abca5aaed8ebbbe6d7eb766987798))
* **routing:** add Count property to EntityResponseMapperCollection ([fa65d3d](https://github.com/Leberkas-org/TurboHTTP/commit/fa65d3db0f5bd70bf9a3fa828266e56fb0e7cfac))
* **routing:** extend EntityMethodConfig with endpoint mappers and tell handler ([7891c56](https://github.com/Leberkas-org/TurboHTTP/commit/7891c56d89c0c5eb632be32c1d1e7772456ad433))
* **routing:** map new TLS options in EndpointResolver ([fcc8c6e](https://github.com/Leberkas-org/TurboHTTP/commit/fcc8c6e8d0a774d0faa92cc60e9f6c359b2a2cc2))
* **routing:** push response context on StartAsync before handler completes ([c968ee1](https://github.com/Leberkas-org/TurboHTTP/commit/c968ee123b15ef31d194b156ca3f7c8fe49fba5c))
* **routing:** update EntityDispatcher with two-tier mapper lookup and pluggable tell handler ([7bda4a1](https://github.com/Leberkas-org/TurboHTTP/commit/7bda4a172f887a75ea614f7cef3cb3d71edb5dfa))
* scaffold TurboHTTP.StressBenchmarks project with data records ([c619b2d](https://github.com/Leberkas-org/TurboHTTP/commit/c619b2d8ee2f179f51bd3c4622e772cef2eed595))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** add ClientCertificateMode and ServerCertificateSelector to TurboHttpsOptions ([3ba4a8c](https://github.com/Leberkas-org/TurboHTTP/commit/3ba4a8c91fe6ee8ab3e159d36c17dd7df23f8951))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add DelayCertificate renegotiation support ([3874559](https://github.com/Leberkas-org/TurboHTTP/commit/3874559468e42b8edf027c08757d86a340a69098))
* **server:** add drain protocol to body decoders for request pipelining ([e16cfed](https://github.com/Leberkas-org/TurboHTTP/commit/e16cfedaad66a8ba7a3de948f7455c3ebe3834b5))
* **server:** add IHttpRequestLifetimeFeature, IHttpRequestIdentifierFeature, IHttpResetFeature ([fcfe851](https://github.com/Leberkas-org/TurboHTTP/commit/fcfe851609662941432bd45d5f1da13b0b941b1d))
* **server:** add internal AddTurboKestrel overload accepting TurboServerOptions instance ([7903f13](https://github.com/Leberkas-org/TurboHTTP/commit/7903f131969d85f6ecdbdf59a3ee30cc3932f6f4))
* **server:** add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse ([ce010a3](https://github.com/Leberkas-org/TurboHTTP/commit/ce010a3c732b724c3ca5d7025896f7dcc935c72b))
* **server:** add ITurboEndpointRouteBuilder interface ([a9f86d4](https://github.com/Leberkas-org/TurboHTTP/commit/a9f86d4c2cb9658e674cdb803dea3bb195184dbd))
* **server:** add minimal TurboHttpContext constructor for protocol-layer creation ([1b43a73](https://github.com/Leberkas-org/TurboHTTP/commit/1b43a7303defa39f8e88b1a02ecd7ad6fc07cfc3))
* **server:** add request tracking and content classification to protocol layer ([04829db](https://github.com/Leberkas-org/TurboHTTP/commit/04829dbdc34b966cad19860ca788ffb1e3226921))
* **server:** add routing extension methods on ITurboEndpointRouteBuilder ([cc7d599](https://github.com/Leberkas-org/TurboHTTP/commit/cc7d599fae6f0235e6826aac301b4bbcbed711a8))
* **server:** add TurboEntityAskBuilder with Response, Produces, and WithTimeout support ([a093d79](https://github.com/Leberkas-org/TurboHTTP/commit/a093d79809f6a6e648083cd46e618ac893dc02b8))
* **server:** add TurboEntityTellBuilder with Response and Produces support ([e069149](https://github.com/Leberkas-org/TurboHTTP/commit/e069149b187059798ec58402c6ec8c98169a1483))
* **server:** add TurboTlsCallbackOptions and TurboTlsCallbackContext ([a1ddcf7](https://github.com/Leberkas-org/TurboHTTP/commit/a1ddcf76e16571266dc3f87a66d4db56fd1afab5))
* **server:** add TurboUrlCollection as ICollection&lt;string&gt; wrapper ([9420f5d](https://github.com/Leberkas-org/TurboHTTP/commit/9420f5d0f82dd6877fa5025e30218c2f0b17ec3b))
* **server:** add TurboWebApplicationBuilder ([51efcf7](https://github.com/Leberkas-org/TurboHTTP/commit/51efcf710656f2bbee6d1c4a5bb212e806391198))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** add UseHttps(TurboTlsCallbackOptions) overload to TurboListenOptions ([1217894](https://github.com/Leberkas-org/TurboHTTP/commit/1217894ae8fcbaebc195ad9a84c40cce4a6489cd))
* **server:** auto-detect response ordering from HTTP version ([32192f7](https://github.com/Leberkas-org/TurboHTTP/commit/32192f7b44bd57619cefc860ce3d587db241890a))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** expose Use, Run, Map, MapWhen directly on TurboWebApplication ([9d83e2f](https://github.com/Leberkas-org/TurboHTTP/commit/9d83e2fed274ef6fe074d592ce3e8686933d6064))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **server:** populate ITlsHandshakeFeature on HttpContext feature ([f50dd4c](https://github.com/Leberkas-org/TurboHTTP/commit/f50dd4c84be3f13c4ab8478c4ee7216e70b5f3db))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))
* **server:** support unordered response emission in ApplicationBridgeStage ([dc819fc](https://github.com/Leberkas-org/TurboHTTP/commit/dc819fc7542f431343473c1ac09d7296e5f1bb88))
* **servus:** add PipeReaderSourceStage and StreamSource factory ([075c6b2](https://github.com/Leberkas-org/TurboHTTP/commit/075c6b20172b2df2ddf7f599b5d2cfeb4398c23f))
* **sse:** add AsEventStream extension for reactive SSE consumption ([3ac50ec](https://github.com/Leberkas-org/TurboHTTP/commit/3ac50ec4697855402d36db860e60804375e2a44e))
* **sse:** add ServerSentEvent and AsEventStream ([794a3c6](https://github.com/Leberkas-org/TurboHTTP/commit/794a3c66a8fc8fa6d949925ad28a8feb497e5c44))
* **sse:** implement ServerSentEvent parser GraphStage with full RFC compliance ([7742ec0](https://github.com/Leberkas-org/TurboHTTP/commit/7742ec0170c94cdfc1ef25dfd2dd10137862a30e))
* **streams:** add NegotiatingServerEngine ([1b01747](https://github.com/Leberkas-org/TurboHTTP/commit/1b017470ebabea0812fca9db04c2925c6bdfd03d))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** add IntegrationTests.E2E project with SSE round-trip tests ([83fa651](https://github.com/Leberkas-org/TurboHTTP/commit/83fa65140a2daf8a5bd8caa787df51e7e7d25f0a))
* **tests:** add IntegrationTests.Server project with basic HTTP tests ([0289df1](https://github.com/Leberkas-org/TurboHTTP/commit/0289df17666cbb323989135692679d145214ee7c))
* **tests:** add ServerTestContextBuilder for fluent test context creation ([b115828](https://github.com/Leberkas-org/TurboHTTP/commit/b1158288d9aff1837c4a53f2d2ac665688977064))
* **tests:** add TurboServerFixture for server and E2E integration tests ([05c4e49](https://github.com/Leberkas-org/TurboHTTP/commit/05c4e49078f1245c049415a0c5902d19bd5f0192))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **transport:** add ClientCertificateMode enum ([06de0c0](https://github.com/Leberkas-org/TurboHTTP/commit/06de0c0532c75d76130e74539f815b409958af0c))
* **transport:** add ClientCertificateMode, HandshakeCallback, ServerCertificateSelector ([a98c9be](https://github.com/Leberkas-org/TurboHTTP/commit/a98c9be99a7d3885b2a6de2402c09c75bd6bcdae))
* **transport:** add TlsHandshakeContext, TlsHandshakeCallback, TlsConnectionResult ([3ba8d70](https://github.com/Leberkas-org/TurboHTTP/commit/3ba8d70b6c1490d0b53213de608fa3313b6875bd))
* **transport:** add TransportTlsState inbound message for DelayCertificate ([98ee596](https://github.com/Leberkas-org/TurboHTTP/commit/98ee596603fe70f2ac053ab0518ac01834e5d070))
* **transport:** extend SecurityInfo with NegotiatedCipherSuite and HostName ([d38c319](https://github.com/Leberkas-org/TurboHTTP/commit/d38c319ac4ba442125798481ccdb2064664b25d4))
* **transport:** rewrite TcpListenerStage handshake with 3 paths ([a84d189](https://github.com/Leberkas-org/TurboHTTP/commit/a84d1898cbe35f098bac7f891d1b64833baefd81))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))
* wire endpoint metadata from route registration through RoutingStage to TurboHttpContext ([e7a0cf7](https://github.com/Leberkas-org/TurboHTTP/commit/e7a0cf7f338f148b0259b05041221be891f241d1))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* add missing PipeWriter overrides and leaveOpen to ResponsePipeWriter ([503c32b](https://github.com/Leberkas-org/TurboHTTP/commit/503c32bbffcfa6c5b257663776228cc73a9c08db))
* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))
* checkout with lfs ([7a470ab](https://github.com/Leberkas-org/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **ci:** Remove docs push trigger ([3368641](https://github.com/Leberkas-org/TurboHTTP/commit/3368641ed0f5312b84851b8e59c664c862cec78a))
* **ci:** upgrade Node to 22 and regenerate docs lockfile ([e599b78](https://github.com/Leberkas-org/TurboHTTP/commit/e599b781d2bd9a2e732345234480e11a966ed62f))
* **docs:** correct scenario snippets to use real TurboHTTP APIs ([69d4eb5](https://github.com/Leberkas-org/TurboHTTP/commit/69d4eb5d3e2438027a1cabd6da11bb641cb5f108))
* **docs:** Pin likec4 to 1.50.0 ([06c08fb](https://github.com/Leberkas-org/TurboHTTP/commit/06c08fb988c68b85b8b64c1ba9797e518fe94103))
* **docs:** pin likec4 to 1.50.0 to avoid icon resolver bug ([5b4ecff](https://github.com/Leberkas-org/TurboHTTP/commit/5b4ecff5987c9fedef5eaa9991fd33f9028334b2))
* duplicate Content-Length in H1.1 server encoder + test fixes ([1880611](https://github.com/Leberkas-org/TurboHTTP/commit/18806116892e58b9037a8c263021d07fae66511b))
* **e2e:** add missing using directives and fix empty-echo response format ([7b96bd4](https://github.com/Leberkas-org/TurboHTTP/commit/7b96bd4c2441e5ffabc6e56f41af5342338edef5))
* **e2e:** skip H3 tests properly, reduce H11 pipelining concurrency ([e10ec98](https://github.com/Leberkas-org/TurboHTTP/commit/e10ec98e0b5cde2f64fcdfdfc8423e7e435cf4a5))
* **e2e:** use Results.Text for plain string assertions in ResilienceSpecs ([f4d0370](https://github.com/Leberkas-org/TurboHTTP/commit/f4d0370fc14a570d4c2fe0baac1f6edd7beae1fa))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* guard _headerCommit in CommitAndFlushAsync with try-finally ([730f9ef](https://github.com/Leberkas-org/TurboHTTP/commit/730f9ef1ebc0f8b741e9d927e6631dbc83ef66ea))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* **h2:** detect response body via HasStarted for H2 responses without Content-Length ([d84bcef](https://github.com/Leberkas-org/TurboHTTP/commit/d84bcef8005ea86e29f7c1749447fa18fb5e59fd))
* **h2:** detect response body via HasStarted when no Content-Length ([5ad82ad](https://github.com/Leberkas-org/TurboHTTP/commit/5ad82adb81a3a9c6a3f0c21dcf3d1ce593be1776))
* **h2:** sync HPACK decoder table size with announced SETTINGS + skip connection preface ([53205b7](https://github.com/Leberkas-org/TurboHTTP/commit/53205b79dd39756be9faadd430e71b9d995a52ea))
* **h3:** enable QUIC/HTTP3 integration tests on Docker ([d940a60](https://github.com/Leberkas-org/TurboHTTP/commit/d940a60499afe142f31994a2090d1be35ed196c5))
* **h3:** improve control stream stability ([153a37b](https://github.com/Leberkas-org/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** open QUIC stream before sending request frames ([8ebd05d](https://github.com/Leberkas-org/TurboHTTP/commit/8ebd05dc4557c1a8f318b98d4a689d76e6f63a13))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/Leberkas-org/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **http2,http3:** treat absent Content-Length as streaming response with body ([bd4767e](https://github.com/Leberkas-org/TurboHTTP/commit/bd4767e9bd3557e7a61792873d506431de379c9e))
* improve NotSupportedException messages for WebSockets and Session ([f1fab94](https://github.com/Leberkas-org/TurboHTTP/commit/f1fab949bb7b6c4766327f15136f65eb4bef21ab))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/Leberkas-org/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/Leberkas-org/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* minor fixes ([2d62179](https://github.com/Leberkas-org/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* pipeline ([103f1ce](https://github.com/Leberkas-org/TurboHTTP/commit/103f1ce3a431ee06e34df74f69d55c7a9ea0a4e3))
* populate IServerAddressesFeature with resolved endpoint URLs ([23ebbd5](https://github.com/Leberkas-org/TurboHTTP/commit/23ebbd53cb2f72741573494340ed33817bc714e0))
* public api changes ([2d6dfa4](https://github.com/Leberkas-org/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/Leberkas-org/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* **quic:** resolve deadlock in AcceptInboundStreamAsync test ([3e1eacb](https://github.com/Leberkas-org/TurboHTTP/commit/3e1eacb9819fd3e5c0b60b419f741bd49083eefe))
* **quic:** use IPEndPoint for IP address hosts in QuicClientProvider ([0d49238](https://github.com/Leberkas-org/TurboHTTP/commit/0d492383e48411ceb906fba187bef31ac43a7a57))
* release please ([c1c7ae3](https://github.com/Leberkas-org/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))
* **request-feature:** ensure Host header fallback from RequestUri ([25979e1](https://github.com/Leberkas-org/TurboHTTP/commit/25979e19acb349bea394ab59faf32a27a7e83d68))
* reset release-please version to 0.8.0 ([3f465d8](https://github.com/Leberkas-org/TurboHTTP/commit/3f465d8c14af2df1d2c2e77061ac6b2fa35c2e2e))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* resolve HTTP/2 and HTTP/3 response body encoding logic ([38f3c1d](https://github.com/Leberkas-org/TurboHTTP/commit/38f3c1d155afd22f57479fb560adbdfef44c8717))
* **routing:** remove broken RequestBinder for HttpRequestMessage parameter type ([c3ec9c0](https://github.com/Leberkas-org/TurboHTTP/commit/c3ec9c0ef23867665a5493db63158014e6e71b95))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **server:** eliminate listener bind race condition via materialized Task ([b16dbc8](https://github.com/Leberkas-org/TurboHTTP/commit/b16dbc8dbcc4399427b1ac097a7f0cc73b249707))
* **server:** thread IServiceProvider and TurboConnectionInfo through server pipeline ([abce087](https://github.com/Leberkas-org/TurboHTTP/commit/abce0870d865b5600e489e799477c6f11b392d5c))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* skip H2 connection preface in server FrameDecoder + fix client ActorSystem setup ([5b8736b](https://github.com/Leberkas-org/TurboHTTP/commit/5b8736b47d1e954a91a949eec3688850950181b8))
* **sse:** align formatter with Kestrel's SseFormatter implementation ([a13b9ef](https://github.com/Leberkas-org/TurboHTTP/commit/a13b9eff960bd4fae3f825192f825f3d94406578))
* **sse:** align SSE formatter with WHATWG spec ([abaef51](https://github.com/Leberkas-org/TurboHTTP/commit/abaef511e0da16e8b063e46a9e36a2c555515352))
* **sse:** align SSE parser with WHATWG spec and skip SSE tests on Docker ([a46d70f](https://github.com/Leberkas-org/TurboHTTP/commit/a46d70f8f9d79b66cc8134db17e75df1589e01ee))
* **sse:** remove duplicate XML documentation in Extensions.cs ([0100f12](https://github.com/Leberkas-org/TurboHTTP/commit/0100f12a4faffb5dc593c0c0c277f1a5a3a11e82))
* switch NuGet publish from Trusted Publishing to API key auth ([2f4bbad](https://github.com/Leberkas-org/TurboHTTP/commit/2f4bbad2ccdf79fee983ea7452e63839d38fc2c9))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **tests:** add Materializer property to FakeServerOps and SwitchCapableOps ([83f9002](https://github.com/Leberkas-org/TurboHTTP/commit/83f9002af679d7f088d70aea94a6d9f28fd95e7d))
* **tests:** consolidate server test fakes and fix all server-side test failures ([d531035](https://github.com/Leberkas-org/TurboHTTP/commit/d53103517e29776e38ab8bbec44a45798715d321))
* **tests:** let Kestrel pick HTTPS port to avoid port conflicts ([7a8bfe0](https://github.com/Leberkas-org/TurboHTTP/commit/7a8bfe00e1807cec5116a522193341101e4b57f4))
* **tests:** Use 127.0.0.1 for H3 and parallelize tests ([9161dd0](https://github.com/Leberkas-org/TurboHTTP/commit/9161dd06516ad24960b782dcde1d72960c071088))
* **tests:** use fresh HttpClient to avoid connection pool reuse in timeout test ([be74295](https://github.com/Leberkas-org/TurboHTTP/commit/be742952ed5285221848304d8f2774f36cfee408))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* Update lock file dependencies ([8399514](https://github.com/Leberkas-org/TurboHTTP/commit/8399514e9a8baed02d7b45b11bbb5a96793088fa))
* update tests for RequestContext pipeline type ([754747f](https://github.com/Leberkas-org/TurboHTTP/commit/754747f64aa9e528c375279a86b38510ae9023ec))
* use N * 1024 size literals in TurboServerOptions per CLAUDE.md ([dfef509](https://github.com/Leberkas-org/TurboHTTP/commit/dfef5096112cc52c0253d91298ced30c13421364))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Performance

* batch HTTP/3 frame serialization into single TransportBuffer per request ([e7346bb](https://github.com/Leberkas-org/TurboHTTP/commit/e7346bb71d46b20280b5b451e66e5bfe169e1858))
* batch QPACK encoder instruction flushes in HTTP/3 client ([84e02d4](https://github.com/Leberkas-org/TurboHTTP/commit/84e02d4f4fa8c44852c3f74bd1428d657d0da1e2))
* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/Leberkas-org/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* coalesce queued outbound TransportData writes into single buffer ([cdaf83b](https://github.com/Leberkas-org/TurboHTTP/commit/cdaf83b0f35fbcc2e1bc1b8777b2ff0349009d6f))
* direct-push bypass and pre-sized queues in HttpConnectionStageLogic ([758300f](https://github.com/Leberkas-org/TurboHTTP/commit/758300fab2ac39fa0ae4771ee4f2e7d4d3c8d12a))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/Leberkas-org/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/Leberkas-org/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/Leberkas-org/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/Leberkas-org/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* increase H3 StreamState pool 16→256, reduce encoder buffer 8K→4K ([7585b11](https://github.com/Leberkas-org/TurboHTTP/commit/7585b1158f4d18f5c194d83b9a18ac9f55849c0b))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/Leberkas-org/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/Leberkas-org/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))
* reuse HeaderCollection in H1.1 encoder, increase benchmark pipeline depth ([ed65553](https://github.com/Leberkas-org/TurboHTTP/commit/ed65553ffe45f660631addb6c2bf418a8ac06e72))
* **routing:** eliminate per-request allocations in route matching ([5ac31d2](https://github.com/Leberkas-org/TurboHTTP/commit/5ac31d21cf1300a647b30515a85ed88b794503a3))
* **routing:** replace linear scan with dictionary lookup in RouteTable ([b3be40a](https://github.com/Leberkas-org/TurboHTTP/commit/b3be40a7d7a7683b271e0f59a6ac12ce77d394da))
* **server:** add Date and Content-Length header caches ([861c436](https://github.com/Leberkas-org/TurboHTTP/commit/861c4365e6ff75f7924a699f2312d5b87eede9c2))
* **server:** add server-side micro and throughput benchmarks ([b6bf348](https://github.com/Leberkas-org/TurboHTTP/commit/b6bf348b51bc0b3f20ee5c810e879918c0d8336d))
* **server:** implement context pooling with reset semantics ([faf7300](https://github.com/Leberkas-org/TurboHTTP/commit/faf7300b7d5fd51c83359a79f04ee7b785bfe88e))
* **tests:** parallelize integration tests and fix H3 infrastructure ([0d93389](https://github.com/Leberkas-org/TurboHTTP/commit/0d9338915af97f7ad1623b49b38f671cb0441acc))


### Documentation

* accept API surface changes from app-framework layer deletion ([5044639](https://github.com/Leberkas-org/TurboHTTP/commit/504463931159599a62a3f972e585fecdffe47429))
* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add dynamic protocol negotiation design spec ([ab58122](https://github.com/Leberkas-org/TurboHTTP/commit/ab58122bb80d5251958399019bf06fe133dbc420))
* add dynamic protocol negotiation implementation plan ([45ba63e](https://github.com/Leberkas-org/TurboHTTP/commit/45ba63eb9c5ad84b2901cddd0d6bb311de820c48))
* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add IServer pipeline redesign spec and implementation plan ([9db902d](https://github.com/Leberkas-org/TurboHTTP/commit/9db902d9b0776d94111292d763436bc6f6205ca3))
* Add LikeC4 plugin and diagrams ([25f17bd](https://github.com/Leberkas-org/TurboHTTP/commit/25f17bd8d11322408ae877777e97a57c1a3e0288))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add scenarios showcase page with all 5 scenarios ([40f1f9e](https://github.com/Leberkas-org/TurboHTTP/commit/40f1f9eea5c1a468026ee8d001eb0c21574967c8))
* add scenarios to VitePress navigation ([c7cab1b](https://github.com/Leberkas-org/TurboHTTP/commit/c7cab1be842b7eefdb15664234dd0967b329dc3c))
* add server test coverage map with risk-based prioritization ([63c5081](https://github.com/Leberkas-org/TurboHTTP/commit/63c5081d1549c054e7b29cbf1794ad164ad0de59))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* align LikeC4 models and docs with actual class names ([75a0a0f](https://github.com/Leberkas-org/TurboHTTP/commit/75a0a0f0e4e5bf7a2233baa96b181b55822a1dd3))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* escape angle brackets in generic types to fix Vue parser ([88261dc](https://github.com/Leberkas-org/TurboHTTP/commit/88261dc11a08120f4a9fc36d7f1c6d98498929e4))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* fix table rendering in installation and aspnet-core pages ([0004b9c](https://github.com/Leberkas-org/TurboHTTP/commit/0004b9c06c3ff9b08a7b10e04c8cbe8cf7a7722b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* **likec4:** consolidate model-server.c4 into model-pipeline.c4 ([7fdf75e](https://github.com/Leberkas-org/TurboHTTP/commit/7fdf75ef7809243e214841132c150f4faf4b4768))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* remove Extending the Pipeline pages ([16b0196](https://github.com/Leberkas-org/TurboHTTP/commit/16b0196b0219e003c87094e171aa5d3ad86a1203))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* restructure server documentation for IServer architecture ([cc3b2b5](https://github.com/Leberkas-org/TurboHTTP/commit/cc3b2b5fa77e779a1f931a4633c8bc269398c988))
* update ([968cbfe](https://github.com/Leberkas-org/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update CLAUDE.md for IServer pipeline architecture ([09ea765](https://github.com/Leberkas-org/TurboHTTP/commit/09ea7651e722f58cc4802ca390f058286539b535))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update landing page and scenarios for IServer architecture ([3dc1919](https://github.com/Leberkas-org/TurboHTTP/commit/3dc1919247b842d318c56da562a9cd6f66d63d6e))
* update LikeC4 ([478dbb4](https://github.com/Leberkas-org/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update NuGet metadata and fix README links ([f4882d4](https://github.com/Leberkas-org/TurboHTTP/commit/f4882d4b552970bef3f44e449dfdae066b74b55d))
* Update README links to new organization ([770150c](https://github.com/Leberkas-org/TurboHTTP/commit/770150cfd0af3d295e1d202ef05e78984d5dffc4))
* Update README with server features ([1f26bb8](https://github.com/Leberkas-org/TurboHTTP/commit/1f26bb80ab21762ae51853ac475a5bd7a22e70b5))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))


### Refactoring

* **bench:** move baselines into MicroBenchmarks project ([ff0ab74](https://github.com/Leberkas-org/TurboHTTP/commit/ff0ab74801800a8c55bb969827326e28902eb420))
* **bench:** shorten baseline filenames to class names ([0b8e863](https://github.com/Leberkas-org/TurboHTTP/commit/0b8e8638eef01d773ff82319141e616f8121dd2f))
* clean up Stages/ subfolders ([bf0b9c6](https://github.com/Leberkas-org/TurboHTTP/commit/bf0b9c67143fc33f3b015f8befd37b9cb73ad489))
* **client:** add version-guarded exception signaling and remove unused body types ([0565e7a](https://github.com/Leberkas-org/TurboHTTP/commit/0565e7a0261f0cca30f1b2f4fa865c783750c4e6))
* **client:** unify TurboHttpClient to single TCS-based SendAsync ([2106753](https://github.com/Leberkas-org/TurboHTTP/commit/21067530a4324fad407fe108ce9364f9720e1845))
* **context:** replace BodySink and GetResponseSource with Servus.Akka IO stages ([3d3e5ce](https://github.com/Leberkas-org/TurboHTTP/commit/3d3e5ceb5cc6fbf910d9f0f2c780ac27deabd78f))
* **context:** unify H2/H3 stream ID into single IHttpStreamIdFeature ([043fc69](https://github.com/Leberkas-org/TurboHTTP/commit/043fc69031d17e3e909dabeceb0b1525704bc5a7))
* create Client namespace ([d25b032](https://github.com/Leberkas-org/TurboHTTP/commit/d25b0328e1da51b63e7bd65239b56bdf0f1eb5e1))
* delete app-framework layer ([fcdf04d](https://github.com/Leberkas-org/TurboHTTP/commit/fcdf04d49ec5eb7e71eb44cb550ab330b1af7fc4))
* delete RequestContext, TurboHttpContext, RoutingStage, and all custom routing types ([d60a68d](https://github.com/Leberkas-org/TurboHTTP/commit/d60a68d1ac3eb64472219f750dd9993f3d4ca992))
* **e2e:** add protocol collections for partial runs, skip timeout tests ([8a95029](https://github.com/Leberkas-org/TurboHTTP/commit/8a9502903aa32d7ff6ea63843a84324a5a9090e5))
* **engines:** simplify all server engines and ProtocolRouter to pass TurboServerOptions ([edbb2fe](https://github.com/Leberkas-org/TurboHTTP/commit/edbb2fe48b7bfe825e82ce94c3fd795f42f720bc))
* exclude integration server tests pending IServer rewrite ([c743555](https://github.com/Leberkas-org/TurboHTTP/commit/c743555f1f24887e66275444920da39e499c6035))
* extract Http1/2/3ServerOptions to separate files ([ce6fb83](https://github.com/Leberkas-org/TurboHTTP/commit/ce6fb83c9e3873c81b7c94fa89ef7ca4aa0949ec))
* **h10:** implement IHttpStateMachine and replace stage logic ([8839a3a](https://github.com/Leberkas-org/TurboHTTP/commit/8839a3a290207ae49edffd0a988690ea8e7920a4))
* **h11:** migrate to generic HttpConnectionStageLogic base class ([192933b](https://github.com/Leberkas-org/TurboHTTP/commit/192933bcf5343f79df9647441407e5f91b385ade))
* **h1:** new StateMachine ([3d0360c](https://github.com/Leberkas-org/TurboHTTP/commit/3d0360ca8d16fad9075049acba51b58c69396a61))
* **h2:** implement IHttpStateMachine and replace stage logic ([395c050](https://github.com/Leberkas-org/TurboHTTP/commit/395c050711a58285312749d5332f3993da72cd33))
* **h2:** new StateMachine ([07bcc84](https://github.com/Leberkas-org/TurboHTTP/commit/07bcc84b3d9b3828628f730e4fcba66969c9e383))
* **h3:** finalize HTTP/3 StateMachine migration with pre-connect buffering ([831a963](https://github.com/Leberkas-org/TurboHTTP/commit/831a9632a51d0fa42a682b79cc963b124d897e6d))
* **h3:** implement IHttpStateMachine on Http3 StateMachine ([17981e5](https://github.com/Leberkas-org/TurboHTTP/commit/17981e570006e431b0ec3ae8740ebbde835ad4de))
* **h3:** remove 0-RTT early data and server push dead code ([80eb0fa](https://github.com/Leberkas-org/TurboHTTP/commit/80eb0faa7272ca17647a57ba8184f616c5db1065))
* **h3:** remove redundant type argument from EmitMultiple call ([1177ca5](https://github.com/Leberkas-org/TurboHTTP/commit/1177ca5b28c98aa03a117aed298e8e98f9337d87))
* **h3:** renaming ([36c4cba](https://github.com/Leberkas-org/TurboHTTP/commit/36c4cba2ff0ba189b324c12c0337b675b71eb8b6))
* **http10:** accept TurboServerOptions in Http10ServerStateMachine ([b8d0404](https://github.com/Leberkas-org/TurboHTTP/commit/b8d040402218cc9f488ac8f8362c68e8feddbdeb))
* http11 ([0e1d710](https://github.com/Leberkas-org/TurboHTTP/commit/0e1d7109895714e8801a0cc70ebb55040284a0c4))
* **http11:** accept TurboServerOptions in Http11ServerStateMachine ([152fd3c](https://github.com/Leberkas-org/TurboHTTP/commit/152fd3c019ec9846c9585d18409c7453e98a3093))
* **http2:** accept TurboServerOptions in Http2ServerStateMachine ([926a677](https://github.com/Leberkas-org/TurboHTTP/commit/926a67740bd7f43d59ae935c9ec593fad02202dd))
* **http3:** accept TurboServerOptions in Http3ServerStateMachine ([2788931](https://github.com/Leberkas-org/TurboHTTP/commit/2788931539ae24def136e625a24d97b1e73322fc))
* **http3:** delegate client body encoding to Multiplexed BodyEncoder ([c08cf68](https://github.com/Leberkas-org/TurboHTTP/commit/c08cf68b9daf1d9d5fb362611265ad633aa15681))
* **integration:** overhaul test infrastructure with backend abstraction ([7e6b5e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e6b5e4fd35a80400a03b9429a861ac93ecef230))
* **internal:** consolidate correlation keys into TurboClientCorrelation ([6b1d92f](https://github.com/Leberkas-org/TurboHTTP/commit/6b1d92f5b846e6532f8fc68f0ceb1d165a02cb0c))
* make lifetime and identifier features self-contained ([c60be42](https://github.com/Leberkas-org/TurboHTTP/commit/c60be42c32d95f89174f676df7a832dfc0ed745e))
* **manager:** rewrite ClientStreamManager as supervisor actor ([d7f6ec6](https://github.com/Leberkas-org/TurboHTTP/commit/d7f6ec63987c26c79e75720b9cf1d28e96e82d21))
* merge Binding/ into Routing/ and move EndpointResolver ([70afda5](https://github.com/Leberkas-org/TurboHTTP/commit/70afda5fd96b2dbde521cb0239d8e01448502d1a))
* merge TurboHTTP.Server into TurboHTTP and reorganize client namespace ([b2f807e](https://github.com/Leberkas-org/TurboHTTP/commit/b2f807e116fd557a9ad9c49e17039ca82593f36c))
* migrate all StateMachines to RequestFault + Tracing ([8dc2d88](https://github.com/Leberkas-org/TurboHTTP/commit/8dc2d88f1a2fa896774c7c19eaecb9567c25ceb9))
* move and rename client stage classes to Stages/Client/ subdirectory ([3651f34](https://github.com/Leberkas-org/TurboHTTP/commit/3651f34cbc29b0e531a220581a4d1f811ddb06a9))
* **multiplexed:** make StreamBodyMessages generic for type-safe stream IDs ([5599631](https://github.com/Leberkas-org/TurboHTTP/commit/55996313bef755a39e020a3373767aa2bd44a3cc))
* **owner:** delegate consumer lifecycle to ConsumerActor children ([e791366](https://github.com/Leberkas-org/TurboHTTP/commit/e791366530a21c0bc6008ff7dfb8e52892cb085f))
* **owner:** eager pipeline materialization in PreStart ([5c7a2af](https://github.com/Leberkas-org/TurboHTTP/commit/5c7a2af096ea434ded44bc48d6e7b1456e4f8a44))
* **pipeline:** remove enrichment from shared pipeline ([6ab5737](https://github.com/Leberkas-org/TurboHTTP/commit/6ab573727a08caa2e430b6c7f862f5caa60266f1))
* promote Server/Context/ to top-level Context/ ([49346a0](https://github.com/Leberkas-org/TurboHTTP/commit/49346a0b3a49efc2087848eabb08e76b07e1808c))
* **protocol:** expose ContentHeaders property on StreamState ([e1739ec](https://github.com/Leberkas-org/TurboHTTP/commit/e1739ec567a811f63c3acf7bf17a6ad7e0929570))
* **protocol:** harden server encoders and improve redirect handling ([0f08578](https://github.com/Leberkas-org/TurboHTTP/commit/0f08578dd4dddd054167161a379de9724e1a9148))
* **protocol:** improve HTTP/1.x and line-based protocol layer ([b48820f](https://github.com/Leberkas-org/TurboHTTP/commit/b48820f1be266d3b28fb0bbe84b7312c94a3a0fe))
* **protocol:** improve HTTP/2 protocol layer and restructure tests ([f196ff1](https://github.com/Leberkas-org/TurboHTTP/commit/f196ff110cae52a6eff8efd61782a8dc71877bd9))
* **protocol:** improve HTTP/3 and multiplexed protocol layer ([2904c81](https://github.com/Leberkas-org/TurboHTTP/commit/2904c8105d7ba5bddb970a9202aaeb0148858a4e))
* relocate HandlerBidiStage from Stages/Server to Stages/Client ([5556a3a](https://github.com/Leberkas-org/TurboHTTP/commit/5556a3a751222d6d5c073099e16d7f8b030b03c0))
* remove ITurbo*Feature interfaces, use IHttp*Feature only ([55c5c12](https://github.com/Leberkas-org/TurboHTTP/commit/55c5c1290dcdc40e5fb7ab708150f2d257fe54d7))
* rename client engines with explicit Client prefix ([8f95990](https://github.com/Leberkas-org/TurboHTTP/commit/8f9599029a5cca5477afdf28302e5d84d1a5a520))
* rename server stages with explicit Server prefix ([a9b2e91](https://github.com/Leberkas-org/TurboHTTP/commit/a9b2e912d928c7f1a37c212c5da86d6e22f0f110))
* replace EmitMultiple with queue-based single Push per pull ([01fdd26](https://github.com/Leberkas-org/TurboHTTP/commit/01fdd26fef5aadf2d61d10e44b501c73c40b092d))
* restructure HTTP/3 and QUIC layers ([a77a049](https://github.com/Leberkas-org/TurboHTTP/commit/a77a049a27939489698840b50893702f39421ecd))
* rewrite ApplicationBridgeStage as generic with IHttpApplication&lt;TContext&gt; ([7eb5117](https://github.com/Leberkas-org/TurboHTTP/commit/7eb5117156ebb625d4529404228500c0f54e543c))
* **routing:** use string-based HTTP method keys in RouteTabler ([9d43351](https://github.com/Leberkas-org/TurboHTTP/commit/9d433514f30c7f3edc2f7fcae3513952a60a17e6))
* **server:** add missing options properties and fix defaults to RFC/Kestrel alignment ([0a492cd](https://github.com/Leberkas-org/TurboHTTP/commit/0a492cdc82b44a3df2247bf02490b2547bc79911))
* **server:** extract ConnectionInfo from TransportConnected in StageLogic ([a4cde53](https://github.com/Leberkas-org/TurboHTTP/commit/a4cde53c9d4d33bc5cf4feae8170e2ba45dd213c))
* **server:** merge middleware pipeline into routing stage ([b2d0317](https://github.com/Leberkas-org/TurboHTTP/commit/b2d031766d4a0aed688529e31b01ec151660ee8c))
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder() ([f4cf7af](https://github.com/Leberkas-org/TurboHTTP/commit/f4cf7af822489518c717bd148caab4d2a3d7f8e8))
* **server:** remove HttpRequestMessage/HttpResponseMessage from server pipeline ([070a1e2](https://github.com/Leberkas-org/TurboHTTP/commit/070a1e2efedb6f10ab78a8705b208295bf56c2d3))
* **server:** remove TurboTlsCallbackOptions ([248e6e1](https://github.com/Leberkas-org/TurboHTTP/commit/248e6e115531bfab325f023e00ec8f0b5acb7446))
* **server:** rename entity builder Response to Handle/Produces ([15ddd9e](https://github.com/Leberkas-org/TurboHTTP/commit/15ddd9e53acea7b632a1be12bf98d401da23d277))
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder ([cbfac65](https://github.com/Leberkas-org/TurboHTTP/commit/cbfac65c9b281a202ee278dc372808142f03d862))
* **server:** return ITurboPipelineBuilder from pipeline methods ([4fbbc72](https://github.com/Leberkas-org/TurboHTTP/commit/4fbbc72168e243678d705ebc5b7c4495cf971805))
* **server:** rewrite TurboWebApplication with static factories and interfaces ([fb4970e](https://github.com/Leberkas-org/TurboHTTP/commit/fb4970eecbb9d47744a1636b3cd94852bd7e70ea))
* **server:** simplify entity routing and move builder types into Server namespace ([bcc311c](https://github.com/Leberkas-org/TurboHTTP/commit/bcc311cf7e4fbb97ad8b86310690627963f0e006))
* **server:** switch entire server pipeline ([65c3214](https://github.com/Leberkas-org/TurboHTTP/commit/65c32140bb66d10d331857bf3fbe9c31b9dd4c35))
* **server:** wire split H2 window sizes through engine chain ([b48dce0](https://github.com/Leberkas-org/TurboHTTP/commit/b48dce0906a0dc8e1bcb5ba0b5360077961fc416))
* **servus:** move PipeWriterSinkStage to Servus.Akka and add StreamSink factory ([8611da3](https://github.com/Leberkas-org/TurboHTTP/commit/8611da32a2855ce075a8b35dccb12add4559515a))
* **servus:** move PipeWriterSinkStage to Servus.Akka and add StreamSink factory ([0a98a57](https://github.com/Leberkas-org/TurboHTTP/commit/0a98a57b93435f02080b0a5f52b5f3415627f879))
* slim IStageOperations -- remove OnWarning/OnComplete/OnFail ([de41e2a](https://github.com/Leberkas-org/TurboHTTP/commit/de41e2aeb528123c0c31c1cffd758e614e4fbee4))
* **stages:** accept TurboServerOptions in all server ConnectionStages ([3950529](https://github.com/Leberkas-org/TurboHTTP/commit/39505290e605b758d4117a5a6c9d16987e86f1aa))
* **streams:** update stream stages, lifecycle, and rename tests ([9a83e8b](https://github.com/Leberkas-org/TurboHTTP/commit/9a83e8b070a838590130b6f74b4638d7081156ca))
* **tests:** consolidate StreamTests into TurboHTTP.Tests ([882da88](https://github.com/Leberkas-org/TurboHTTP/commit/882da88439777582e5d3341825963b434f734967))
* **tests:** extract shared integration test fixtures into separate project ([5ea8eac](https://github.com/Leberkas-org/TurboHTTP/commit/5ea8eac2a41f6ac6423ff4cda3e0e1228d7c06e6))
* **tests:** merge IntegrationTests.Shared into Tests.Shared and remove old project ([111cf5d](https://github.com/Leberkas-org/TurboHTTP/commit/111cf5dcf82235358e6f4a8b0c36585806a7e31c))
* **tests:** migrate EngineTestBase from static ActorSystem to StreamTestBase ([08d51a6](https://github.com/Leberkas-org/TurboHTTP/commit/08d51a6addc9a8dc74c574f05910eb3170f9f9eb))
* **tests:** migrate protocol server test specs ([e5b4fdc](https://github.com/Leberkas-org/TurboHTTP/commit/e5b4fdc2f10fb06174b496b0069eacb8ac1efde7))
* **tests:** migrate routing and binding specs to ServerTestContextBuilder ([6240ffe](https://github.com/Leberkas-org/TurboHTTP/commit/6240ffe3066304011ee31e4fe750ad056598a6da))
* **tests:** migrate server and context specs to ServerTestContextBuilder ([094ae8a](https://github.com/Leberkas-org/TurboHTTP/commit/094ae8a7adc492d76b0aba1f8c8cab8181c213ed))
* **tests:** migrate StateMachine and factory tests to TestKit ([e3faed1](https://github.com/Leberkas-org/TurboHTTP/commit/e3faed1bfcc85bf402124a32fedb06bb1fce7b26))
* **tests:** migrate stream stage tests to StreamTestBase ([3b42177](https://github.com/Leberkas-org/TurboHTTP/commit/3b42177dbe0b356b68558449766f78b6a8ac893d))
* **tests:** move server tests to IntegrationTests.Server and update Client.Shared ([5ee2358](https://github.com/Leberkas-org/TurboHTTP/commit/5ee2358e555cdee66d09cbe108aaf29c922576b3))
* **tests:** move TransportBufferPoolSpec to Servus.Akka.Tests ([97d3dd5](https://github.com/Leberkas-org/TurboHTTP/commit/97d3dd5012e20c5cdb3d0ae9de607527e71cd689))
* **tests:** remove CtsPoolSpec that only tested .NET framework behavior ([b7741c9](https://github.com/Leberkas-org/TurboHTTP/commit/b7741c9112ffbcf5024bd7decbeaa1bbd64150c7))
* **tests:** rename IntegrationTests to IntegrationTests.Client ([8bfe9e4](https://github.com/Leberkas-org/TurboHTTP/commit/8bfe9e419e9f9d6bae63c478a2429576404a9d05))
* **tests:** reorganize Semantics tests into domain subfolders ([e7a79c7](https://github.com/Leberkas-org/TurboHTTP/commit/e7a79c7ad392daef999504f12bedcdad44c9d264))
* **tests:** replace ServerTestContext.CreateRequestFeature with builder entry point ([8dc194a](https://github.com/Leberkas-org/TurboHTTP/commit/8dc194ae6eb7ac1e3c8f1dfb8e80c8c136660ac1))
* **tests:** restructure Http2 tests into Client/Server hierarchy ([dec4697](https://github.com/Leberkas-org/TurboHTTP/commit/dec46978c8be64d0ed0bc765be3f46d159de8384))
* **tests:** restructure Http3 tests into Client/Server hierarchy ([d60ad21](https://github.com/Leberkas-org/TurboHTTP/commit/d60ad21603e1c78b9e5966526ef6620fec1494c1))
* **tests:** restructure test folders to mirror source project layout ([db4efe9](https://github.com/Leberkas-org/TurboHTTP/commit/db4efe973a3b972dd835e3a1f31d5133961703a4))
* **tests:** restructure tests to match new protocol architecture ([6263464](https://github.com/Leberkas-org/TurboHTTP/commit/626346405752958b92e1af99c20066d735ec693b))
* **tests:** update acceptance tests and shared test helpers ([332b09a](https://github.com/Leberkas-org/TurboHTTP/commit/332b09a96b1e5e86ef78436ea87092afb4268711))
* **tests:** update Client test files with Client.Shared namespace ([19f8c0d](https://github.com/Leberkas-org/TurboHTTP/commit/19f8c0de254befdef0cd080d97aa12c31118fe89))
* **tests:** update shared test infrastructure for server support ([4d36c0f](https://github.com/Leberkas-org/TurboHTTP/commit/4d36c0ffcd2b921708097da85a8f13bb715ab92a))
* **transport:** introduce StreamTarget and extract ServerStreamResolver ([d59c2c6](https://github.com/Leberkas-org/TurboHTTP/commit/d59c2c6fa1b613f05764fd94d514a50262cbe3dd))
* **transport:** remove TLS handshake callback abstraction ([31316bc](https://github.com/Leberkas-org/TurboHTTP/commit/31316bcc2af756aa08d8f567e5860567ed752298))
* **transport:** split ConnectionInfo into ConnectionInfo + SecurityInfo ([91a63b9](https://github.com/Leberkas-org/TurboHTTP/commit/91a63b9064b5c31b2d0fd0ff692ea8fc26f83b57))
* **TurboHTTP:** Move protocol features to Features namespace ([7a0d5f6](https://github.com/Leberkas-org/TurboHTTP/commit/7a0d5f69513e4064afbde872c90ebf551d05e6e8))
* **TurboHTTP:** restructure protocol to LineBased/Multiplexed architecture ([89d1f2f](https://github.com/Leberkas-org/TurboHTTP/commit/89d1f2fb549da01ab193935850b5a39404336ad1))
* update all state machines and session managers to IFeatureCollection ([1a353af](https://github.com/Leberkas-org/TurboHTTP/commit/1a353afcbbbf3c566aea65de7923821bfa8a41f9))
* update protocol encoders to accept IFeatureCollection ([d386282](https://github.com/Leberkas-org/TurboHTTP/commit/d386282ae5791477d5567df93acd85b88c1adec2))
* update stage logic, connection stages, and engines to IFeatureCollection ([6940dce](https://github.com/Leberkas-org/TurboHTTP/commit/6940dce3258a3e0c70e864ca6e087463a54b89e5))
* Use lowercase property names in ProtocolVariant ([d5d6eef](https://github.com/Leberkas-org/TurboHTTP/commit/d5d6eef2912c6d7d496f593112273fd07de3361e))
* wire IHttpApplication through actors to ApplicationBridgeStage ([853f142](https://github.com/Leberkas-org/TurboHTTP/commit/853f1424bf288ba8879985a9b5a256239e94261a))


### Dependencies

* bump actions/download-artifact from 4 to 8 ([262114c](https://github.com/Leberkas-org/TurboHTTP/commit/262114ce2377df106bf34f0a9fe2a6d5cdb43aa4))
* bump actions/upload-artifact from 4 to 7 ([bdd7d51](https://github.com/Leberkas-org/TurboHTTP/commit/bdd7d518501ba6029111a34a766023a4ce1e3be0))
* Bump Akka.Streams, Akka.Streams.TestKit and Akka.TestKit.Xunit ([b2c3c36](https://github.com/Leberkas-org/TurboHTTP/commit/b2c3c362df304acc44ace4a3023d06da845845b0))
* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/Leberkas-org/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))
* Bump Microsoft.Testing.Extensions.CodeCoverage from 18.6.2 to 18.7.0 ([6d518f7](https://github.com/Leberkas-org/TurboHTTP/commit/6d518f707c119620bcbb819dfa236c5dd7911445))
* Bump Testcontainers from 4.11.0 to 4.12.0 ([780d25a](https://github.com/Leberkas-org/TurboHTTP/commit/780d25a4b06f79a181e566e5d89688f6394b78cd))
* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump the akka group with 1 update ([4edbee8](https://github.com/Leberkas-org/TurboHTTP/commit/4edbee8a16572580be06385984939448aacaebd9))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))
* Bump Verify.XunitV3 from 31.16.3 to 31.17.0 ([a2933ed](https://github.com/Leberkas-org/TurboHTTP/commit/a2933ed059af7763db8b93c34929fc4f5f0c48aa))

## [2.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.3.0...v2.0.0) (2026-05-28)


### ⚠ BREAKING CHANGES

* delete RequestContext, TurboHttpContext, RoutingStage, and all custom routing types
* wire IHttpApplication through actors to ApplicationBridgeStage
* rewrite ApplicationBridgeStage as generic with IHttpApplication<TContext>
* delete app-framework layer
* implement TurboServer as IServer replacement
* remove ITurbo*Feature interfaces, use IHttp*Feature only
* replace FrameworkReference with targeted NuGet packages

* replace FrameworkReference with targeted NuGet packages ([bd25a46](https://github.com/Leberkas-org/TurboHTTP/commit/bd25a4680cf9b62e39bd0f114b2f04e0fd6b750d))


### Features

* add AddTurboServerInstrumentation registration methods ([4d156fb](https://github.com/Leberkas-org/TurboHTTP/commit/4d156fbf2d5e886f8a55f0f20cdc130ebddd234d))
* add CLI orchestration for stress benchmarks ([df87944](https://github.com/Leberkas-org/TurboHTTP/commit/df879440bfb11270c087c42cba60d6503a022498))
* add connection metrics and tracing to ListenerActor ([9ad8c7a](https://github.com/Leberkas-org/TurboHTTP/commit/9ad8c7a41fdc9b8b4d369bd8017f3d5e88c4349c))
* add fast-path slots for IHttpMaxRequestBodySizeFeature and IHttpBodyControlFeature ([ccfde96](https://github.com/Leberkas-org/TurboHTTP/commit/ccfde961fd515e1f7eaa543904f8368bb44c98cc))
* add FeatureCollectionFactory returning IFeatureCollection ([71110c6](https://github.com/Leberkas-org/TurboHTTP/commit/71110c69eddf29485d9fbeaab071ee4473f9f3c2))
* add four stress scenarios (slow-handler, connection-storm, body-flood, memory-endurance) ([727f28a](https://github.com/Leberkas-org/TurboHTTP/commit/727f28a5041d013e91d7a9f91af6f2416ba8e66d))
* add IHttpMaxRequestBodySizeFeature and IHttpBodyControlFeature implementations ([2e6c426](https://github.com/Leberkas-org/TurboHTTP/commit/2e6c426b1dc1e915463e1aac8af3031332678373))
* add LoadGenerator with concurrent worker loops ([5b50b78](https://github.com/Leberkas-org/TurboHTTP/commit/5b50b78839acb2e0cf17147769cc8ebe53f3048f))
* add MetricsCollector with per-second time-series aggregation ([a09b2a4](https://github.com/Leberkas-org/TurboHTTP/commit/a09b2a421bbe5958e6e12678663d3414ace6ea2f))
* add OTel-standard server metric instruments ([602eeb3](https://github.com/Leberkas-org/TurboHTTP/commit/602eeb3c0749acf7d26a77cb32a493ee9a65d9bd))
* add pipeline metrics and backpressure events to ApplicationBridgeStage ([b84bf7a](https://github.com/Leberkas-org/TurboHTTP/commit/b84bf7ad64afc25e00b0f5cd87442f3ec38d389e))
* add protocol negotiation metrics to ConnectionActor ([3879a89](https://github.com/Leberkas-org/TurboHTTP/commit/3879a8915d0ec3e5d023f12ab051db68438a15d2))
* add request metrics and tracing to HttpConnectionServerStageLogic ([6049d29](https://github.com/Leberkas-org/TurboHTTP/commit/6049d29b368e8ea5c19032d4e3abe04c934cc93f))
* add RequestTimestamp and RequestActivity to TurboFeatureCollection ([bc1e98a](https://github.com/Leberkas-org/TurboHTTP/commit/bc1e98a12eabf20221df7d960951756b98220ca9))
* add ResponsePipeWriter for writer-side header commit ([93d1bb9](https://github.com/Leberkas-org/TurboHTTP/commit/93d1bb9d13336c1fefced770912931e35e7074d4))
* Add server instrumentation methods ([251fcf2](https://github.com/Leberkas-org/TurboHTTP/commit/251fcf21327e0e4b8312e34f6dd38caaf99ee13b))
* add server-side Activity lifecycle (connection + request tracing) ([6989cfe](https://github.com/Leberkas-org/TurboHTTP/commit/6989cfe13aef5cf5ed21264379553035bab4ae82))
* add ServerHarness for Turbo/Kestrel lifecycle ([95cf049](https://github.com/Leberkas-org/TurboHTTP/commit/95cf0496b5f16a579bd4102bc0f704435c0b6c09))
* add Servus.Akka.AspNetCore with AkkaResults and MapEntity ([739a1a5](https://github.com/Leberkas-org/TurboHTTP/commit/739a1a581cdb97b1547fc9e1b4a4246a398671a1))
* add StressReport and JsonExporter for benchmark output ([855ed9d](https://github.com/Leberkas-org/TurboHTTP/commit/855ed9db414c37dbd9a23e72e6efd9e3a024243f))
* add turbo.server.* differenzierung metric instruments ([a826af1](https://github.com/Leberkas-org/TurboHTTP/commit/a826af13a351d2ac7f273b5c9cca64775734b75f))
* add TurboServer vs Kestrel server benchmarks ([a5eba3e](https://github.com/Leberkas-org/TurboHTTP/commit/a5eba3e0389fd8f602796014056f0100a644dafc))
* **client:** round-robin connection routing in GroupByRequestEndpointStage ([b180d21](https://github.com/Leberkas-org/TurboHTTP/commit/b180d21f5343e63e839eade8e018d812f491ba03))
* **e2e:** add End2EndSpecBase infrastructure for TurboHTTP client-server tests ([9051d71](https://github.com/Leberkas-org/TurboHTTP/commit/9051d715944b82cf8eb3a0ae778a11951921795e))
* **e2e:** add H1.1 StreamingSpec and PipeliningSpec ([8b5a32b](https://github.com/Leberkas-org/TurboHTTP/commit/8b5a32b7406cbbe291611ce18aa63500200ab31d))
* **e2e:** add H2 MultiplexingSpec, FlowControlSpec, UpgradeSpec ([156b9e1](https://github.com/Leberkas-org/TurboHTTP/commit/156b9e110444a6daf7b9a44b8b9ffe3eee7b8740))
* **e2e:** add H3 MultiplexingSpec ([85af05e](https://github.com/Leberkas-org/TurboHTTP/commit/85af05e5d4f5dfb15e2b291fb87cf60ab36b676f))
* **e2e:** add LargePayloadSpecs for all protocols ([0361829](https://github.com/Leberkas-org/TurboHTTP/commit/0361829b0dfc22eb49cbec843ef1834983d891f5))
* **e2e:** add ResilienceSpecs for all protocols ([43719c0](https://github.com/Leberkas-org/TurboHTTP/commit/43719c069d2acfe316bfeee4cd79ee57f8747e5b))
* **e2e:** add RoundtripSpecs for H1.0, H1.1, H2, H3 ([58b8a9c](https://github.com/Leberkas-org/TurboHTTP/commit/58b8a9ccf25e8a8bd4c9a915df469db9f9c60efb))
* **http3:** Stream 3 is unidirectional by default ([0609bd2](https://github.com/Leberkas-org/TurboHTTP/commit/0609bd261897bacf59bf70277193225db4eb782a))
* implement TurboServer as IServer replacement ([ebfb865](https://github.com/Leberkas-org/TurboHTTP/commit/ebfb865c58457644c64d153a0e626c09ef102e0f))
* register body size and body control features in FeatureCollectionFactory ([8850337](https://github.com/Leberkas-org/TurboHTTP/commit/88503375ed4abca5aaed8ebbbe6d7eb766987798))
* scaffold TurboHTTP.StressBenchmarks project with data records ([c619b2d](https://github.com/Leberkas-org/TurboHTTP/commit/c619b2d8ee2f179f51bd3c4622e772cef2eed595))
* **server:** auto-detect response ordering from HTTP version ([32192f7](https://github.com/Leberkas-org/TurboHTTP/commit/32192f7b44bd57619cefc860ce3d587db241890a))
* **server:** support unordered response emission in ApplicationBridgeStage ([dc819fc](https://github.com/Leberkas-org/TurboHTTP/commit/dc819fc7542f431343473c1ac09d7296e5f1bb88))


### Bug Fixes

* add missing PipeWriter overrides and leaveOpen to ResponsePipeWriter ([503c32b](https://github.com/Leberkas-org/TurboHTTP/commit/503c32bbffcfa6c5b257663776228cc73a9c08db))
* duplicate Content-Length in H1.1 server encoder + test fixes ([1880611](https://github.com/Leberkas-org/TurboHTTP/commit/18806116892e58b9037a8c263021d07fae66511b))
* **e2e:** add missing using directives and fix empty-echo response format ([7b96bd4](https://github.com/Leberkas-org/TurboHTTP/commit/7b96bd4c2441e5ffabc6e56f41af5342338edef5))
* **e2e:** skip H3 tests properly, reduce H11 pipelining concurrency ([e10ec98](https://github.com/Leberkas-org/TurboHTTP/commit/e10ec98e0b5cde2f64fcdfdfc8423e7e435cf4a5))
* **e2e:** use Results.Text for plain string assertions in ResilienceSpecs ([f4d0370](https://github.com/Leberkas-org/TurboHTTP/commit/f4d0370fc14a570d4c2fe0baac1f6edd7beae1fa))
* guard _headerCommit in CommitAndFlushAsync with try-finally ([730f9ef](https://github.com/Leberkas-org/TurboHTTP/commit/730f9ef1ebc0f8b741e9d927e6631dbc83ef66ea))
* **h2:** detect response body via HasStarted for H2 responses without Content-Length ([d84bcef](https://github.com/Leberkas-org/TurboHTTP/commit/d84bcef8005ea86e29f7c1749447fa18fb5e59fd))
* **h2:** detect response body via HasStarted when no Content-Length ([5ad82ad](https://github.com/Leberkas-org/TurboHTTP/commit/5ad82adb81a3a9c6a3f0c21dcf3d1ce593be1776))
* **h2:** sync HPACK decoder table size with announced SETTINGS + skip connection preface ([53205b7](https://github.com/Leberkas-org/TurboHTTP/commit/53205b79dd39756be9faadd430e71b9d995a52ea))
* populate IServerAddressesFeature with resolved endpoint URLs ([23ebbd5](https://github.com/Leberkas-org/TurboHTTP/commit/23ebbd53cb2f72741573494340ed33817bc714e0))
* resolve HTTP/2 and HTTP/3 response body encoding logic ([38f3c1d](https://github.com/Leberkas-org/TurboHTTP/commit/38f3c1d155afd22f57479fb560adbdfef44c8717))
* skip H2 connection preface in server FrameDecoder + fix client ActorSystem setup ([5b8736b](https://github.com/Leberkas-org/TurboHTTP/commit/5b8736b47d1e954a91a949eec3688850950181b8))
* update tests for RequestContext pipeline type ([754747f](https://github.com/Leberkas-org/TurboHTTP/commit/754747f64aa9e528c375279a86b38510ae9023ec))


### Performance

* batch HTTP/3 frame serialization into single TransportBuffer per request ([e7346bb](https://github.com/Leberkas-org/TurboHTTP/commit/e7346bb71d46b20280b5b451e66e5bfe169e1858))
* batch QPACK encoder instruction flushes in HTTP/3 client ([84e02d4](https://github.com/Leberkas-org/TurboHTTP/commit/84e02d4f4fa8c44852c3f74bd1428d657d0da1e2))
* coalesce queued outbound TransportData writes into single buffer ([cdaf83b](https://github.com/Leberkas-org/TurboHTTP/commit/cdaf83b0f35fbcc2e1bc1b8777b2ff0349009d6f))
* direct-push bypass and pre-sized queues in HttpConnectionStageLogic ([758300f](https://github.com/Leberkas-org/TurboHTTP/commit/758300fab2ac39fa0ae4771ee4f2e7d4d3c8d12a))
* increase H3 StreamState pool 16→256, reduce encoder buffer 8K→4K ([7585b11](https://github.com/Leberkas-org/TurboHTTP/commit/7585b1158f4d18f5c194d83b9a18ac9f55849c0b))
* reuse HeaderCollection in H1.1 encoder, increase benchmark pipeline depth ([ed65553](https://github.com/Leberkas-org/TurboHTTP/commit/ed65553ffe45f660631addb6c2bf418a8ac06e72))


### Documentation

* accept API surface changes from app-framework layer deletion ([5044639](https://github.com/Leberkas-org/TurboHTTP/commit/504463931159599a62a3f972e585fecdffe47429))
* add IServer pipeline redesign spec and implementation plan ([9db902d](https://github.com/Leberkas-org/TurboHTTP/commit/9db902d9b0776d94111292d763436bc6f6205ca3))
* escape angle brackets in generic types to fix Vue parser ([88261dc](https://github.com/Leberkas-org/TurboHTTP/commit/88261dc11a08120f4a9fc36d7f1c6d98498929e4))
* fix table rendering in installation and aspnet-core pages ([0004b9c](https://github.com/Leberkas-org/TurboHTTP/commit/0004b9c06c3ff9b08a7b10e04c8cbe8cf7a7722b))
* restructure server documentation for IServer architecture ([cc3b2b5](https://github.com/Leberkas-org/TurboHTTP/commit/cc3b2b5fa77e779a1f931a4633c8bc269398c988))
* update CLAUDE.md for IServer pipeline architecture ([09ea765](https://github.com/Leberkas-org/TurboHTTP/commit/09ea7651e722f58cc4802ca390f058286539b535))
* update landing page and scenarios for IServer architecture ([3dc1919](https://github.com/Leberkas-org/TurboHTTP/commit/3dc1919247b842d318c56da562a9cd6f66d63d6e))


### Refactoring

* delete app-framework layer ([fcdf04d](https://github.com/Leberkas-org/TurboHTTP/commit/fcdf04d49ec5eb7e71eb44cb550ab330b1af7fc4))
* delete RequestContext, TurboHttpContext, RoutingStage, and all custom routing types ([d60a68d](https://github.com/Leberkas-org/TurboHTTP/commit/d60a68d1ac3eb64472219f750dd9993f3d4ca992))
* **e2e:** add protocol collections for partial runs, skip timeout tests ([8a95029](https://github.com/Leberkas-org/TurboHTTP/commit/8a9502903aa32d7ff6ea63843a84324a5a9090e5))
* exclude integration server tests pending IServer rewrite ([c743555](https://github.com/Leberkas-org/TurboHTTP/commit/c743555f1f24887e66275444920da39e499c6035))
* make lifetime and identifier features self-contained ([c60be42](https://github.com/Leberkas-org/TurboHTTP/commit/c60be42c32d95f89174f676df7a832dfc0ed745e))
* remove ITurbo*Feature interfaces, use IHttp*Feature only ([55c5c12](https://github.com/Leberkas-org/TurboHTTP/commit/55c5c1290dcdc40e5fb7ab708150f2d257fe54d7))
* rewrite ApplicationBridgeStage as generic with IHttpApplication&lt;TContext&gt; ([7eb5117](https://github.com/Leberkas-org/TurboHTTP/commit/7eb5117156ebb625d4529404228500c0f54e543c))
* update all state machines and session managers to IFeatureCollection ([1a353af](https://github.com/Leberkas-org/TurboHTTP/commit/1a353afcbbbf3c566aea65de7923821bfa8a41f9))
* update protocol encoders to accept IFeatureCollection ([d386282](https://github.com/Leberkas-org/TurboHTTP/commit/d386282ae5791477d5567df93acd85b88c1adec2))
* update stage logic, connection stages, and engines to IFeatureCollection ([6940dce](https://github.com/Leberkas-org/TurboHTTP/commit/6940dce3258a3e0c70e864ca6e087463a54b89e5))
* Use lowercase property names in ProtocolVariant ([d5d6eef](https://github.com/Leberkas-org/TurboHTTP/commit/d5d6eef2912c6d7d496f593112273fd07de3361e))
* wire IHttpApplication through actors to ApplicationBridgeStage ([853f142](https://github.com/Leberkas-org/TurboHTTP/commit/853f1424bf288ba8879985a9b5a256239e94261a))

## [1.3.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.2.0...v1.3.0) (2026-05-26)


### Features

* add own collection interfaces and dual-implement on adapter classes ([7944d50](https://github.com/Leberkas-org/TurboHTTP/commit/7944d50a9ccf7f868e3aebcc85e004a4c6f8a771))
* add standalone ITurbo*Feature interfaces for ASP.NET Core decoupling ([f420515](https://github.com/Leberkas-org/TurboHTTP/commit/f4205154c11f85ee6ee95b483305c53a9688e0f2))
* complete ServerContextFactory migration to TurboFeatureCollection ([e9d48aa](https://github.com/Leberkas-org/TurboHTTP/commit/e9d48aab1869ab58de4732665d209d14a9469722))
* dual-implement all feature classes with both ASP.NET Core and own interfaces ([7bdaf92](https://github.com/Leberkas-org/TurboHTTP/commit/7bdaf9201b67108b740587110308d072ca0d63f2))
* migrate protocol layer from ASP.NET Core feature interfaces to own ITurbo* interfaces ([1b920c0](https://github.com/Leberkas-org/TurboHTTP/commit/1b920c0a67b15b520c01b009af5d353de131a1df))

## [1.2.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.1.0...v1.2.0) (2026-05-26)


### Features

* add Host property to TurboWebApplicationBuilder for full builder parity ([7977263](https://github.com/Leberkas-org/TurboHTTP/commit/79772639541da397ddf54845ef1884634e39b5ac))
* Add metadata support to TurboRouteHandlerBuilder and TurboRouteGroupBuilder ([ca7ee9c](https://github.com/Leberkas-org/TurboHTTP/commit/ca7ee9c17e5aa69ab1d4c85aa0ffa4076518d796))
* add TurboEndpointMetadata type with marker interfaces ([9eab9c8](https://github.com/Leberkas-org/TurboHTTP/commit/9eab9c86ecfa5b59c681c3dabd480f14641105b1))
* add TurboServerLimits, Listen(string url), ConfigureEndpointDefaults ([0a63b9d](https://github.com/Leberkas-org/TurboHTTP/commit/0a63b9dce780956cb7cd63ebc25b3d943fbd214e))
* deprecate MapTurbo*/UseTurbo* WebApplication extensions for 2.0 removal ([f81b3ed](https://github.com/Leberkas-org/TurboHTTP/commit/f81b3ed60380ecb569a540ad8a34db56c6217823))
* wire endpoint metadata from route registration through RoutingStage to TurboHttpContext ([e7a0cf7](https://github.com/Leberkas-org/TurboHTTP/commit/e7a0cf7f338f148b0259b05041221be891f241d1))


### Bug Fixes

* improve NotSupportedException messages for WebSockets and Session ([f1fab94](https://github.com/Leberkas-org/TurboHTTP/commit/f1fab949bb7b6c4766327f15136f65eb4bef21ab))
* use N * 1024 size literals in TurboServerOptions per CLAUDE.md ([dfef509](https://github.com/Leberkas-org/TurboHTTP/commit/dfef5096112cc52c0253d91298ced30c13421364))

## [1.1.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.2...v1.1.0) (2026-05-26)


### Features

* **ci:** Add docs build workflow ([1741c44](https://github.com/Leberkas-org/TurboHTTP/commit/1741c44f4a0009cf96f040319eb199aa588fae69))


### Bug Fixes

* **ci:** Remove docs push trigger ([3368641](https://github.com/Leberkas-org/TurboHTTP/commit/3368641ed0f5312b84851b8e59c664c862cec78a))
* Update lock file dependencies ([8399514](https://github.com/Leberkas-org/TurboHTTP/commit/8399514e9a8baed02d7b45b11bbb5a96793088fa))


### Documentation

* Add LikeC4 plugin and diagrams ([25f17bd](https://github.com/Leberkas-org/TurboHTTP/commit/25f17bd8d11322408ae877777e97a57c1a3e0288))

## [1.0.2](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.1...v1.0.2) (2026-05-25)


### Bug Fixes

* **docs:** Pin likec4 to 1.50.0 ([06c08fb](https://github.com/Leberkas-org/TurboHTTP/commit/06c08fb988c68b85b8b64c1ba9797e518fe94103))

## [1.0.1](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.0...v1.0.1) (2026-05-25)


### Bug Fixes

* **docs:** correct scenario snippets to use real TurboHTTP APIs ([69d4eb5](https://github.com/Leberkas-org/TurboHTTP/commit/69d4eb5d3e2438027a1cabd6da11bb641cb5f108))
* **docs:** pin likec4 to 1.50.0 to avoid icon resolver bug ([5b4ecff](https://github.com/Leberkas-org/TurboHTTP/commit/5b4ecff5987c9fedef5eaa9991fd33f9028334b2))


### Documentation

* add scenarios showcase page with all 5 scenarios ([40f1f9e](https://github.com/Leberkas-org/TurboHTTP/commit/40f1f9eea5c1a468026ee8d001eb0c21574967c8))
* add scenarios to VitePress navigation ([c7cab1b](https://github.com/Leberkas-org/TurboHTTP/commit/c7cab1be842b7eefdb15664234dd0967b329dc3c))

## [1.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.2...v1.0.0) (2026-05-25)


### ⚠ BREAKING CHANGES

* pipeline
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder()
* **server:** rewrite TurboWebApplication with static factories and interfaces

### Features

* **h2:** implement server-side HTTP/2 response trailers ([fc4df62](https://github.com/Leberkas-org/TurboHTTP/commit/fc4df6242b90facf9ebd010672af2450519b447e))
* **server:** add internal AddTurboKestrel overload accepting TurboServerOptions instance ([7903f13](https://github.com/Leberkas-org/TurboHTTP/commit/7903f131969d85f6ecdbdf59a3ee30cc3932f6f4))
* **server:** add ITurboEndpointRouteBuilder interface ([a9f86d4](https://github.com/Leberkas-org/TurboHTTP/commit/a9f86d4c2cb9658e674cdb803dea3bb195184dbd))
* **server:** add routing extension methods on ITurboEndpointRouteBuilder ([cc7d599](https://github.com/Leberkas-org/TurboHTTP/commit/cc7d599fae6f0235e6826aac301b4bbcbed711a8))
* **server:** add TurboUrlCollection as ICollection&lt;string&gt; wrapper ([9420f5d](https://github.com/Leberkas-org/TurboHTTP/commit/9420f5d0f82dd6877fa5025e30218c2f0b17ec3b))
* **server:** add TurboWebApplicationBuilder ([51efcf7](https://github.com/Leberkas-org/TurboHTTP/commit/51efcf710656f2bbee6d1c4a5bb212e806391198))
* **server:** expose Use, Run, Map, MapWhen directly on TurboWebApplication ([9d83e2f](https://github.com/Leberkas-org/TurboHTTP/commit/9d83e2fed274ef6fe074d592ce3e8686933d6064))


### Bug Fixes

* pipeline ([103f1ce](https://github.com/Leberkas-org/TurboHTTP/commit/103f1ce3a431ee06e34df74f69d55c7a9ea0a4e3))
* switch NuGet publish from Trusted Publishing to API key auth ([2f4bbad](https://github.com/Leberkas-org/TurboHTTP/commit/2f4bbad2ccdf79fee983ea7452e63839d38fc2c9))


### Refactoring

* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder() ([f4cf7af](https://github.com/Leberkas-org/TurboHTTP/commit/f4cf7af822489518c717bd148caab4d2a3d7f8e8))
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder ([cbfac65](https://github.com/Leberkas-org/TurboHTTP/commit/cbfac65c9b281a202ee278dc372808142f03d862))
* **server:** return ITurboPipelineBuilder from pipeline methods ([4fbbc72](https://github.com/Leberkas-org/TurboHTTP/commit/4fbbc72168e243678d705ebc5b7c4495cf971805))
* **server:** rewrite TurboWebApplication with static factories and interfaces ([fb4970e](https://github.com/Leberkas-org/TurboHTTP/commit/fb4970eecbb9d47744a1636b3cd94852bd7e70ea))

## [1.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.2...v1.0.0) (2026-05-25)


### ⚠ BREAKING CHANGES

* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder()
* **server:** rewrite TurboWebApplication with static factories and interfaces

### Features

* **h2:** implement server-side HTTP/2 response trailers ([fc4df62](https://github.com/Leberkas-org/TurboHTTP/commit/fc4df6242b90facf9ebd010672af2450519b447e))
* **server:** add internal AddTurboKestrel overload accepting TurboServerOptions instance ([7903f13](https://github.com/Leberkas-org/TurboHTTP/commit/7903f131969d85f6ecdbdf59a3ee30cc3932f6f4))
* **server:** add ITurboEndpointRouteBuilder interface ([a9f86d4](https://github.com/Leberkas-org/TurboHTTP/commit/a9f86d4c2cb9658e674cdb803dea3bb195184dbd))
* **server:** add routing extension methods on ITurboEndpointRouteBuilder ([cc7d599](https://github.com/Leberkas-org/TurboHTTP/commit/cc7d599fae6f0235e6826aac301b4bbcbed711a8))
* **server:** add TurboUrlCollection as ICollection&lt;string&gt; wrapper ([9420f5d](https://github.com/Leberkas-org/TurboHTTP/commit/9420f5d0f82dd6877fa5025e30218c2f0b17ec3b))
* **server:** add TurboWebApplicationBuilder ([51efcf7](https://github.com/Leberkas-org/TurboHTTP/commit/51efcf710656f2bbee6d1c4a5bb212e806391198))
* **server:** expose Use, Run, Map, MapWhen directly on TurboWebApplication ([9d83e2f](https://github.com/Leberkas-org/TurboHTTP/commit/9d83e2fed274ef6fe074d592ce3e8686933d6064))


### Refactoring

* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder() ([f4cf7af](https://github.com/Leberkas-org/TurboHTTP/commit/f4cf7af822489518c717bd148caab4d2a3d7f8e8))
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder ([cbfac65](https://github.com/Leberkas-org/TurboHTTP/commit/cbfac65c9b281a202ee278dc372808142f03d862))
* **server:** return ITurboPipelineBuilder from pipeline methods ([4fbbc72](https://github.com/Leberkas-org/TurboHTTP/commit/4fbbc72168e243678d705ebc5b7c4495cf971805))
* **server:** rewrite TurboWebApplication with static factories and interfaces ([fb4970e](https://github.com/Leberkas-org/TurboHTTP/commit/fb4970eecbb9d47744a1636b3cd94852bd7e70ea))

## [0.9.2](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.1...v0.9.2) (2026-05-24)


### Documentation

* update NuGet metadata and fix README links ([f4882d4](https://github.com/Leberkas-org/TurboHTTP/commit/f4882d4b552970bef3f44e449dfdae066b74b55d))

## [0.9.1](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.0...v0.9.1) (2026-05-24)


### Bug Fixes

* **ci:** upgrade Node to 22 and regenerate docs lockfile ([e599b78](https://github.com/Leberkas-org/TurboHTTP/commit/e599b781d2bd9a2e732345234480e11a966ed62f))


### Performance

* **routing:** eliminate per-request allocations in route matching ([5ac31d2](https://github.com/Leberkas-org/TurboHTTP/commit/5ac31d21cf1300a647b30515a85ed88b794503a3))
* **routing:** replace linear scan with dictionary lookup in RouteTable ([b3be40a](https://github.com/Leberkas-org/TurboHTTP/commit/b3be40a7d7a7683b271e0f59a6ac12ce77d394da))
* **server:** add server-side micro and throughput benchmarks ([b6bf348](https://github.com/Leberkas-org/TurboHTTP/commit/b6bf348b51bc0b3f20ee5c810e879918c0d8336d))


### Documentation

* Update README links to new organization ([770150c](https://github.com/Leberkas-org/TurboHTTP/commit/770150cfd0af3d295e1d202ef05e78984d5dffc4))


### Dependencies

* bump actions/download-artifact from 4 to 8 ([262114c](https://github.com/Leberkas-org/TurboHTTP/commit/262114ce2377df106bf34f0a9fe2a6d5cdb43aa4))
* bump actions/upload-artifact from 4 to 7 ([bdd7d51](https://github.com/Leberkas-org/TurboHTTP/commit/bdd7d518501ba6029111a34a766023a4ce1e3be0))
* Bump the akka group with 1 update ([4edbee8](https://github.com/Leberkas-org/TurboHTTP/commit/4edbee8a16572580be06385984939448aacaebd9))
* Bump Verify.XunitV3 from 31.16.3 to 31.17.0 ([a2933ed](https://github.com/Leberkas-org/TurboHTTP/commit/a2933ed059af7763db8b93c34929fc4f5f0c48aa))

## [0.9.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.8.0...v0.9.0) (2026-05-24)


### Features

* add code coverage to integration tests ([2f1d7bc](https://github.com/Leberkas-org/TurboHTTP/commit/2f1d7bcff531589e13372554a70bedcfbc479f22))
* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add QUIC transport implementation ([780d0c0](https://github.com/Leberkas-org/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/Leberkas-org/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/Leberkas-org/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/Leberkas-org/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/Leberkas-org/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* **body:** add GetBodyStream() to LineBased IBodyDecoder implementations ([f0578b1](https://github.com/Leberkas-org/TurboHTTP/commit/f0578b1cf0804fb5b2fb3e06bbd6d06a528c2965))
* **body:** add Stream-based Start/Create overloads to LineBased body encoders ([49c2e42](https://github.com/Leberkas-org/TurboHTTP/commit/49c2e42bf1cd139231107e42d18d3e7ea1ee8eb9))
* **body:** add Stream-based Start/Create overloads to Multiplexed body encoders ([0a97365](https://github.com/Leberkas-org/TurboHTTP/commit/0a97365554ec860db479a2adf4c655ca77f8e025))
* **context:** add WhenHeadersReady signal to TurboHttpResponseBodyFeature ([e604a6c](https://github.com/Leberkas-org/TurboHTTP/commit/e604a6cf146479f0c6abba289e5d5bb32f8117a8))
* **context:** create TurboRequestBodyFeature and align response body with Kestrel ([4738712](https://github.com/Leberkas-org/TurboHTTP/commit/473871230437f749c8119e6fd8459ac2ae294768))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **encoders:** add TurboHttpContext overloads to all 4 server encoders ([d314a86](https://github.com/Leberkas-org/TurboHTTP/commit/d314a86e611c3f7d60722d062e9d850fd7592538))
* **features:** add ITlsHandshakeFeature interface and implementation ([4207222](https://github.com/Leberkas-org/TurboHTTP/commit/42072228166b95f38017a154e6071a277065d75a))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/Leberkas-org/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/Leberkas-org/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/Leberkas-org/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/Leberkas-org/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/Leberkas-org/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/Leberkas-org/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/Leberkas-org/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/Leberkas-org/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/Leberkas-org/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/Leberkas-org/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))
* **http10:** add Http10ServerDecoder.GetRequestFeature() ([0804b1a](https://github.com/Leberkas-org/TurboHTTP/commit/0804b1a4158582c3c4f47cade4a214f352677a7b))
* **http11:** add h2c upgrade detection with IProtocolSwitchCapable signaling ([1e60d31](https://github.com/Leberkas-org/TurboHTTP/commit/1e60d3163e1bbadf92c67491799173c4704a2f4a))
* **http11:** start body encoder in server OnResponse for streaming support ([984c503](https://github.com/Leberkas-org/TurboHTTP/commit/984c503166845e226efabe68beb8233d665b954e))
* **http2:** add Http2ServerDecoder.DecodeHeadersToFeature() ([2f990ce](https://github.com/Leberkas-org/TurboHTTP/commit/2f990cebdc0abbd9dba607485b4d937b46a663fe))
* **http3:** add Http3ServerDecoder.DecodeHeadersToFeature() ([7d6a356](https://github.com/Leberkas-org/TurboHTTP/commit/7d6a35658dd884eb510e131756e2580ab65a6cad))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **lifecycle:** extract TLS metadata ([07fd711](https://github.com/Leberkas-org/TurboHTTP/commit/07fd71114421703629103b74ddbe6639cf20cb15))
* **protocol:** add HeaderRouter.ApplyToHeaderDictionary for flat header writing ([a839e74](https://github.com/Leberkas-org/TurboHTTP/commit/a839e74815d480e39d8ad763328eb815f6acb094))
* **protocol:** add ProtocolNegotiatingStateMachine with ALPN and preface detection ([befd348](https://github.com/Leberkas-org/TurboHTTP/commit/befd3489bd741ad2412b4df46f62865031d588ea))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **routing:** add Count property to EntityResponseMapperCollection ([fa65d3d](https://github.com/Leberkas-org/TurboHTTP/commit/fa65d3db0f5bd70bf9a3fa828266e56fb0e7cfac))
* **routing:** extend EntityMethodConfig with endpoint mappers and tell handler ([7891c56](https://github.com/Leberkas-org/TurboHTTP/commit/7891c56d89c0c5eb632be32c1d1e7772456ad433))
* **routing:** map new TLS options in EndpointResolver ([fcc8c6e](https://github.com/Leberkas-org/TurboHTTP/commit/fcc8c6e8d0a774d0faa92cc60e9f6c359b2a2cc2))
* **routing:** push response context on StartAsync before handler completes ([c968ee1](https://github.com/Leberkas-org/TurboHTTP/commit/c968ee123b15ef31d194b156ca3f7c8fe49fba5c))
* **routing:** update EntityDispatcher with two-tier mapper lookup and pluggable tell handler ([7bda4a1](https://github.com/Leberkas-org/TurboHTTP/commit/7bda4a172f887a75ea614f7cef3cb3d71edb5dfa))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** add ClientCertificateMode and ServerCertificateSelector to TurboHttpsOptions ([3ba4a8c](https://github.com/Leberkas-org/TurboHTTP/commit/3ba4a8c91fe6ee8ab3e159d36c17dd7df23f8951))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add DelayCertificate renegotiation support ([3874559](https://github.com/Leberkas-org/TurboHTTP/commit/3874559468e42b8edf027c08757d86a340a69098))
* **server:** add drain protocol to body decoders for request pipelining ([e16cfed](https://github.com/Leberkas-org/TurboHTTP/commit/e16cfedaad66a8ba7a3de948f7455c3ebe3834b5))
* **server:** add IHttpRequestLifetimeFeature, IHttpRequestIdentifierFeature, IHttpResetFeature ([fcfe851](https://github.com/Leberkas-org/TurboHTTP/commit/fcfe851609662941432bd45d5f1da13b0b941b1d))
* **server:** add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse ([ce010a3](https://github.com/Leberkas-org/TurboHTTP/commit/ce010a3c732b724c3ca5d7025896f7dcc935c72b))
* **server:** add minimal TurboHttpContext constructor for protocol-layer creation ([1b43a73](https://github.com/Leberkas-org/TurboHTTP/commit/1b43a7303defa39f8e88b1a02ecd7ad6fc07cfc3))
* **server:** add request tracking and content classification to protocol layer ([04829db](https://github.com/Leberkas-org/TurboHTTP/commit/04829dbdc34b966cad19860ca788ffb1e3226921))
* **server:** add TurboEntityAskBuilder with Response, Produces, and WithTimeout support ([a093d79](https://github.com/Leberkas-org/TurboHTTP/commit/a093d79809f6a6e648083cd46e618ac893dc02b8))
* **server:** add TurboEntityTellBuilder with Response and Produces support ([e069149](https://github.com/Leberkas-org/TurboHTTP/commit/e069149b187059798ec58402c6ec8c98169a1483))
* **server:** add TurboTlsCallbackOptions and TurboTlsCallbackContext ([a1ddcf7](https://github.com/Leberkas-org/TurboHTTP/commit/a1ddcf76e16571266dc3f87a66d4db56fd1afab5))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** add UseHttps(TurboTlsCallbackOptions) overload to TurboListenOptions ([1217894](https://github.com/Leberkas-org/TurboHTTP/commit/1217894ae8fcbaebc195ad9a84c40cce4a6489cd))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **server:** populate ITlsHandshakeFeature on HttpContext feature ([f50dd4c](https://github.com/Leberkas-org/TurboHTTP/commit/f50dd4c84be3f13c4ab8478c4ee7216e70b5f3db))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))
* **servus:** add PipeReaderSourceStage and StreamSource factory ([075c6b2](https://github.com/Leberkas-org/TurboHTTP/commit/075c6b20172b2df2ddf7f599b5d2cfeb4398c23f))
* **sse:** add AsEventStream extension for reactive SSE consumption ([3ac50ec](https://github.com/Leberkas-org/TurboHTTP/commit/3ac50ec4697855402d36db860e60804375e2a44e))
* **sse:** add ServerSentEvent and AsEventStream ([794a3c6](https://github.com/Leberkas-org/TurboHTTP/commit/794a3c66a8fc8fa6d949925ad28a8feb497e5c44))
* **sse:** implement ServerSentEvent parser GraphStage with full RFC compliance ([7742ec0](https://github.com/Leberkas-org/TurboHTTP/commit/7742ec0170c94cdfc1ef25dfd2dd10137862a30e))
* **streams:** add NegotiatingServerEngine ([1b01747](https://github.com/Leberkas-org/TurboHTTP/commit/1b017470ebabea0812fca9db04c2925c6bdfd03d))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** add IntegrationTests.E2E project with SSE round-trip tests ([83fa651](https://github.com/Leberkas-org/TurboHTTP/commit/83fa65140a2daf8a5bd8caa787df51e7e7d25f0a))
* **tests:** add IntegrationTests.Server project with basic HTTP tests ([0289df1](https://github.com/Leberkas-org/TurboHTTP/commit/0289df17666cbb323989135692679d145214ee7c))
* **tests:** add ServerTestContextBuilder for fluent test context creation ([b115828](https://github.com/Leberkas-org/TurboHTTP/commit/b1158288d9aff1837c4a53f2d2ac665688977064))
* **tests:** add TurboServerFixture for server and E2E integration tests ([05c4e49](https://github.com/Leberkas-org/TurboHTTP/commit/05c4e49078f1245c049415a0c5902d19bd5f0192))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **transport:** add ClientCertificateMode enum ([06de0c0](https://github.com/Leberkas-org/TurboHTTP/commit/06de0c0532c75d76130e74539f815b409958af0c))
* **transport:** add ClientCertificateMode, HandshakeCallback, ServerCertificateSelector ([a98c9be](https://github.com/Leberkas-org/TurboHTTP/commit/a98c9be99a7d3885b2a6de2402c09c75bd6bcdae))
* **transport:** add TlsHandshakeContext, TlsHandshakeCallback, TlsConnectionResult ([3ba8d70](https://github.com/Leberkas-org/TurboHTTP/commit/3ba8d70b6c1490d0b53213de608fa3313b6875bd))
* **transport:** add TransportTlsState inbound message for DelayCertificate ([98ee596](https://github.com/Leberkas-org/TurboHTTP/commit/98ee596603fe70f2ac053ab0518ac01834e5d070))
* **transport:** extend SecurityInfo with NegotiatedCipherSuite and HostName ([d38c319](https://github.com/Leberkas-org/TurboHTTP/commit/d38c319ac4ba442125798481ccdb2064664b25d4))
* **transport:** rewrite TcpListenerStage handshake with 3 paths ([a84d189](https://github.com/Leberkas-org/TurboHTTP/commit/a84d1898cbe35f098bac7f891d1b64833baefd81))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))
* **vault:** convert all 393 RFC section files to VAULT_STYLE_GUIDE-compliant Markdown ([b9c3a81](https://github.com/Leberkas-org/TurboHTTP/commit/b9c3a81cbab00b7b73d65b2737a8a7cf29bcd12b))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))
* checkout with lfs ([7a470ab](https://github.com/Leberkas-org/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **ci:** adjust release manifest directory ([44980a5](https://github.com/Leberkas-org/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/Leberkas-org/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/Leberkas-org/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* **h3:** enable QUIC/HTTP3 integration tests on Docker ([d940a60](https://github.com/Leberkas-org/TurboHTTP/commit/d940a60499afe142f31994a2090d1be35ed196c5))
* **h3:** improve control stream stability ([153a37b](https://github.com/Leberkas-org/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** open QUIC stream before sending request frames ([8ebd05d](https://github.com/Leberkas-org/TurboHTTP/commit/8ebd05dc4557c1a8f318b98d4a689d76e6f63a13))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/Leberkas-org/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **http2,http3:** treat absent Content-Length as streaming response with body ([bd4767e](https://github.com/Leberkas-org/TurboHTTP/commit/bd4767e9bd3557e7a61792873d506431de379c9e))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/Leberkas-org/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/Leberkas-org/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* minor fixes ([2d62179](https://github.com/Leberkas-org/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/Leberkas-org/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/Leberkas-org/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* public api changes ([2d6dfa4](https://github.com/Leberkas-org/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/Leberkas-org/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* **quic:** resolve deadlock in AcceptInboundStreamAsync test ([3e1eacb](https://github.com/Leberkas-org/TurboHTTP/commit/3e1eacb9819fd3e5c0b60b419f741bd49083eefe))
* **quic:** use IPEndPoint for IP address hosts in QuicClientProvider ([0d49238](https://github.com/Leberkas-org/TurboHTTP/commit/0d492383e48411ceb906fba187bef31ac43a7a57))
* **readme:** Correct workflow badges ([1392385](https://github.com/Leberkas-org/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* release please ([c1c7ae3](https://github.com/Leberkas-org/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))
* **release:** correct config paths ([a6ad77e](https://github.com/Leberkas-org/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))
* **request-feature:** ensure Host header fallback from RequestUri ([25979e1](https://github.com/Leberkas-org/TurboHTTP/commit/25979e19acb349bea394ab59faf32a27a7e83d68))
* reset release-please version to 0.8.0 ([3f465d8](https://github.com/Leberkas-org/TurboHTTP/commit/3f465d8c14af2df1d2c2e77061ac6b2fa35c2e2e))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **routing:** remove broken RequestBinder for HttpRequestMessage parameter type ([c3ec9c0](https://github.com/Leberkas-org/TurboHTTP/commit/c3ec9c0ef23867665a5493db63158014e6e71b95))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **server:** eliminate listener bind race condition via materialized Task ([b16dbc8](https://github.com/Leberkas-org/TurboHTTP/commit/b16dbc8dbcc4399427b1ac097a7f0cc73b249707))
* **server:** thread IServiceProvider and TurboConnectionInfo through server pipeline ([abce087](https://github.com/Leberkas-org/TurboHTTP/commit/abce0870d865b5600e489e799477c6f11b392d5c))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **sse:** align formatter with Kestrel's SseFormatter implementation ([a13b9ef](https://github.com/Leberkas-org/TurboHTTP/commit/a13b9eff960bd4fae3f825192f825f3d94406578))
* **sse:** align SSE formatter with WHATWG spec ([abaef51](https://github.com/Leberkas-org/TurboHTTP/commit/abaef511e0da16e8b063e46a9e36a2c555515352))
* **sse:** align SSE parser with WHATWG spec and skip SSE tests on Docker ([a46d70f](https://github.com/Leberkas-org/TurboHTTP/commit/a46d70f8f9d79b66cc8134db17e75df1589e01ee))
* **sse:** remove duplicate XML documentation in Extensions.cs ([0100f12](https://github.com/Leberkas-org/TurboHTTP/commit/0100f12a4faffb5dc593c0c0c277f1a5a3a11e82))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **tests:** add Materializer property to FakeServerOps and SwitchCapableOps ([83f9002](https://github.com/Leberkas-org/TurboHTTP/commit/83f9002af679d7f088d70aea94a6d9f28fd95e7d))
* **tests:** consolidate server test fakes and fix all server-side test failures ([d531035](https://github.com/Leberkas-org/TurboHTTP/commit/d53103517e29776e38ab8bbec44a45798715d321))
* **tests:** let Kestrel pick HTTPS port to avoid port conflicts ([7a8bfe0](https://github.com/Leberkas-org/TurboHTTP/commit/7a8bfe00e1807cec5116a522193341101e4b57f4))
* **tests:** Use 127.0.0.1 for H3 and parallelize tests ([9161dd0](https://github.com/Leberkas-org/TurboHTTP/commit/9161dd06516ad24960b782dcde1d72960c071088))
* **tests:** use fresh HttpClient to avoid connection pool reuse in timeout test ([be74295](https://github.com/Leberkas-org/TurboHTTP/commit/be742952ed5285221848304d8f2774f36cfee408))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/Leberkas-org/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/Leberkas-org/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/Leberkas-org/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/Leberkas-org/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/Leberkas-org/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/Leberkas-org/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/Leberkas-org/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/Leberkas-org/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))
* **server:** add Date and Content-Length header caches ([861c436](https://github.com/Leberkas-org/TurboHTTP/commit/861c4365e6ff75f7924a699f2312d5b87eede9c2))
* **server:** implement context pooling with reset semantics ([faf7300](https://github.com/Leberkas-org/TurboHTTP/commit/faf7300b7d5fd51c83359a79f04ee7b785bfe88e))
* **tests:** parallelize integration tests and fix H3 infrastructure ([0d93389](https://github.com/Leberkas-org/TurboHTTP/commit/0d9338915af97f7ad1623b49b38f671cb0441acc))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add dynamic protocol negotiation design spec ([ab58122](https://github.com/Leberkas-org/TurboHTTP/commit/ab58122bb80d5251958399019bf06fe133dbc420))
* add dynamic protocol negotiation implementation plan ([45ba63e](https://github.com/Leberkas-org/TurboHTTP/commit/45ba63eb9c5ad84b2901cddd0d6bb311de820c48))
* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add server test coverage map with risk-based prioritization ([63c5081](https://github.com/Leberkas-org/TurboHTTP/commit/63c5081d1549c054e7b29cbf1794ad164ad0de59))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* align LikeC4 models and docs with actual class names ([75a0a0f](https://github.com/Leberkas-org/TurboHTTP/commit/75a0a0f0e4e5bf7a2233baa96b181b55822a1dd3))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* **likec4:** consolidate model-server.c4 into model-pipeline.c4 ([7fdf75e](https://github.com/Leberkas-org/TurboHTTP/commit/7fdf75ef7809243e214841132c150f4faf4b4768))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* remove Extending the Pipeline pages ([16b0196](https://github.com/Leberkas-org/TurboHTTP/commit/16b0196b0219e003c87094e171aa5d3ad86a1203))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update ([968cbfe](https://github.com/Leberkas-org/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update LikeC4 ([478dbb4](https://github.com/Leberkas-org/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update Obsidian notes ([b933151](https://github.com/Leberkas-org/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))
* Update README with server features ([1f26bb8](https://github.com/Leberkas-org/TurboHTTP/commit/1f26bb80ab21762ae51853ac475a5bd7a22e70b5))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/Leberkas-org/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/Leberkas-org/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/Leberkas-org/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/Leberkas-org/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* Bump Akka.Streams, Akka.Streams.TestKit and Akka.TestKit.Xunit ([b2c3c36](https://github.com/Leberkas-org/TurboHTTP/commit/b2c3c362df304acc44ace4a3023d06da845845b0))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/Leberkas-org/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/Leberkas-org/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))
* Bump Microsoft.Testing.Extensions.CodeCoverage from 18.6.2 to 18.7.0 ([6d518f7](https://github.com/Leberkas-org/TurboHTTP/commit/6d518f707c119620bcbb819dfa236c5dd7911445))
* Bump Testcontainers from 4.11.0 to 4.12.0 ([780d25a](https://github.com/Leberkas-org/TurboHTTP/commit/780d25a4b06f79a181e566e5d89688f6394b78cd))
* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.7.1](https://github.com/Leberkas-org/TurboHTTP/compare/v0.7.0...v0.7.1) (2026-05-20)


### Documentation

* align LikeC4 models and docs with actual class names ([75a0a0f](https://github.com/Leberkas-org/TurboHTTP/commit/75a0a0f0e4e5bf7a2233baa96b181b55822a1dd3))
* **likec4:** consolidate model-server.c4 into model-pipeline.c4 ([7fdf75e](https://github.com/Leberkas-org/TurboHTTP/commit/7fdf75ef7809243e214841132c150f4faf4b4768))
* remove Extending the Pipeline pages ([16b0196](https://github.com/Leberkas-org/TurboHTTP/commit/16b0196b0219e003c87094e171aa5d3ad86a1203))
* Update README with server features ([1f26bb8](https://github.com/Leberkas-org/TurboHTTP/commit/1f26bb80ab21762ae51853ac475a5bd7a22e70b5))


### Dependencies

* Bump Akka.Streams, Akka.Streams.TestKit and Akka.TestKit.Xunit ([b2c3c36](https://github.com/Leberkas-org/TurboHTTP/commit/b2c3c362df304acc44ace4a3023d06da845845b0))
* Bump Microsoft.Testing.Extensions.CodeCoverage from 18.6.2 to 18.7.0 ([6d518f7](https://github.com/Leberkas-org/TurboHTTP/commit/6d518f707c119620bcbb819dfa236c5dd7911445))
* Bump Testcontainers from 4.11.0 to 4.12.0 ([780d25a](https://github.com/Leberkas-org/TurboHTTP/commit/780d25a4b06f79a181e566e5d89688f6394b78cd))

## [0.7.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.6.0...v0.7.0) (2026-05-20)


### Features

* **features:** add ITlsHandshakeFeature interface and implementation ([4207222](https://github.com/Leberkas-org/TurboHTTP/commit/42072228166b95f38017a154e6071a277065d75a))
* **http11:** add h2c upgrade detection with IProtocolSwitchCapable signaling ([1e60d31](https://github.com/Leberkas-org/TurboHTTP/commit/1e60d3163e1bbadf92c67491799173c4704a2f4a))
* **lifecycle:** extract TLS metadata ([07fd711](https://github.com/Leberkas-org/TurboHTTP/commit/07fd71114421703629103b74ddbe6639cf20cb15))
* **protocol:** add ProtocolNegotiatingStateMachine with ALPN and preface detection ([befd348](https://github.com/Leberkas-org/TurboHTTP/commit/befd3489bd741ad2412b4df46f62865031d588ea))
* **routing:** add Count property to EntityResponseMapperCollection ([fa65d3d](https://github.com/Leberkas-org/TurboHTTP/commit/fa65d3db0f5bd70bf9a3fa828266e56fb0e7cfac))
* **routing:** extend EntityMethodConfig with endpoint mappers and tell handler ([7891c56](https://github.com/Leberkas-org/TurboHTTP/commit/7891c56d89c0c5eb632be32c1d1e7772456ad433))
* **routing:** map new TLS options in EndpointResolver ([fcc8c6e](https://github.com/Leberkas-org/TurboHTTP/commit/fcc8c6e8d0a774d0faa92cc60e9f6c359b2a2cc2))
* **routing:** update EntityDispatcher with two-tier mapper lookup and pluggable tell handler ([7bda4a1](https://github.com/Leberkas-org/TurboHTTP/commit/7bda4a172f887a75ea614f7cef3cb3d71edb5dfa))
* **server:** add ClientCertificateMode and ServerCertificateSelector to TurboHttpsOptions ([3ba4a8c](https://github.com/Leberkas-org/TurboHTTP/commit/3ba4a8c91fe6ee8ab3e159d36c17dd7df23f8951))
* **server:** add DelayCertificate renegotiation support ([3874559](https://github.com/Leberkas-org/TurboHTTP/commit/3874559468e42b8edf027c08757d86a340a69098))
* **server:** add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse ([ce010a3](https://github.com/Leberkas-org/TurboHTTP/commit/ce010a3c732b724c3ca5d7025896f7dcc935c72b))
* **server:** add TurboEntityAskBuilder with Response, Produces, and WithTimeout support ([a093d79](https://github.com/Leberkas-org/TurboHTTP/commit/a093d79809f6a6e648083cd46e618ac893dc02b8))
* **server:** add TurboEntityTellBuilder with Response and Produces support ([e069149](https://github.com/Leberkas-org/TurboHTTP/commit/e069149b187059798ec58402c6ec8c98169a1483))
* **server:** add TurboTlsCallbackOptions and TurboTlsCallbackContext ([a1ddcf7](https://github.com/Leberkas-org/TurboHTTP/commit/a1ddcf76e16571266dc3f87a66d4db56fd1afab5))
* **server:** add UseHttps(TurboTlsCallbackOptions) overload to TurboListenOptions ([1217894](https://github.com/Leberkas-org/TurboHTTP/commit/1217894ae8fcbaebc195ad9a84c40cce4a6489cd))
* **server:** populate ITlsHandshakeFeature on HttpContext feature ([f50dd4c](https://github.com/Leberkas-org/TurboHTTP/commit/f50dd4c84be3f13c4ab8478c4ee7216e70b5f3db))
* **streams:** add NegotiatingServerEngine ([1b01747](https://github.com/Leberkas-org/TurboHTTP/commit/1b017470ebabea0812fca9db04c2925c6bdfd03d))
* **transport:** add ClientCertificateMode enum ([06de0c0](https://github.com/Leberkas-org/TurboHTTP/commit/06de0c0532c75d76130e74539f815b409958af0c))
* **transport:** add ClientCertificateMode, HandshakeCallback, ServerCertificateSelector ([a98c9be](https://github.com/Leberkas-org/TurboHTTP/commit/a98c9be99a7d3885b2a6de2402c09c75bd6bcdae))
* **transport:** add TlsHandshakeContext, TlsHandshakeCallback, TlsConnectionResult ([3ba8d70](https://github.com/Leberkas-org/TurboHTTP/commit/3ba8d70b6c1490d0b53213de608fa3313b6875bd))
* **transport:** add TransportTlsState inbound message for DelayCertificate ([98ee596](https://github.com/Leberkas-org/TurboHTTP/commit/98ee596603fe70f2ac053ab0518ac01834e5d070))
* **transport:** extend SecurityInfo with NegotiatedCipherSuite and HostName ([d38c319](https://github.com/Leberkas-org/TurboHTTP/commit/d38c319ac4ba442125798481ccdb2064664b25d4))
* **transport:** rewrite TcpListenerStage handshake with 3 paths ([a84d189](https://github.com/Leberkas-org/TurboHTTP/commit/a84d1898cbe35f098bac7f891d1b64833baefd81))


### Documentation

* add dynamic protocol negotiation design spec ([ab58122](https://github.com/Leberkas-org/TurboHTTP/commit/ab58122bb80d5251958399019bf06fe133dbc420))
* add dynamic protocol negotiation implementation plan ([45ba63e](https://github.com/Leberkas-org/TurboHTTP/commit/45ba63eb9c5ad84b2901cddd0d6bb311de820c48))

## [0.6.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.5.0...v0.6.0) (2026-05-20)


### Features

* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add QUIC transport implementation ([780d0c0](https://github.com/Leberkas-org/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/Leberkas-org/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/Leberkas-org/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/Leberkas-org/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/Leberkas-org/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/Leberkas-org/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/Leberkas-org/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/Leberkas-org/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/Leberkas-org/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/Leberkas-org/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/Leberkas-org/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/Leberkas-org/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/Leberkas-org/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/Leberkas-org/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/Leberkas-org/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))
* **vault:** convert all 393 RFC section files to VAULT_STYLE_GUIDE-compliant Markdown ([b9c3a81](https://github.com/Leberkas-org/TurboHTTP/commit/b9c3a81cbab00b7b73d65b2737a8a7cf29bcd12b))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))
* checkout with lfs ([7a470ab](https://github.com/Leberkas-org/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **ci:** adjust release manifest directory ([44980a5](https://github.com/Leberkas-org/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/Leberkas-org/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/Leberkas-org/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* **h3:** improve control stream stability ([153a37b](https://github.com/Leberkas-org/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/Leberkas-org/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/Leberkas-org/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/Leberkas-org/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* minor fixes ([2d62179](https://github.com/Leberkas-org/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/Leberkas-org/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/Leberkas-org/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* public api changes ([2d6dfa4](https://github.com/Leberkas-org/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/Leberkas-org/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* **readme:** Correct workflow badges ([1392385](https://github.com/Leberkas-org/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* release please ([c1c7ae3](https://github.com/Leberkas-org/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))
* **release:** correct config paths ([a6ad77e](https://github.com/Leberkas-org/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/Leberkas-org/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/Leberkas-org/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/Leberkas-org/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/Leberkas-org/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/Leberkas-org/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/Leberkas-org/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/Leberkas-org/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/Leberkas-org/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update ([968cbfe](https://github.com/Leberkas-org/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update LikeC4 ([478dbb4](https://github.com/Leberkas-org/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update Obsidian notes ([b933151](https://github.com/Leberkas-org/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/Leberkas-org/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/Leberkas-org/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/Leberkas-org/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/Leberkas-org/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/Leberkas-org/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/Leberkas-org/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))
* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.5.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.4.0...v0.5.0) (2026-05-20)


### Features

* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))


### Bug Fixes

* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))

## [0.4.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.3.0...v0.4.0) (2026-05-19)


### Features

* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Documentation

* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))


### Dependencies

* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.3.0](https://github.com/st0o0/TurboHTTP/compare/v0.2.0...v0.3.0) (2026-05-07)


### Features

* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/st0o0/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/st0o0/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/st0o0/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/st0o0/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/st0o0/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/st0o0/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/st0o0/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/st0o0/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/st0o0/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/st0o0/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))


### Bug Fixes

* checkout with lfs ([7a470ab](https://github.com/st0o0/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **h3:** improve control stream stability ([153a37b](https://github.com/st0o0/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/st0o0/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/st0o0/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/st0o0/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* public api changes ([2d6dfa4](https://github.com/st0o0/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/st0o0/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* release please ([c1c7ae3](https://github.com/st0o0/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/st0o0/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/st0o0/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/st0o0/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/st0o0/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/st0o0/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/st0o0/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/st0o0/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* update ([968cbfe](https://github.com/st0o0/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update LikeC4 ([478dbb4](https://github.com/st0o0/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))


### Dependencies

* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/st0o0/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))

## [0.3.0](https://github.com/st0o0/TurboHTTP/compare/v0.2.0...v0.3.0) (2026-05-07)


### Features

* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/st0o0/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/st0o0/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/st0o0/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/st0o0/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/st0o0/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/st0o0/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/st0o0/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/st0o0/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/st0o0/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/st0o0/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))


### Bug Fixes

* checkout with lfs ([7a470ab](https://github.com/st0o0/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **h3:** improve control stream stability ([153a37b](https://github.com/st0o0/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/st0o0/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/st0o0/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/st0o0/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* public api changes ([2d6dfa4](https://github.com/st0o0/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/st0o0/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/st0o0/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/st0o0/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/st0o0/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/st0o0/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/st0o0/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/st0o0/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/st0o0/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* update ([968cbfe](https://github.com/st0o0/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update LikeC4 ([478dbb4](https://github.com/st0o0/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))


### Dependencies

* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/st0o0/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))

## [0.2.0](https://github.com/st0o0/TurboHTTP/compare/v0.1.3...v0.2.0) (2026-05-04)


### Features

* add QUIC transport implementation ([780d0c0](https://github.com/st0o0/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/st0o0/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/st0o0/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/st0o0/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/st0o0/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))


### Bug Fixes

* **ci:** adjust release manifest directory ([44980a5](https://github.com/st0o0/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/st0o0/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/st0o0/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* minor fixes ([2d62179](https://github.com/st0o0/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/st0o0/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/st0o0/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* **readme:** Correct workflow badges ([1392385](https://github.com/st0o0/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* **release:** correct config paths ([a6ad77e](https://github.com/st0o0/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))


### Performance

* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/st0o0/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))


### Documentation

* update Obsidian notes ([b933151](https://github.com/st0o0/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/st0o0/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/st0o0/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/st0o0/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/st0o0/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/st0o0/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
