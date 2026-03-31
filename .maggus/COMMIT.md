# TASK-038-001: Fix RedirectBidiStage — Transaction Guard + _inFlightCount Decrement

Added `_redirectTransactionActive` guard to `RedirectBidiStage` and corrected `_inFlightCount`
decrement ordering across redirect hops so that `TryCompleteIfDone` Case 2 fires reliably.

- Added `_redirectTransactionActive` bool field to `Logic` (mirrors `RetryBidiStage` pattern)
- Redirect path in `onPush(InResponse)` now uses atomic transaction:
  enqueue → TryEmitRedirect → `_inFlightCount--` → TryPullResponse → guard off → TryCompleteIfDone
- `TryCompleteIfDone()` returns early when `_redirectTransactionActive == true`, preventing
  premature Out1 completion mid-redirect transaction
- Catch blocks for `ProtocolDowngrade` and `MaxRedirects` do NOT set the guard (non-redirect paths)
- Added `BidiFlowFeedbackRaceTests.cs` with 4 regression tests (BidiLoop-001 through BidiLoop-004)

Verified:
- Build: 0 errors, 0 warnings
- Stream tests: 835/835 pass (BidiLoop-001..004 all green)
- Roslyn get_diagnostics on RedirectBidiStage.cs: 0 errors
