# Changelog

## [3.0.0-alpha.6](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha.5...v3.0.0-alpha.6) (2026-06-24)


### Features

* **body:** implement BodyPumpBase with credit system and adaptive budget ([bc18b6a](https://github.com/Leberkas-org/TurboHTTP/commit/bc18b6a615ff64b21b8092a3ef599108dae16fdc))
* **body:** unseal BodyDrainSlot, add FlowControlledDrainSlot ([415b81d](https://github.com/Leberkas-org/TurboHTTP/commit/415b81d1bef37f160034d92c009bb8b09c1d3692))
* **body:** update IBodyDrainTarget interface and remove DrainContinue ([9919a88](https://github.com/Leberkas-org/TurboHTTP/commit/9919a88330160934a99e6f18e185c62c794534b4))
* **body:** wire OnOutboundFlushed → AddCredit across all protocols ([4b80d6a](https://github.com/Leberkas-org/TurboHTTP/commit/4b80d6a2b68bdbbde1dd4957849bef2b7fbb444f))
* **client:** forward 1xx informational responses to caller ([0f4627e](https://github.com/Leberkas-org/TurboHTTP/commit/0f4627e9e32992357efafc9be406b6d4f5571aae))
* **h1-server:** add chunked trailer section encoding to ChunkedFramingHelper ([da0b1ba](https://github.com/Leberkas-org/TurboHTTP/commit/da0b1ba58dac21890894d35825fb2b69c2550dac))
* **h1-server:** integrate trailer emission into Http11ServerStateMachine ([c175b18](https://github.com/Leberkas-org/TurboHTTP/commit/c175b18db9f0400da9df54aed2d54f60458ec4a7))
* **h1-server:** support 1xx informational responses via SendInformational ([ae4aa17](https://github.com/Leberkas-org/TurboHTTP/commit/ae4aa1792b214a769978a7166e3458bd85759c7e))
* **h2-server:** support 1xx informational responses ([a104f9c](https://github.com/Leberkas-org/TurboHTTP/commit/a104f9ceef165813d5e6e8f839ce091a71286cad))
* **h2:** add Reserve/Refund to FlowController for window reservation ([e395b09](https://github.com/Leberkas-org/TurboHTTP/commit/e395b09c9ef93e312c312c545f60f7b1f9f1ef54))
* **h3-server:** add EncodeTrailers to Http3ServerEncoder ([ba7d586](https://github.com/Leberkas-org/TurboHTTP/commit/ba7d5867691efc44cafc0707ed45818d24cff91f))
* **h3-server:** integrate trailer emission into Http3ServerSessionManager ([8a50fd2](https://github.com/Leberkas-org/TurboHTTP/commit/8a50fd2dddd6b2b899c4f3cb6393de5248b603b3))
* **h3-server:** support 1xx informational responses ([9b4caab](https://github.com/Leberkas-org/TurboHTTP/commit/9b4caab62a10a6874f407dbfc9c99f6b37c25b75))
* Improve HTTP trailers and informational responses ([816cbf7](https://github.com/Leberkas-org/TurboHTTP/commit/816cbf79532effab9ee6328b3c1f2f139c10ad6a))
* Rename TurboResponseHeaderDictionary to TurboHeaderDictionary ([b69079d](https://github.com/Leberkas-org/TurboHTTP/commit/b69079d22d21c9cfe7fda6a5e754d6d0f177cd62))
* **server:** add IHttpRequestTrailersFeature support ([f3fca70](https://github.com/Leberkas-org/TurboHTTP/commit/f3fca70a1487204f1a142ae1ed661bfe14bb0cbb))
* **server:** add SetReadOnly freeze semantics to TurboResponseHeaderDictionary ([366de03](https://github.com/Leberkas-org/TurboHTTP/commit/366de03318ec87edc5f70c0f872fb060e7d26bfb))
* **server:** add TurboInformationalResponseFeature for 1xx responses ([985ee8e](https://github.com/Leberkas-org/TurboHTTP/commit/985ee8e4908fbb4272e5c11c0941e6dc19ecafe8))
* **server:** auto-send 100 Continue for Expect: 100-continue requests ([0b0c00f](https://github.com/Leberkas-org/TurboHTTP/commit/0b0c00f8a3bd0a28263b3caca58f5a22a46bf456))


### Bug Fixes

* **body:** add inline AddCredit to all EmitDataFrames implementations ([f52824b](https://github.com/Leberkas-org/TurboHTTP/commit/f52824b403f341ab4b2c4c29ede7b0568da422de))
* **body:** fix Cancel stale-id leak and document IsStreamEligible contract ([92524fc](https://github.com/Leberkas-org/TurboHTTP/commit/92524fc8cb06d4a530c09e48d62581351b54f38a))
* **body:** fix pump stall on async PipeReader streams ([71df2e2](https://github.com/Leberkas-org/TurboHTTP/commit/71df2e242a29d068a9489bf413b81a938277fb47))
* **body:** prevent double slot cleanup on drain complete + fix test ([5e09c43](https://github.com/Leberkas-org/TurboHTTP/commit/5e09c4306fd954817ec087f12defb872497953dc))
* **body:** reclaim credit after synchronous read completion ([26368c1](https://github.com/Leberkas-org/TurboHTTP/commit/26368c152812743f4afdf1cc52ac5ef1456f9560))
* **body:** restore client-side backpressure and remove sync credit reclaim ([85fcb87](https://github.com/Leberkas-org/TurboHTTP/commit/85fcb8753be6f2703907e95aa15ad1e5e34783e6))
* **client:** add MaxConcurrentStreams defense-in-depth warning in H2/H3 EncodeRequest ([98b9ac7](https://github.com/Leberkas-org/TurboHTTP/commit/98b9ac7bbc0ca95fc718989afeb376973df84523))
* **client:** dispose response when PendingRequest version has advanced ([d476f97](https://github.com/Leberkas-org/TurboHTTP/commit/d476f972ff7f019b8d8d343a48feb9bfc46b5375))
* **client:** revert response dispose — BroadcastHub shares objects ([4cc2306](https://github.com/Leberkas-org/TurboHTTP/commit/4cc23066575087b42c44e74402826d83c1b7ee79))
* **cookies:** enforce __Host- and __Secure- prefix rules (RFC 6265bis) ([9abed3a](https://github.com/Leberkas-org/TurboHTTP/commit/9abed3aa6a25d5d4e68a1675c19b40e61e434da1))
* **h1-client:** consume 101 in 1xx block instead of delivering as final response ([4f7a0d8](https://github.com/Leberkas-org/TurboHTTP/commit/4f7a0d8a3415a67ee2121e2b4e960982641deea9))
* **h1-client:** exclude 101 Switching Protocols from 1xx forwarding ([2541222](https://github.com/Leberkas-org/TurboHTTP/commit/25412228478962252883a09d1666c8bd8970f059))
* **h2-server:** prevent stream hang when all trailers are filtered + add SetReadOnly freeze ([4455ac8](https://github.com/Leberkas-org/TurboHTTP/commit/4455ac8e401354af5f90bf805644d9ed84105826))
* **h2/h3:** guard StackStreamStatePool against double-return ([abba72d](https://github.com/Leberkas-org/TurboHTTP/commit/abba72d3436e7c96b986edb2b156111f5a5356d0))
* **h2:** fix FlowControlledBodyPump OnWindowUpdate deadlock + window leak ([dff4cec](https://github.com/Leberkas-org/TurboHTTP/commit/dff4cec756268da8e26c649b3f516b0174796527))
* **h3-client:** handle 1xx informational responses without closing stream ([68423e5](https://github.com/Leberkas-org/TurboHTTP/commit/68423e5ccdd61750e8e872d77cfea5d8dea615f2))
* **h3-client:** update edge-case tests for 1xx behavior + status validation ([a3af20b](https://github.com/Leberkas-org/TurboHTTP/commit/a3af20bf1fa117486d77e389b0cfea25fd74b2af))
* **quic:** only flush streams that actually wrote to the pipe (not opening-buffer) ([01fcde9](https://github.com/Leberkas-org/TurboHTTP/commit/01fcde9f8b9af7bfc1a3678181559eae589ebf5c))
* **quic:** return pooled MultiplexedData after write + treat QuicException as graceful close ([5af56a5](https://github.com/Leberkas-org/TurboHTTP/commit/5af56a51feb416ef4f0e7674f669e7dfce69d053))
* **server:** cap _pendingSources queue in GroupByRequestEndpointStage ([c44a6fe](https://github.com/Leberkas-org/TurboHTTP/commit/c44a6fe9f488c2aca81b0bc516ec09e9e1d6e8c1))
* **test:** tighten Trailer announcement header assertion in Http11ServerTrailerSpec ([e482e5c](https://github.com/Leberkas-org/TurboHTTP/commit/e482e5c4e3a51d5bbe915b387e3c32c77eb20f59))
* **test:** update H1.1 stage test for 1xx forwarding behavior ([fea9d9d](https://github.com/Leberkas-org/TurboHTTP/commit/fea9d9d1c50483287971bc5970b58ae93cb4fed3))
* **test:** update Http3ServerTrailerSpec to use MultiplexedData.Rent() ([af9f6ca](https://github.com/Leberkas-org/TurboHTTP/commit/af9f6ca94ab10611130aa1dbc935412a09bd1104))
* **test:** update starvation guard tests after removal ([105307f](https://github.com/Leberkas-org/TurboHTTP/commit/105307f2f8cafd510d0a5ebb00968cf6a0fe805b))


### Performance

* **h3:** batch DATA frames into single TransportBuffer (match H2 pattern) ([c8d88fc](https://github.com/Leberkas-org/TurboHTTP/commit/c8d88fc67dc9c847adb021d02b2182e94658bead))
* **h3:** eliminate timer key strings, skip LinkedCTS, deduplicate correlation map ([44a492c](https://github.com/Leberkas-org/TurboHTTP/commit/44a492c4e340fc9dfa248a478b08477fdc7f48dc))
* **h3:** pool MultiplexedData like TransportData (ObjectPool&lt;256&gt;) ([39f3e86](https://github.com/Leberkas-org/TurboHTTP/commit/39f3e864419fb9b81c23513bc32a31f25e6bd43e))
* **quic:** adaptive read buffer 4KB-128KB for QUIC direct reads ([5d9642c](https://github.com/Leberkas-org/TurboHTTP/commit/5d9642c2196498650183850636de726233276ab3))
* **quic:** cache read failure/success lambdas and body pump delegates ([550254f](https://github.com/Leberkas-org/TurboHTTP/commit/550254fb1e16ecbcc5414713fe6bb26bf0c663c9))
* **quic:** defer PipeWriter flush until batch complete (write coalescing) ([508a27d](https://github.com/Leberkas-org/TurboHTTP/commit/508a27df7e3a8e4d9b787428f98af7838d062b36))
* **quic:** pool QuicStreamState objects (ObjectPool&lt;256&gt;) ([4951b73](https://github.com/Leberkas-org/TurboHTTP/commit/4951b7317dff20b9346e849c526a6e8a110bd013))


### Refactoring

* **body:** rewrite FlowControlledBodyPump on BodyPumpBase ([66e024e](https://github.com/Leberkas-org/TurboHTTP/commit/66e024e0847e2e3429afd765b34ac09b763cfe79))
* **body:** rewrite MultiplexedBodyPump on BodyPumpBase ([1f07721](https://github.com/Leberkas-org/TurboHTTP/commit/1f07721cdabe1a01f406ef1282cbcdb10a3f75cf))
* **body:** rewrite SerialBodyPump on BodyPumpBase ([338c467](https://github.com/Leberkas-org/TurboHTTP/commit/338c467a7039c6e0700e8d454f3a0ce0eda45b45))
* **body:** simplify pump architecture ([0f1523b](https://github.com/Leberkas-org/TurboHTTP/commit/0f1523b1d32050cb24cd5c39bc24a39bf027c193))

## [3.0.0-alpha.5](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha.4...v3.0.0-alpha.5) (2026-06-22)


### Features

* **servus.akka:** Update subproject commit ([d8f1974](https://github.com/Leberkas-org/TurboHTTP/commit/d8f197491b304c8717e87887f6d2d26343f036df))


### Bug Fixes

* **client:** isolate per-request enrichment failures from the shared ingress ([4feb34a](https://github.com/Leberkas-org/TurboHTTP/commit/4feb34a6ed04571ecd8f1f65ad9615243570f098))
* **h10:** set Content-Length on large payload endpoints to prevent truncation ([7184724](https://github.com/Leberkas-org/TurboHTTP/commit/718472437cc0f7845f53453a53f6ee6a00d69c21))
* **h1:** retain unconsumed prefix across reads so split response headers don't desync ([52e0123](https://github.com/Leberkas-org/TurboHTTP/commit/52e01232d1feb74f8eea44cb90ee26046c910819))
* **h2:** bound the receive WINDOW_UPDATE threshold to the advertised stream window ([99aa90c](https://github.com/Leberkas-org/TurboHTTP/commit/99aa90c67d0f11e1250e5d3fce1a98d9b8500bf1))
* **h2:** drain in-flight streams on a graceful GOAWAY instead of dropping them (RFC 9113 6.8) ([b6da569](https://github.com/Leberkas-org/TurboHTTP/commit/b6da5697b8f44a015822bba3e13cb5d187d0cb1a))
* **h3:** drive reconnect from the state machine like TCP ([7150963](https://github.com/Leberkas-org/TurboHTTP/commit/71509639576fd646137ddc74eaa8b73fa455c4e0))


### Performance

* **body:** override QueuedBodyStream.CopyToAsync to write pooled chunks directly ([ff3468b](https://github.com/Leberkas-org/TurboHTTP/commit/ff3468bb5d4112e8c085cde35078d2b2aa2f21e8))
* **body:** rent body buffers from a shared cross-thread pool ([89dac37](https://github.com/Leberkas-org/TurboHTTP/commit/89dac3795506ce18ebdb0f1e8a3c9c7fc11c4233))
* **client:** dispose the channel-path default-timeout source on completion ([ffdc864](https://github.com/Leberkas-org/TurboHTTP/commit/ffdc864ccaa58c640e19b46c3c48f79eb9f5ed8c))
* **h1:** coalesce buffered H1.1 response headers and body into one outbound ([e496637](https://github.com/Leberkas-org/TurboHTTP/commit/e49663779fb83d4172293c2ad583becf75ea9894))
* **h1:** drop per-message header-iteration allocations ([ba79c83](https://github.com/Leberkas-org/TurboHTTP/commit/ba79c830b3a23bb209a768c1b513f0211b27f855))
* **h1:** scan response headers once instead of three passes ([456b9a4](https://github.com/Leberkas-org/TurboHTTP/commit/456b9a4ecbf50597e75f52cd44d3551e233a94ea))
* **h1:** single vectorized two-byte CRLF search in BufferSearch.FindCrlf ([49562db](https://github.com/Leberkas-org/TurboHTTP/commit/49562db9ce299759198e53ffa4dc02a2f4766dd2))
* **h1:** single-value header fast paths drop per-message allocations ([0b7d0b1](https://github.com/Leberkas-org/TurboHTTP/commit/0b7d0b155b8b5b104e17c8cca7a7e3c555a191f2))
* **h1:** write pre-encoded u8 version bytes in the request line ([d072d9f](https://github.com/Leberkas-org/TurboHTTP/commit/d072d9f6554f01c5802f1b4e56d8d840f30169c8))
* **h2-server:** batch DATA frames into single TransportBuffer ([30156ea](https://github.com/Leberkas-org/TurboHTTP/commit/30156ea69f990ab32f4db5e5f7c894e537ace837))
* **h2-server:** defer UpgradeToPipe for buffered async handlers ([639abdf](https://github.com/Leberkas-org/TurboHTTP/commit/639abdf2498932bfe036e7f27a3d9e63575a026f))
* **h2-server:** lazy Pipe upgrade on explicit write/flush instead of eager ([ea9ba40](https://github.com/Leberkas-org/TurboHTTP/commit/ea9ba40246b5a766fe1c7d00de9da874e19b42bf))
* **h2:** FrameDecoder.Decode returns its reused frame list (no per-read array alloc) ([d1a379d](https://github.com/Leberkas-org/TurboHTTP/commit/d1a379d5e79129b7ea579147e7a4ffaae0997563))
* **h2:** iterate stream windows in place on SETTINGS window-size change ([d5cc998](https://github.com/Leberkas-org/TurboHTTP/commit/d5cc998ed02a0c9a1c01d9078ada52c817aa2d16))
* **hpack:** decode Huffman over a flat struct-array tree ([9e62516](https://github.com/Leberkas-org/TurboHTTP/commit/9e6251669fbde61dc7a572515ee4826092ad5606))
* **hpack:** reuse computed UTF-8 byte lengths when adding to the dynamic table ([10c8956](https://github.com/Leberkas-org/TurboHTTP/commit/10c8956a8a06958bcc553dc5c98044a5f52de561))
* **quic:** bump servus.akka — direct-read + stream-type ordering fix ([63f42a6](https://github.com/Leberkas-org/TurboHTTP/commit/63f42a637318343937e6bfc5054e511966ca36fb))
* **server:** tune connection materializer input buffer to 32/128 ([5903794](https://github.com/Leberkas-org/TurboHTTP/commit/590379444c077d72e227e04c9d47205e609b80a8))
* **servus.akka:** LIFO connection pool for warmer TCP reuse ([37ad9fe](https://github.com/Leberkas-org/TurboHTTP/commit/37ad9fefc980a290c5b48d8b42c4598b45fb3bed))
* **servus.akka:** QUIC streams use PipeReader/Writer.Create instead of Task.Run loops ([4e51a08](https://github.com/Leberkas-org/TurboHTTP/commit/4e51a0854ec0cfecea572134f837447a403c3362))
* **transport:** bounded sync fast-path for PipeReader.ReadAsync ([feec226](https://github.com/Leberkas-org/TurboHTTP/commit/feec2263bbb72f6f753f53f06fc32798fb966d8b))


### Documentation

* replace stale ConnectionStage references with ConnectionActor ([ed90760](https://github.com/Leberkas-org/TurboHTTP/commit/ed907602d1d3e862c350d18ad361ea6653d41e3b))
* Update benchmark context and use cases ([e7c0347](https://github.com/Leberkas-org/TurboHTTP/commit/e7c0347fa8ee3eabd94baa29f3588c92425ceb2b))
* update CLAUDE.md decode-buffer note to match FrameDecoder.Decode contract ([5848a24](https://github.com/Leberkas-org/TurboHTTP/commit/5848a24ed373c15535e0c1f57c0310c8d575f8a0))


### Refactoring

* **client:** remove dead dispatcher-sizing computation ([457b8f8](https://github.com/Leberkas-org/TurboHTTP/commit/457b8f8dc10b93103d90e14703132fc11f21ff5a))

## [3.0.0-alpha.4](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha.3...v3.0.0-alpha.4) (2026-06-19)


### Features

* **benchmarks:** add client-side benchmarks (--protocol client-h1|h2|h3) ([c016e15](https://github.com/Leberkas-org/TurboHTTP/commit/c016e153cbe6b1b099e5f8d1e2ddf4f83498627f))
* **benchmarks:** add H2 and H3 loadtest support (--protocol h2|h3) ([961156d](https://github.com/Leberkas-org/TurboHTTP/commit/961156da17965f7e9c55a2886ad7ea03d509e748))
* **benchmarks:** add in-memory state-machine benchmarks (--protocol mem-h1|mem-h2) ([04eef35](https://github.com/Leberkas-org/TurboHTTP/commit/04eef35d6b3b1119f5cf6c0585fc61f198d86246))
* **benchmarks:** add per-type allocation profiling via BDN custom exporter ([a1cd11c](https://github.com/Leberkas-org/TurboHTTP/commit/a1cd11c9446a34d0bc9362085f2604bda333549d))
* **body:** add generic IBodyDrainTarget&lt;T&gt;, BodyDrainSlot&lt;T&gt;, BodyPumpHelper, ChunkedFramingHelper ([adc53da](https://github.com/Leberkas-org/TurboHTTP/commit/adc53da5fd687173117514b835000b85f51eb0e6))
* **body:** add IResettable to inbound types, create BodyReaderPoolExtensions ([79120c2](https://github.com/Leberkas-org/TurboHTTP/commit/79120c2721bbf192170d2ff72d823288107a2b8b))
* **body:** add SerialBodyPump for H1.x body drain with capacity model and starvation guard ([3ef519e](https://github.com/Leberkas-org/TurboHTTP/commit/3ef519e66e93fcd5da982526de90ce77c4713387))
* **body:** add shared drain interfaces, messages, and slot types ([ad4d99f](https://github.com/Leberkas-org/TurboHTTP/commit/ad4d99ffc84ce931896eee79fb7fdaf5a737b1fd))
* **body:** refactor pumps to generic BodyDrainSlot&lt;T&gt; + BodyPumpHelper ([1237e9d](https://github.com/Leberkas-org/TurboHTTP/commit/1237e9d56ed9478fcbf50da53f3425cb291ebdb7))
* **docs:** Add 'When to Use' page and nav link ([bd5c6f3](https://github.com/Leberkas-org/TurboHTTP/commit/bd5c6f3b9057e3819b19b0ac184a41e16d1a8eca))
* **h2:** add BodyDrainScheduler with AIMD, limbo zero-copy, and slot pooling ([c020598](https://github.com/Leberkas-org/TurboHTTP/commit/c02059832e11ec7edb5c448c8e0b2904f4486031))
* **h2:** add GetStreamSendWindow to FlowController ([c57d65b](https://github.com/Leberkas-org/TurboHTTP/commit/c57d65bb8d0b35b17b6a100c01e9b9e9880e88a8))
* **h3:** add MultiplexedBodyPump with fixed cap, slot pooling, and starvation guard ([212ffb1](https://github.com/Leberkas-org/TurboHTTP/commit/212ffb1bae92a69bbef90c78f9aac95fbb48879c))
* **lifecycle:** ClientStreamManager differentiated supervisor, Watch+Terminated cleanup ([737be81](https://github.com/Leberkas-org/TurboHTTP/commit/737be8109508ed8ff82ec6a01814f879c4267ebc))
* **lifecycle:** ServerSupervisorActor failure responses, drain timeout, Watch+Terminated ([1baad6d](https://github.com/Leberkas-org/TurboHTTP/commit/1baad6d42fdac7c491972aee3cf5495c76097e4a))
* **pooling:** add Microsoft.Extensions.ObjectPool primitive (IResettable + policy) ([cbad4d6](https://github.com/Leberkas-org/TurboHTTP/commit/cbad4d610eb7b664ed8fb4491146247cbd74528a))
* **pooling:** expose per-connection PoolContext on the server stage seam ([af2c9ec](https://github.com/Leberkas-org/TurboHTTP/commit/af2c9eccdae2ecd54f287c5ea5b4d062d21d844a))
* **pooling:** per-connection ConnectionPoolContext ([2245c7f](https://github.com/Leberkas-org/TurboHTTP/commit/2245c7f55b4ed774d59a9fa91fc042f2046b8d57))
* **server:** Add ReceiveBufferHint option ([6ebd4e3](https://github.com/Leberkas-org/TurboHTTP/commit/6ebd4e39cd888c6b720a629f2b953ae18eb3c237))
* **server:** handle ListenersFailed and DrainComplete.TimedOut in TurboServer ([93ea515](https://github.com/Leberkas-org/TurboHTTP/commit/93ea5151036a30cf1889fa83f5e407d00835647d))


### Bug Fixes

* **benchmarks:** add missing [GlobalSetup] to cold-start benchmark classes ([54d85ac](https://github.com/Leberkas-org/TurboHTTP/commit/54d85acebca5016a659bcd3da9d23ba42fcf6fd1))
* **benchmarks:** clean up old trace files ([1557008](https://github.com/Leberkas-org/TurboHTTP/commit/15570080f8e40f9c98bd304eb8e9000cb6ae825c))
* **benchmarks:** prevent ActorSystem shutdown during BDN warmup iterations ([8de9fd1](https://github.com/Leberkas-org/TurboHTTP/commit/8de9fd18c0747b1f506730977f8580d0b382c885))
* **benchmarks:** use per-connection SocketsHttpHandler for H2/H3 driver ([3d86d7b](https://github.com/Leberkas-org/TurboHTTP/commit/3d86d7bde34e1cceb2b76da7283c7c6846ed3916))
* **body:** retain HttpContent reference in drain slots to prevent GC disposal race ([9f3fa07](https://github.com/Leberkas-org/TurboHTTP/commit/9f3fa0730ed8063b70b490f33fd50361519e12f2))
* **body:** SerialBodyPump Cancel should not fire OnDrainComplete ([2491a11](https://github.com/Leberkas-org/TurboHTTP/commit/2491a11f28b21ef7e8d7e48f3432ccd8324680fa))
* **client:** bound HTTP/1.1 request body pump with outbound flush backpressure ([c76e9ee](https://github.com/Leberkas-org/TurboHTTP/commit/c76e9ee6f081bd0394aa8618a1fdf508ff3d9650))
* **client:** Correct request cancellation logic ([6c1b390](https://github.com/Leberkas-org/TurboHTTP/commit/6c1b3904796b2b3ed81dbd9de89d0122ce0b4dd5))
* **client:** decouple substream source creation from upstream pull gating ([7d0cb71](https://github.com/Leberkas-org/TurboHTTP/commit/7d0cb71ed6906b87371eefa19fec83f007e836ac))
* **client:** don't retry non-rewindable bodies; stop parked retries blocking intake ([a468db0](https://github.com/Leberkas-org/TurboHTTP/commit/a468db04c9b777097a8ef57204d22204c5c26372))
* **client:** resolve multi-connection pipeline deadlock in GroupByRequestEndpointStage ([0549940](https://github.com/Leberkas-org/TurboHTTP/commit/054994000b4200a001444803ac1f705d70395250))
* **client:** route responses by compacting consumer index to fix same-name misroute ([f16d3a7](https://github.com/Leberkas-org/TurboHTTP/commit/f16d3a76bc0dc0787d9fca4c25e19d0af9fdcb89))
* **client:** stamp cancellation token into request options before channel enqueue ([28d422d](https://github.com/Leberkas-org/TurboHTTP/commit/28d422dc342bb65c3fbc8d1c1daff639e40b6f37))
* **h10-client:** use streaming CloseDelimitedFramingDecoder for connection-close bodies ([d06db52](https://github.com/Leberkas-org/TurboHTTP/commit/d06db5247fe5a49856f62eca5989843ef65facb4))
* **h2:** Pass request cancellation to body drain ([5e60f59](https://github.com/Leberkas-org/TurboHTTP/commit/5e60f592f56fbbd5e06fb66e5ee2d7001624a7ce))
* **h2:** prevent GC pressure from body buffering under high concurrency ([7cc0841](https://github.com/Leberkas-org/TurboHTTP/commit/7cc0841c29a705276f3ba9daeb802ffce2f94664))
* **h3:** dispose decoded frames and copy blocked QPACK header blocks ([e51ab5d](https://github.com/Leberkas-org/TurboHTTP/commit/e51ab5daca6cc913289d94e9aef3aa58656f45dc))
* **http11:** Optimize request body pumping for synchronous reads ([7db4edf](https://github.com/Leberkas-org/TurboHTTP/commit/7db4edf3b84a31af3365ac216f2d06bd75911bb6))
* **http1:** stop chunked decoder duplicating a stashed partial control line ([a1a58a5](https://github.com/Leberkas-org/TurboHTTP/commit/a1a58a5793870edb47ed338aa14ab250b413fc55))
* **http1:** suppress response body for HEAD requests ([42f3637](https://github.com/Leberkas-org/TurboHTTP/commit/42f36378317eaeb1cfda232e44b9cdd9baae851c))
* **http2:** enforce the advertised HEADER_TABLE_SIZE in the server HPACK decoder ([4ad285c](https://github.com/Leberkas-org/TurboHTTP/commit/4ad285c5cfddadf1d0547923566c7086805af808))
* **http2:** RFC 9113 frame compliance — settings overflow, padded DATA flow control, half-closed(remote) ([d76c134](https://github.com/Leberkas-org/TurboHTTP/commit/d76c1340089c68829472223be4f4ad09ff1c975a))
* **http2:** size the HPACK encode buffer to the header block ([38625ab](https://github.com/Leberkas-org/TurboHTTP/commit/38625ab9586afaea8d5eaea6ebdbd6b696ae4875))
* **http2:** stop returning in-use body-drain buffers to the shared pool on teardown ([19b83c5](https://github.com/Leberkas-org/TurboHTTP/commit/19b83c57b18c91836da5484dbbe444d0c8c1f3bf))
* **http2:** suppress response body (DATA frames) for HEAD requests ([26b7841](https://github.com/Leberkas-org/TurboHTTP/commit/26b7841421a53f2db772064de6230f56372a503c))
* **http3:** process inbound QPACK encoder-stream and guard response body-drain UAF (server) ([ea45c9c](https://github.com/Leberkas-org/TurboHTTP/commit/ea45c9c139fdeeafbf87878024caa5a080c04803))
* **http3:** replay DATA received while response HEADERS are QPACK-blocked ([ab401a8](https://github.com/Leberkas-org/TurboHTTP/commit/ab401a839a96454da5841d81cebd7f00df9f93cb))
* **http3:** surface malformed frame bodies as HttpProtocolException ([c0a0462](https://github.com/Leberkas-org/TurboHTTP/commit/c0a04622233041a2fff17f9044230a478734c878))
* **http:** enforce body-size limit on Content-Length framed bodies ([025fc6b](https://github.com/Leberkas-org/TurboHTTP/commit/025fc6baf7813edbcb45df2d8963b0fa9c0fcd60))
* **http:** tighten Content-Length parsing, dup CL on H1.0, redirect credential leak ([fd16691](https://github.com/Leberkas-org/TurboHTTP/commit/fd16691e30b3cfa661edffddb845edaeead81659))
* **lifecycle:** ConnectionActor failure handler and PostStop cleanup ([327a54f](https://github.com/Leberkas-org/TurboHTTP/commit/327a54f5246fa3ce64303b40ff1ea6100e90a2e6))
* **lifecycle:** consistent full exception logging in CleanupForRetry ([0187c1d](https://github.com/Leberkas-org/TurboHTTP/commit/0187c1d052773163f56c2bf5f48e316ea027bdb2))
* **lifecycle:** Consumer stops on sink error instead of continuing ([9fe8885](https://github.com/Leberkas-org/TurboHTTP/commit/9fe88851d68679f8d675c0849f3dddd460136d54))
* **lifecycle:** ListenerActor PipeTo throw bug, BindFailed message, supervisor logging ([4e07e07](https://github.com/Leberkas-org/TurboHTTP/commit/4e07e07f82be3111f68e0b58976d7397913fc6c9))
* **lifecycle:** StreamOwner stops after retry exhaustion, actor-scoped materializer ([4ef77cd](https://github.com/Leberkas-org/TurboHTTP/commit/4ef77cde3323da5be40d96e5477a1d35e50c3b62))
* revert Servus 0.34.1 bump — caused 90% throughput regression ([7b140f9](https://github.com/Leberkas-org/TurboHTTP/commit/7b140f94c77b7afc1614f20f19edb037be3ea1a4))
* security hardening, body pump redesign, perf optimizations, and lifecycle reliability ([#42](https://github.com/Leberkas-org/TurboHTTP/issues/42)) ([c0f216e](https://github.com/Leberkas-org/TurboHTTP/commit/c0f216e150130a29f31603ee25ae546cda9d117a))
* **server:** await ActorSystem WhenTerminated in StopAsync, dispose responses in E2E tests ([bb87acc](https://github.com/Leberkas-org/TurboHTTP/commit/bb87acc7a227c2ef2e12e261a10b8962c4a13942))
* **server:** bound the cleartext protocol-sniffing window (DoS) ([d82c37f](https://github.com/Leberkas-org/TurboHTTP/commit/d82c37f74335591f664c84bb2fb10a89e16c20bb))
* **server:** bound the routing Pending queue by propagating backpressure ([c2965a8](https://github.com/Leberkas-org/TurboHTTP/commit/c2965a846e6089a94dc23d0bf57cc24a7c859165))
* **server:** check the cleartext sniff cap after protocol identification, not before ([ab3a40c](https://github.com/Leberkas-org/TurboHTTP/commit/ab3a40c8bb438826752d2f6da713ba9f84ab8543))
* **server:** defer H2 per-stream WINDOW_UPDATE until app consumes body ([5bd80aa](https://github.com/Leberkas-org/TurboHTTP/commit/5bd80aaffbdaea018b17bf752b3c64ff6c9dd5a7))
* **server:** empty-body responses, outbound queue drain, body-message ShouldComplete ([94332f4](https://github.com/Leberkas-org/TurboHTTP/commit/94332f4c05e0544896289f42ede27f6977010acb))
* **server:** guard buffered body fast path and serve single-segment bodies zero-copy ([3358b02](https://github.com/Leberkas-org/TurboHTTP/commit/3358b0252e5b36337fda83bbb17dc5c516319ae6))
* **server:** H1.x SerialBodyPump backpressure stall on streaming responses ([25eeb7a](https://github.com/Leberkas-org/TurboHTTP/commit/25eeb7a3f9777d2750e4ef5fa3a015ec70855878))
* **server:** Pool CancellationTokenSource to reduce allocations ([8c18619](https://github.com/Leberkas-org/TurboHTTP/commit/8c18619024c47af45946dfe9c3277991b2c93b54))
* **server:** recycle the FeatureCollection on body-suppressed (204/304/HEAD) responses ([7eabbe5](https://github.com/Leberkas-org/TurboHTTP/commit/7eabbe5fd13cb1c18c8886ba30a61953b27b5028))
* **server:** remove response data-rate entry when body completes ([61a4b1e](https://github.com/Leberkas-org/TurboHTTP/commit/61a4b1e9e506ee275db4caf2961d8ba68503f6e5))
* **server:** serialize HTTP/1.x pipelined dispatch and fix WirePipeliningSpec under-read ([3429403](https://github.com/Leberkas-org/TurboHTTP/commit/34294036036463b08538a905eb565ebcb702fa20))
* **server:** stop ApplicationBridgeStage double-emitting on handler timeout ([51a1804](https://github.com/Leberkas-org/TurboHTTP/commit/51a18047683b3a3f64928c73b225017bc105757e))
* **server:** wire MaxConcurrentStreams to QUIC and MaxRequestBufferSize to the TCP input buffer ([ffc4f88](https://github.com/Leberkas-org/TurboHTTP/commit/ffc4f8814c9431b71d763f3cfd129bfc98783812))
* **test:** H10 RequestCompressionSpec must accumulate multi-chunk requests ([9456ce7](https://github.com/Leberkas-org/TurboHTTP/commit/9456ce794b35d989280863cb1b00371b89b4096c))
* **test:** reduce H2 ConnectionWindowStarvation payload to prevent CI flake ([fb21883](https://github.com/Leberkas-org/TurboHTTP/commit/fb218832b3d447bbfb984ffac9528bc6dfe321e8))


### Performance

* Add Kestrel benchmarks for HttpClient ([539e99b](https://github.com/Leberkas-org/TurboHTTP/commit/539e99b5f102ae8350626b27b582742dd1f07cfe))
* **client:** avoid CreateLinkedTokenSource when only caller token is cancelable ([0ce8fc3](https://github.com/Leberkas-org/TurboHTTP/commit/0ce8fc38e1eba2b50ebfba48acbf11071289b762))
* **client:** replace ConcurrentStack pools with ObjectPool in client path ([f070894](https://github.com/Leberkas-org/TurboHTTP/commit/f070894443ebabb936d493f03657c3fe145f0e8a))
* **h2,h3:** add per-connection HeaderNameCache for HPACK/QPACK decoding ([ddac45e](https://github.com/Leberkas-org/TurboHTTP/commit/ddac45e61d2c4f6e32827f5b955486b37842d3ee))
* **h2,h3:** sync fast-path for body drain reads, remove .AsTask() allocations ([fb19fd0](https://github.com/Leberkas-org/TurboHTTP/commit/fb19fd0583bd73b41e30c0f9dbd050632a799a11))
* **h3:** decode inbound frames zero-copy from the transport buffer ([1171012](https://github.com/Leberkas-org/TurboHTTP/commit/11710123dd96f7155ed673f1100de52861c6529d))
* **h3:** pre-compute QPACK static table name byte lengths and encoded sizes ([1544580](https://github.com/Leberkas-org/TurboHTTP/commit/15445808d5083d470fcecd2957993e53d1af96be))
* **h3:** remove 4 allocating dead-code methods from H3 production pathr ([9e8d511](https://github.com/Leberkas-org/TurboHTTP/commit/9e8d511763a0e42c072882e4dc711ef5ab5a0636))
* **h3:** reuse HeaderEncodingEntry[] array in QpackEncoder.PlanEncodings ([5903b56](https://github.com/Leberkas-org/TurboHTTP/commit/5903b5649c2cac58a3ba6f94535d60a2a4382284))
* **h3:** reuse QPACK encode buffer in Http3ServerEncoder ([09bf2db](https://github.com/Leberkas-org/TurboHTTP/commit/09bf2db428c13b3f94695394a85e7a988da6d76b))
* **h3:** reuse QpackInstructionDecoder in ProcessDecoderInstructions ([7347319](https://github.com/Leberkas-org/TurboHTTP/commit/7347319161804ba725f85fa6d836843508edcf07))
* **http11:** build request headers once and rent exact-size buffer ([9983d41](https://github.com/Leberkas-org/TurboHTTP/commit/9983d4107cab328be144a5eacab88d2226b5f088))
* pool QueuedBodyReader per-connection for H2/H3 request bodies ([23ba3c0](https://github.com/Leberkas-org/TurboHTTP/commit/23ba3c0119d7ce3263330936eb98e234171c768e))
* **protocol:** drop per-response decode allocations (reason phrase + header value) ([22ecdd1](https://github.com/Leberkas-org/TurboHTTP/commit/22ecdd17adfc7d1caf41a1a9bfdd934b1a3cad58))
* **protocol:** rent body chunks from a cross-thread ArrayPool ([e65bf75](https://github.com/Leberkas-org/TurboHTTP/commit/e65bf75a76294f12c139508e469d8f332652bff7))
* quick-win allocation reductions across client and server ([deb2530](https://github.com/Leberkas-org/TurboHTTP/commit/deb25307b6b1a6237a3e070fa650271cfbce972f))
* replace H2/H3 pseudo-header Dictionary with typed fields ([9fbf854](https://github.com/Leberkas-org/TurboHTTP/commit/9fbf85437319ed328c01761c534ee2adebc02d8b))
* replace per-byte branching with lookup tables + H3 sawDate flag ([372d623](https://github.com/Leberkas-org/TurboHTTP/commit/372d623f3d5aef6f5752fd034670d5583f88bed6))
* replace QPACK per-decode MemoryPool.Rent with per-decoder scratch buffer ([c637773](https://github.com/Leberkas-org/TurboHTTP/commit/c6377731fc0623d066f9a4dcb718dc29db4b8a87))
* reuse content-header lists across pool cycles + Servus 0.34.1 ([7367de0](https://github.com/Leberkas-org/TurboHTTP/commit/7367de0adda32140e87d40c0dce736d1709ec41e))
* round 3 quick wins — pre-encode, scratch buffers, O(1) lookups ([61be7d5](https://github.com/Leberkas-org/TurboHTTP/commit/61be7d5a6b15acf6496799754d61d689868062db))
* round 4 — lock-free pending list, span-based parsing, pre-baked headers ([50918ff](https://github.com/Leberkas-org/TurboHTTP/commit/50918ff2b3d9fa61e9552045434fcd586d593c90))
* **server:** cache send delegate and pool PassthroughFramingEncoder ([b29c23b](https://github.com/Leberkas-org/TurboHTTP/commit/b29c23bb7002b351de635ca1b84cba0ce5e0ab9c))
* **server:** cut per-request allocations on the H1.1 hot path ([05cbf9e](https://github.com/Leberkas-org/TurboHTTP/commit/05cbf9e65f8c214c736b45a7f45454a58eed6fd7))
* **server:** deduplicate data-rate timer scheduling and reuse response pipe writer ([0c0e157](https://github.com/Leberkas-org/TurboHTTP/commit/0c0e15776b8f6a0eaef40b3bfdf5c4786888d22f))
* **server:** eliminate per-response pipe lock on the buffered write path ([6104e05](https://github.com/Leberkas-org/TurboHTTP/commit/6104e0535200ec2d56677576f9bb8bd26b2b934b))
* **server:** H2 HPACK header block cache for repeated response patterns ([29bf831](https://github.com/Leberkas-org/TurboHTTP/commit/29bf83147f969fb44bec034553e12df31898ce0a))
* **server:** intern common request targets to avoid per-request string alloc ([54d1355](https://github.com/Leberkas-org/TurboHTTP/commit/54d135580f1adc83a59019ae1915d9f2c6963e94))
* **server:** pool H2 request feature + reduce QUIC receive buffer 64K→4K ([813a730](https://github.com/Leberkas-org/TurboHTTP/commit/813a7300f413cb8eaf8e9de897a8b9c8f9aa21e3))
* **server:** pool the feature collection per connection ([a9852ca](https://github.com/Leberkas-org/TurboHTTP/commit/a9852ca09264762cba3e31f8255d486e03652257))
* **server:** pool TurboHttpRequestFeature on the recycled feature-collection path ([ab291ed](https://github.com/Leberkas-org/TurboHTTP/commit/ab291eda642eb2b29ce8341211a61807f69f822e))
* **server:** pre-bake H1.x status lines as static byte tables ([22e192b](https://github.com/Leberkas-org/TurboHTTP/commit/22e192b71739666f6b050d5e26c843f95e9bfb78))
* **server:** remove TryCoalesceOutbound memcpy — writev handles scatter natively ([471c661](https://github.com/Leberkas-org/TurboHTTP/commit/471c66107e22127f71a234966bc32f1a244b9a1b))
* **server:** replace LINQ hot-path allocations in H1.1 state machine ([274a48e](https://github.com/Leberkas-org/TurboHTTP/commit/274a48e2ae6c6620c3019b7458f4ead6fd1d07a9))
* **server:** replace per-encode MemoryPool.Rent with scratch buffer in H2 encoder ([b88e5d9](https://github.com/Leberkas-org/TurboHTTP/commit/b88e5d9261b816df85cacc5cf4f5c970e46da42f))
* **server:** reuse the ASP.NET HttpContext per connection via IHostContextContainer ([7da9755](https://github.com/Leberkas-org/TurboHTTP/commit/7da9755f02eb033a57a9f6de3325a84470b78a3f))
* **server:** short-circuit body classification for no-body requests ([596e2da](https://github.com/Leberkas-org/TurboHTTP/commit/596e2da0227bd941d23fbe6b8f9819f357695cec))


### Documentation

* fix old docs ([e025e18](https://github.com/Leberkas-org/TurboHTTP/commit/e025e18155e819d8f14dee4ae995a4812e1ce850))
* fix stale integration-test commands in CLAUDE.md ([fa76bf9](https://github.com/Leberkas-org/TurboHTTP/commit/fa76bf9ad91225ef0d8746836570a7d5efa3a3e4))


### Refactoring

* **h10:** use SerialBodyPump for known Content-Length, retain BufferedBodyWriter for unknown ([e2bb318](https://github.com/Leberkas-org/TurboHTTP/commit/e2bb318fd973031f329e0111bca18e59c6ca1794))
* **h11:** replace push-based body drain with SerialBodyPump ([26cb252](https://github.com/Leberkas-org/TurboHTTP/commit/26cb2520b05f46da5848c283d2e81ce1188de2ff))
* **h1x:** eliminate writer layer, use ConnectionPoolContext + ChunkedFramingHelper ([0ba8c0b](https://github.com/Leberkas-org/TurboHTTP/commit/0ba8c0b66ffe39209f9ea443a0b448f023c6d888))
* **h2,h3:** migrate to FlowControlledBodyPump/MultiplexedBodyPump with generic targets ([82e5feb](https://github.com/Leberkas-org/TurboHTTP/commit/82e5febd3b9edad63b49229b8a5e324b41cc593d))
* **h2:** replace push-based body drain with BodyDrainScheduler ([1d3b371](https://github.com/Leberkas-org/TurboHTTP/commit/1d3b3719a0b0d096199aa436130b8a59c1993bd1))
* **h3:** replace push-based body drain with MultiplexedBodyPump ([34f92da](https://github.com/Leberkas-org/TurboHTTP/commit/34f92da05f03877c9571edaef8ad9da1bfc886ab))

## [3.0.0-alpha.3](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha.2...v3.0.0-alpha.3) (2026-06-11)


### Features

* **client:** add CONNECT support ([99448da](https://github.com/Leberkas-org/TurboHTTP/commit/99448dabaa6198c11f644c4e64dd3717bc3afd68))


### Documentation

* align option references and client API docs with current code ([4c3f186](https://github.com/Leberkas-org/TurboHTTP/commit/4c3f186ff907e970b03d5f4601da6b889e0e0768))

## [3.0.0-alpha.2](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha.1...v3.0.0-alpha.2) (2026-06-11)


### Features

* **client:** default timeout for channel path + CancelPendingRequests drain ([fd4bf5e](https://github.com/Leberkas-org/TurboHTTP/commit/fd4bf5e4b1b57029e6907bc8537c61fc0cf4a505))
* **client:** expose pipe buffer tuning via TurboClientOptions ([9773bab](https://github.com/Leberkas-org/TurboHTTP/commit/9773bab0a19d58905d3ef209ef3b9045c9c9641d))
* **client:** propagate effective CancellationToken onto request options ([9602238](https://github.com/Leberkas-org/TurboHTTP/commit/9602238d85911e8a846e4f1cf4d5092a7dfd1d2e))
* **h10:** per-request cancellation with disconnect ([6b51616](https://github.com/Leberkas-org/TurboHTTP/commit/6b516168f3403b7eaf8a506b895065cad1a81e93))
* **h11:** per-request cancellation with pipelining awareness ([3b01383](https://github.com/Leberkas-org/TurboHTTP/commit/3b01383d2ef45ec000054a66ee72e35c8dd97aed))
* **h2:** emit RST_STREAM on per-request cancellation ([323097d](https://github.com/Leberkas-org/TurboHTTP/commit/323097d26ec2c21fd40c53ad6c9beb08793d2e3a))
* **h3:** emit STOP_SENDING on per-request cancellation ([0cbe4d9](https://github.com/Leberkas-org/TurboHTTP/commit/0cbe4d9a19476c4daf461c7a0988aa66df0b45d2))
* pipe transport, body redesign, server simplification ([5281774](https://github.com/Leberkas-org/TurboHTTP/commit/528177468b6dc7d9cf0dcbe514af70641f37190b))
* **protocol:** add CancellationToken infrastructure for per-request cancel ([e74d9d2](https://github.com/Leberkas-org/TurboHTTP/commit/e74d9d2bea936d953c929ddcea761e5967302036))
* **server:** Add transport buffer options ([d2cc47f](https://github.com/Leberkas-org/TurboHTTP/commit/d2cc47f3b5a2eb2a3b7e008ed5cfc5314f0d2baa))
* **server:** expose TransportBufferOptions with protocol-optimized defaults ([3857fc9](https://github.com/Leberkas-org/TurboHTTP/commit/3857fc9cc3883e7be455979d29c7a694923451cf))
* **stage:** register per-request CancellationToken callbacks in connection stage ([bcb808e](https://github.com/Leberkas-org/TurboHTTP/commit/bcb808e6d807a88be870adcdadc390a29349849c))


### Bug Fixes

* **bench:** add 30s timeout guard to all benchmark iterations ([82b1cb7](https://github.com/Leberkas-org/TurboHTTP/commit/82b1cb71ee96daf4937d9f916bb2860283772123))
* **bench:** add 30s timeout to all benchmark clients ([48fe358](https://github.com/Leberkas-org/TurboHTTP/commit/48fe358330a8d3761c4686c07b7231fb82af95a5))
* **bench:** add CancellationToken timeout to all warmup and SendAsync calls ([b9cd7e0](https://github.com/Leberkas-org/TurboHTTP/commit/b9cd7e034aaecaa125bc96865f07bb0874995770))
* **bench:** add IterationCleanup drain for streaming benchmarks ([a7be4f4](https://github.com/Leberkas-org/TurboHTTP/commit/a7be4f4ec3e9f225f524da05d4e439badf462991))
* **bench:** align H3 client MaxConcurrentStreams with Kestrel default ([564e753](https://github.com/Leberkas-org/TurboHTTP/commit/564e753ac04e3366723406a07728849468239b95))
* **bench:** drain stale responses at start of each streaming iteration ([be01236](https://github.com/Leberkas-org/TurboHTTP/commit/be012368484f05b7be77a32e099cc43fff5dcfd7))
* **bench:** drop CL=4096 from streaming benchmarks ([564968a](https://github.com/Leberkas-org/TurboHTTP/commit/564968a16dae2ded49a253ff99c43dc448409f6d))
* **benchmarks:** protocol-aware fan-out limits and scaled timeouts ([575375e](https://github.com/Leberkas-org/TurboHTTP/commit/575375ee8b08f863ed46e5290dc5beb2ce05c5cc))
* **bench:** prevent benchmark reports from overwriting previous runs ([fa5d3bd](https://github.com/Leberkas-org/TurboHTTP/commit/fa5d3bdaf01c0a57db9811f873359a7430f6b41f))
* **bench:** prevent streaming benchmark deadlocks ([a249088](https://github.com/Leberkas-org/TurboHTTP/commit/a249088e8a601f72e6900abdccf74ea83a612c6b))
* **bench:** raise QUIC stream limit and harden streaming benchmarks ([9a54267](https://github.com/Leberkas-org/TurboHTTP/commit/9a54267077668f468a03ad24ea69c7d4bb6cefe9))
* **bench:** restore CL=4096 for streaming benchmarks ([a02761a](https://github.com/Leberkas-org/TurboHTTP/commit/a02761ad6d235ccf369b86c5b4aa70fe42b53363))
* **bench:** switch heavy benchmarks to /upload route + throttle streaming writer ([daab86a](https://github.com/Leberkas-org/TurboHTTP/commit/daab86a41a5cb62d4f810e4424546c9657dfdef8))
* **body:** H10 truncated body error propagation, H11 chunked boundary deadlock ([561ff28](https://github.com/Leberkas-org/TurboHTTP/commit/561ff286be80a54637977d7419483f70967fc83e))
* **body:** QueuedBodyReader.ReadAsync now respects CancellationToken ([fb5c55a](https://github.com/Leberkas-org/TurboHTTP/commit/fb5c55a48e2d1583f4d3328ee48b010fa100c740))
* **body:** resolve pending ReadAsync on Reset to prevent InvalidOperationException ([bc21107](https://github.com/Leberkas-org/TurboHTTP/commit/bc21107a5c2be2e00cd293dbba99faf10eb7616b))
* **ci:** Disable parallel test modules ([a1d783f](https://github.com/Leberkas-org/TurboHTTP/commit/a1d783ff137bbef145dde2d2dbbef7245e374bf4))
* **ci:** run two test modules in parallel ([0ed7e7f](https://github.com/Leberkas-org/TurboHTTP/commit/0ed7e7f2b11ea3e17a6ffe97108138c23f12451a))
* client flush backpressure, QUIC pipe options, test fixes ([bf3effd](https://github.com/Leberkas-org/TurboHTTP/commit/bf3effd10f1f7b5e6d4986ebb0cf7ec49d3c4fd2))
* **e2e:** stabilize E2E integration tests ([cbc2252](https://github.com/Leberkas-org/TurboHTTP/commit/cbc22522d97a8f4a3089acb41b9be412571be0f4))
* **e2e:** use ctx.RequestAborted instead of TestContext CancellationToken ([e6c6f56](https://github.com/Leberkas-org/TurboHTTP/commit/e6c6f560be57920460f372dae9f5aec15bf0acc5))
* **h10/server:** dispatch streaming request bodies before full receipt ([4ed9c42](https://github.com/Leberkas-org/TurboHTTP/commit/4ed9c424126d91b38d5045919452984dd590388d))
* **h2/server:** partial send in DrainOutboundBuffer when flow control window &lt; chunk ([2f57852](https://github.com/Leberkas-org/TurboHTTP/commit/2f57852b8dadfd16202ce3a724aa6635e712e671))
* **h2:** track stream-level send window in FlowController.OnDataSent ([f31784e](https://github.com/Leberkas-org/TurboHTTP/commit/f31784ed7de48eb515b2eb838bca1495629aba34))
* **http2/3:** correct body read pending state ([f8d2485](https://github.com/Leberkas-org/TurboHTTP/commit/f8d248538f839ed6246e8e7904017ba2e341199c))
* **http2:** make QueuedBodyReader thread-safe and fail truncated response bodies ([ba89a9c](https://github.com/Leberkas-org/TurboHTTP/commit/ba89a9c508bc72b6ff6ce5a2754c37a932e03492))
* **http:** fix http version comparison and null checks ([91fdab1](https://github.com/Leberkas-org/TurboHTTP/commit/91fdab1787a5793bc2c1cd7d9c2f2144c65c8ee2))
* **http:** Improve flow control and stream draining ([6ec29cb](https://github.com/Leberkas-org/TurboHTTP/commit/6ec29cb5d5bffed046d2c021990c84e819f5cfe4))
* **quic:** update submodule — drain pending acquires on release/establish ([1defdbb](https://github.com/Leberkas-org/TurboHTTP/commit/1defdbbfb2a732402513a4faef14d7da2fd34ea2))
* **quic:** update submodule — server stream accept loop exception handling ([63a1319](https://github.com/Leberkas-org/TurboHTTP/commit/63a1319b38f1a45449f5a364b70fff5362d0d866))
* **quic:** update submodule with QUIC accept loop resilience ([7cee693](https://github.com/Leberkas-org/TurboHTTP/commit/7cee6937709ab21b9068e51953a99a0dcbb040a5))
* Remove unused OpenTelemetry package ([93a9f26](https://github.com/Leberkas-org/TurboHTTP/commit/93a9f26af3f75dafbcb194d2d93e8eebdb2f51ef))
* **server:** always call TryPullResponse from OnNetworkPull ([e18b2e3](https://github.com/Leberkas-org/TurboHTTP/commit/e18b2e312793d36be962e85ebdccbe3a6237ed18))
* **server:** split buffered body into MAX_FRAME_SIZE-compliant DATA frames ([a64f68b](https://github.com/Leberkas-org/TurboHTTP/commit/a64f68bf788f59b89457bf4399fc057d7802f5f6))
* **tcp:** update submodule with concurrent PipeWriter access fix ([ef32fea](https://github.com/Leberkas-org/TurboHTTP/commit/ef32fea181f593dfd1f901ef352b5fba5e0e9e59))
* **test:** Disable parallel test collections ([0dae2a3](https://github.com/Leberkas-org/TurboHTTP/commit/0dae2a3e7837e005a2384c6fe19bca53a90362fa))
* **test:** make integration test infrastructure parallel-safe ([9dce6a6](https://github.com/Leberkas-org/TurboHTTP/commit/9dce6a6be975cb850b4e056194757785f0208d3d))
* **test:** raise client timeout in LargePayloadSpec for CI contention ([65dd73f](https://github.com/Leberkas-org/TurboHTTP/commit/65dd73f853c9a23cde702cf545a3a0b6698331c7))


### Performance

* **client:** remove .Async() boundary from EndpointDispatchStage ([f4a1bb4](https://github.com/Leberkas-org/TurboHTTP/commit/f4a1bb4d9e2e5792b4dee441784fe442fa61daa4))
* **h3:** cache QPACK encode buffer across Encode() calls ([1ff7130](https://github.com/Leberkas-org/TurboHTTP/commit/1ff7130818e824abe8230e56e1ccbc20c04afacb))
* **h3:** pool FrameDecoder and rent StreamState from pool in server ([49b3032](https://github.com/Leberkas-org/TurboHTTP/commit/49b303219df20dee3155ab3880a1b183f8b96b1e))
* **h3:** reduce QUIC pipe MinimumSegmentSize from 16KB to 4KB ([35e45fa](https://github.com/Leberkas-org/TurboHTTP/commit/35e45fa134839650083576e10c36a8a78ca892fb))
* pass sizeHint to GetMemory() + sync body read bypass for H10 server ([4b65fb3](https://github.com/Leberkas-org/TurboHTTP/commit/4b65fb3c864eaae98885b49d709ee33d7198400a))
* pool TransportData wrappers + convert PipeTo messages to readonly record structs ([f27b895](https://github.com/Leberkas-org/TurboHTTP/commit/f27b895b00839e3a8064f6fe9b9b803491540f82))
* reduce QueuedBodyReader default capacity from 64 to 8 ([1988ddb](https://github.com/Leberkas-org/TurboHTTP/commit/1988ddb89b1b7f7ec583a0d404664bd21832d501))
* right-size body drain buffers using content-length ([5e54fe3](https://github.com/Leberkas-org/TurboHTTP/commit/5e54fe3c6ff1d648790cba7e221c97e999820049))
* **server:** buffered body fast path for all protocol SMs ([8823dec](https://github.com/Leberkas-org/TurboHTTP/commit/8823dec035f3174ad2fdb264ba431c6565d3cace))
* **server:** dual-mode ResponsePipeWriter with lazy Pipe upgrade ([721c992](https://github.com/Leberkas-org/TurboHTTP/commit/721c9928ae9935cc24c321d4d11028aa0bb20e39))
* **server:** eliminate SetOnStarting closure allocation ([0ea3214](https://github.com/Leberkas-org/TurboHTTP/commit/0ea3214607db8a258a4497ac94d29c1975c3154b))
* **server:** fix QUIC stream leak, reduce allocations, improve H2 throughput ([03a6d9c](https://github.com/Leberkas-org/TurboHTTP/commit/03a6d9c9b8f5e0481c787c832ffe4c4a3f3df402))
* **server:** pool ArrayBufferWriter&lt;byte&gt; for response body buffering ([3db8272](https://github.com/Leberkas-org/TurboHTTP/commit/3db8272f2b3192579edfa53faacbe3aef979022e))
* **server:** recycle FeatureCollection after response body consumption ([6a06a89](https://github.com/Leberkas-org/TurboHTTP/commit/6a06a89e6fec7f3a527cf0cac3c3c54edd50b69b))
* **server:** synchronous body read bypass for pre-buffered responses ([ea52a12](https://github.com/Leberkas-org/TurboHTTP/commit/ea52a129638688772fe0991a5ea8c0fed1021c8c))
* **tcp:** update submodule with server transport alignment ([a16fd7f](https://github.com/Leberkas-org/TurboHTTP/commit/a16fd7f777737c1561ac1f676297e67290efc1ed))
* **tcp:** update submodule with write coalescing ([e1b512f](https://github.com/Leberkas-org/TurboHTTP/commit/e1b512fe79d069c97e70014c73dabfa35cac5adc))


### Documentation

* **notes:** document H2 response truncation race with repro steps ([0db412f](https://github.com/Leberkas-org/TurboHTTP/commit/0db412f39a1003004cda8f642eb6e196bed7d1c1))


### Refactoring

* **bench:** remove Binkraken benchmarks entirely ([fe255b2](https://github.com/Leberkas-org/TurboHTTP/commit/fe255b2c2a7f71998e66a4efa3cb6632124555ff))
* Remove unused MemoryPool reference ([e56b38d](https://github.com/Leberkas-org/TurboHTTP/commit/e56b38df69493f202352508a3bbe7ecf3bb0acc5))

## [3.0.0-alpha.1](https://github.com/Leberkas-org/TurboHTTP/compare/v3.0.0-alpha...v3.0.0-alpha.1) (2026-06-02)


### Features

* **client:** Add WithFirstPartyContext and WithTimeout ([4debf0f](https://github.com/Leberkas-org/TurboHTTP/commit/4debf0f06f34348036f7e0c00a1e30ae2ab41002))
* Consolidate timer names with constants ([2c5623c](https://github.com/Leberkas-org/TurboHTTP/commit/2c5623c2fba2f94f61f775bbac094e1e8e226073))
* **h3:** connection-error teardown on the server (stop swallow, close, RST) ([32ec3f9](https://github.com/Leberkas-org/TurboHTTP/commit/32ec3f956e084b89edf19db5443de0a5bbb8a911))
* **http2:** adaptive receive-window growth in FlowController (client-gated) ([028c49e](https://github.com/Leberkas-org/TurboHTTP/commit/028c49e9f2fa41e7e8038f0de6aab93a474cfd81))
* **http2:** adaptive window-scaling client options + projection ([8537293](https://github.com/Leberkas-org/TurboHTTP/commit/853729352f64d4fcd4326bdd2e655a0ea3f99f81))
* **http2:** add RttEstimator for PING-based min-RTT measurement ([0995bb1](https://github.com/Leberkas-org/TurboHTTP/commit/0995bb1f51d0678593919a3e3786bd123152e244))
* **http2:** add WindowScaler BDP growth formula ([6a6413d](https://github.com/Leberkas-org/TurboHTTP/commit/6a6413d0621954eec51220bd51f7878fc19d7dbe))
* **http2:** enable adaptive window scaling ([fd722ad](https://github.com/Leberkas-org/TurboHTTP/commit/fd722ad04fa915a85086f6d276f7a772e9c4dfc4))
* **http2:** Improve HTTP/2 protocol robustness and RFC compliance ([b67bc5d](https://github.com/Leberkas-org/TurboHTTP/commit/b67bc5d6fc63b708986caa04d94d715b226d89be))
* **http2:** Improve interim response and trailer handling ([a64314c](https://github.com/Leberkas-org/TurboHTTP/commit/a64314cb333722b00ce7a66ba3d1a538c46781f8))
* **http2:** project client http2 options to encoder ([2762854](https://github.com/Leberkas-org/TurboHTTP/commit/2762854d3bcb75bfb98caf37312f89fa90c895b3))
* **http2:** raise per-stream receive window to 1 MB + E2E flow control tests ([ce844b5](https://github.com/Leberkas-org/TurboHTTP/commit/ce844b5c1b674dc093e6250ee9b93dc0a5a8780d))
* **http2:** validate client stream IDs per RFC 9113 §5.1.1 ([0ceaad9](https://github.com/Leberkas-org/TurboHTTP/commit/0ceaad9e18d3cd8207e6e9bc212b9d70cacad4b9))
* **http2:** wire client adaptive window scaling + RTT probes ([5cf1549](https://github.com/Leberkas-org/TurboHTTP/commit/5cf1549e709a04f337dd036f18b32c7dd16eae85))
* **http3:** improve session manager logic ([d4eb2ac](https://github.com/Leberkas-org/TurboHTTP/commit/d4eb2ac7099d9255f9784a2e5abc33cba8846288))
* **http3:** process inbound SETTINGS and reject duplicates ([4e73f7c](https://github.com/Leberkas-org/TurboHTTP/commit/4e73f7ce5d885d4cd34b742515a7f86afa85bd96))
* **options:** Rename body size properties ([9467b52](https://github.com/Leberkas-org/TurboHTTP/commit/9467b527f405e81088a37c2307f4fe2d4c04590a))
* **options:** Rename maxEndpointSubstreams to maxConcurrentEndpoints ([24b8c5e](https://github.com/Leberkas-org/TurboHTTP/commit/24b8c5ef4d8ecbbdaf42f89602a318736e2d89e6))
* **security:** extend CVE-class protections to HTTP/3 + close HPACK ([322a53b](https://github.com/Leberkas-org/TurboHTTP/commit/322a53b5847f0477d664f38ba7658c02bb00d28e))
* **server:** add actor-based FairShareCoordinator ([dc3d6c6](https://github.com/Leberkas-org/TurboHTTP/commit/dc3d6c68b430fff016fa5bf0b92820bd5096c761))
* **server:** add ConnectionActor for per-connection lifecycle ([9ea7cb2](https://github.com/Leberkas-org/TurboHTTP/commit/9ea7cb26b8666e945a8648650dbe889283022d92))
* **server:** add generic DynamicHub keyed fan-out stage ([fb9bb12](https://github.com/Leberkas-org/TurboHTTP/commit/fb9bb121188421c2f33f9668502dc679be4dcfea))
* **server:** extract W3C trace context from inbound requests ([1c0124b](https://github.com/Leberkas-org/TurboHTTP/commit/1c0124baa3a8661acc4da5c64bd7ffc66067df90))
* **server:** introduce ServerPipeline owning shared + per-connection flow ([cb81c9c](https://github.com/Leberkas-org/TurboHTTP/commit/cb81c9ce3b073f8a599c9bcde7322d37106dfd4f))
* **server:** validate options on startup ([e532447](https://github.com/Leberkas-org/TurboHTTP/commit/e532447f3544b652efb277c28ff7db0c539e7f84))
* **streams:** migrate DynamicHub tests and impl ([21c3c4b](https://github.com/Leberkas-org/TurboHTTP/commit/21c3c4b6f156c1c5e80cffb00be8cfe9ba3e79aa))


### Bug Fixes

* **client:** propagate handler exceptions, wire per-request timeout, enforce SameSite ([3bd9ddd](https://github.com/Leberkas-org/TurboHTTP/commit/3bd9ddd1609cfd86a50273ebb126800b13717763))
* **client:** resolve typed clients via ActivatorUtilities instead of cast ([b815e42](https://github.com/Leberkas-org/TurboHTTP/commit/b815e4226ce8da8f4787db4c8eac2660fc1bc8d5))
* **http2:** reject empty :path pseudo-header for non-CONNECT requests ([56876e3](https://github.com/Leberkas-org/TurboHTTP/commit/56876e35294738ef58204ff2bd3888ab974cb363))
* **server:** close idle H2/H3 connections on keep-alive timeout ([86fae26](https://github.com/Leberkas-org/TurboHTTP/commit/86fae2685b3e48593720b06be27323f4b72db61c))
* **server:** pull next pipelined response after an outbound body completes ([a78c352](https://github.com/Leberkas-org/TurboHTTP/commit/a78c352ca5738bf39010c5481c678d45981c0e59))


### Documentation

* update config docs ([b7b751f](https://github.com/Leberkas-org/TurboHTTP/commit/b7b751fd2a9ac102fda4e43a755b9a3d5d12bcff))


### Refactoring

* **client:** move H1.1 MaxPipelineDepth out of decoder options ([7e47256](https://github.com/Leberkas-org/TurboHTTP/commit/7e47256323f340c68253ded0e692c49005f27718))
* **http2:** move RttEstimator ownership into FlowController ([db3e376](https://github.com/Leberkas-org/TurboHTTP/commit/db3e3761158789b7914d36919e650ef11fbf9f47))
* **http2:** Simplify session manager constructor ([5bf8b84](https://github.com/Leberkas-org/TurboHTTP/commit/5bf8b848a26e58fba1fec45b6a675607ca5cce76))
* rename instrumentation extensions ([28c8c07](https://github.com/Leberkas-org/TurboHTTP/commit/28c8c07f72c1470533c90ab09fce0537190d5d84))
* replace local Servus.Akka with git submodule ([7bd8566](https://github.com/Leberkas-org/TurboHTTP/commit/7bd856673114389b81e3f70bd2932f5752ca514c))
* **server:** FairShareAdmissionStage + ServerPipeline use actor-based coordinator ([67f875c](https://github.com/Leberkas-org/TurboHTTP/commit/67f875ce1b8caea96f375747f9f8472152b902ad))
* **server:** migrate H1.0/H1.1 data-rate clock to TimeProvider ([8a9b5b0](https://github.com/Leberkas-org/TurboHTTP/commit/8a9b5b0a161d8d214f869f1f8823f385824a78d2))
* **server:** move DynamicHub to shared Streams.Stages namespace ([6f97dc2](https://github.com/Leberkas-org/TurboHTTP/commit/6f97dc2d7e27619b056f19f81b311ad5098c058d))
* **server:** rewrite ListenerActor to spawn ConnectionActor per connection ([638a946](https://github.com/Leberkas-org/TurboHTTP/commit/638a946a7112b87675bb0aebc31ce1605681ab7d))
* **server:** wire ServerPipeline, remove ResponseDispatcherHub ([0667861](https://github.com/Leberkas-org/TurboHTTP/commit/0667861e6db11342b1b9bf8c6e91ac46e967dff3))
* simplify constructor parameter passing ([86363a4](https://github.com/Leberkas-org/TurboHTTP/commit/86363a48a16ad26ce42ce4891f5ef880e2210fd0))
* **transport:** inject TimeProvider into connection pool leases for deterministic eviction ([d878aa2](https://github.com/Leberkas-org/TurboHTTP/commit/d878aa23b9ed5f7f543e035f88a758b99f74f457))

## [3.0.0-alpha](https://github.com/Leberkas-org/TurboHTTP/compare/v2.0.0...v3.0.0-alpha) (2026-05-31)


### ⚠ BREAKING CHANGES

* publish accumulated v3 work as alpha prereleases

### Features

* **ci:** Add release-next to CI triggers ([e1407f6](https://github.com/Leberkas-org/TurboHTTP/commit/e1407f63d52c189565009dcbbd2a1af40e7cf487))
* publish accumulated v3 work as alpha prereleases ([e8b6e9a](https://github.com/Leberkas-org/TurboHTTP/commit/e8b6e9a205b7f23761e4681c4e5d3a05da94db1b))
* **server:** connection-per-stage pipeline with fair-share dispatch ([c49104f](https://github.com/Leberkas-org/TurboHTTP/commit/c49104fa99950f8f50c10422f6aa97956e87f452))
* **server:** data-rate monitoring and protocol server option resolution ([ad4d0b7](https://github.com/Leberkas-org/TurboHTTP/commit/ad4d0b74344830390b8561f6d9a2b1f6ea983907))
* **server:** enforce four previously-unwired server options ([a9b581c](https://github.com/Leberkas-org/TurboHTTP/commit/a9b581c0e347bbfa5ffa746210daa4c34c429a78))
* **server:** per-protocol connection options with resolved limit projections ([ea1eb2c](https://github.com/Leberkas-org/TurboHTTP/commit/ea1eb2ce30b67ddec940e3fb645f1df060a0ada4))
* **servus:** add TransportBuffer.Wrap for zero-copy buffer handoff ([d52d0bf](https://github.com/Leberkas-org/TurboHTTP/commit/d52d0bffaff7c446a459e45e9dca4dda9627bf40))


### Bug Fixes

* **tests:** adjust maxParallelThreads to 0.5x ([611e5b3](https://github.com/Leberkas-org/TurboHTTP/commit/611e5b34bcc594ae702eed19d644491bbaa6e372))


### Documentation

* **architecture:** update engine and pipeline descriptions ([e5331e7](https://github.com/Leberkas-org/TurboHTTP/commit/e5331e7dc5f5d490db740761fed58d9c6f0da110))
* **client:** correct namespaces, option defaults, and examples ([55cadd5](https://github.com/Leberkas-org/TurboHTTP/commit/55cadd5746e02af86add524d6f41e45596d14423))
* **diagrams:** fix LikeC4 client pipeline order and component metadata ([3e3e6e2](https://github.com/Leberkas-org/TurboHTTP/commit/3e3e6e21c6b58202a7717b3fa080332a903bff30))
* **server:** align option reference with code, fix stale architecture ([9043b06](https://github.com/Leberkas-org/TurboHTTP/commit/9043b06048a391ec927f5e558a1a53bbd60692ed))
* **server:** reflect ASP.NET Core IServer architecture and new options ([7b7c233](https://github.com/Leberkas-org/TurboHTTP/commit/7b7c23347abfb51c97cb64664f9e7877dc8af9f5))
* **site:** exclude internal docs from build, fix meta description, wire orphan pages ([1906807](https://github.com/Leberkas-org/TurboHTTP/commit/1906807ec376951456ba6045f16a730a15e42b96))


### Refactoring

* **client:** drop Validate from client option records ([ccf32c2](https://github.com/Leberkas-org/TurboHTTP/commit/ccf32c2df261ee6ea8a5afebe65f525712c5daf8))
* **client:** flatten client protocol options and project via extensions ([b0c4e1f](https://github.com/Leberkas-org/TurboHTTP/commit/b0c4e1ff86e689d44fad72fb46a66ecc806f9461))
* **codec:** bundle body encoder/decoder factory params into options records ([e75fce7](https://github.com/Leberkas-org/TurboHTTP/commit/e75fce7245cd210c68bb2a03b43761e83fe6ea56))
* **codec:** project BodyDecoderOptions via ToBodyDecoderOptions extension ([d0bd68e](https://github.com/Leberkas-org/TurboHTTP/commit/d0bd68e9e43587ba0eca6606ea4d072be7161c80))
* **protocol:** streamline body encoders/decoders and content classification ([a1a1a7e](https://github.com/Leberkas-org/TurboHTTP/commit/a1a1a7e44438ddff1f3cec43abc95b471926c96c))
* **server:** project BodyEncoderOptions via ToBodyEncoderOptions extension ([af232d6](https://github.com/Leberkas-org/TurboHTTP/commit/af232d60b9e4a2d8a6e9a444ff4dd9371a850ce8))
* **server:** remove unused form and header context abstractions ([22c84cc](https://github.com/Leberkas-org/TurboHTTP/commit/22c84ccc2c153537c7077a77fe92cc0aabf7e88c))
* **servus:** convert backing fields to auto-properties across transport and IO stages ([9440aca](https://github.com/Leberkas-org/TurboHTTP/commit/9440acaee4f911b111a0ec89c8e18ef5113ec62f))

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
