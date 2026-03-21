TASK-007: Refactor Engine to BidiFlow Atop chain with conditional inclusion

Replace the three-island pipeline (PreProcessingGraphBuilder + ProtocolCoreGraphBuilder +
PostProcessingGraphBuilder) with a composable BidiFlow.Atop() chain. Each feature
(Redirect, Cookie, Retry, Cache, Decompression) is conditionally included only when
its policy is non-null, eliminating all external MergePreferred feedback loops,
Buffer(4) cycle-breakers, and the Merge(cacheHit) node.

Key changes:
- Engine.BuildExtendedPipeline: conditional BidiFlow.Atop stacking
  (outermost→innermost: Redirect → Cookie → Retry → Cache → Decompression)
- Remove PreProcessingGraphBuilder, PostProcessingGraphBuilder, PreProcessShape, PostProcessShape
- Remove DecompressionStage from ProtocolCoreGraphBuilder (handled by DecompressionBidiStage)
- Fix RedirectBidiStage/RetryBidiStage: track in-flight request count to prevent
  premature Out1 completion when upstream finishes before responses arrive
- Middleware stages remain as Flows prepended/appended outside the BidiFlow chain
- 11 new tests in 16_EngineBidiFlowCompositionTests.cs
- All 2,560 tests green (1,827 unit + 733 stream)
