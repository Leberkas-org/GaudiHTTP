TASK-027-003: Fix H10 pipeline deadlock in GroupByHostKeyStage

Root cause: Dead Source.Queue actors caused 5s Ask timeouts on OfferAsync,
and stale ContinueWith callbacks corrupted replacement substream state.

Fix: Synchronous dead detection via WatchCompletionAsync().IsCompleted
bypasses the Ask timeout entirely. Captured SubflowState reference in
offer callbacks prevents stale callbacks from corrupting replacements.
MergeSubstreamsStage absorbs upstream failures to prevent zombie actors.

Changed files:
- GroupByHostKeyStage.cs: IsDead fast-path + stale callback guard
- MergeSubstreamsStage.cs: onUpstreamFailure handler (absorb + set _upstreamDone)
- H10/ErrorHandlingIntegrationTests.cs: Error-H10-005 expects OK not exception
- H11/ErrorHandlingIntegrationTests.cs: Error-005 expects OK not exception
- feature_002.md: TASK-027-003 acceptance criteria checked

Test results: 0/17 H10 failures across 3 consecutive runs (baseline: 1-3).
Full suite: 3509 unit + 790 stream + 244 integration tests pass.
