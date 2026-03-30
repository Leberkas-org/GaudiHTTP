# TASK-023-009: Verification Gate – Integration Test Depth Feature 023

Fixed `Error-H10-003` test and aligned H10 decoder behavior with RFC 1945 §7.2.2:
Content-Length mismatch on abrupt close now throws instead of returning truncated body.

- Updated `Http10Decoder`: added `_pendingContentLength` tracking and `IsWaitingForContentLength`
  property; `TryDecodeEof` throws `HttpDecoderException` on Content-Length mismatch
  (only when `body.Length > 0`, preserving HEAD response semantics)
- Updated `Http10DecoderStage`: abrupt close (`TlsCloseKind.AbruptClose`) with
  `IsWaitingForContentLength` → `FailStage`; `onUpstreamFinish` catches decoder exceptions
- Updated `10_DecoderStateTests.cs`: ST-001/ST-004 use HTTP/0.9 (body-until-EOF) pattern;
  added ST-014 for Content-Length mismatch throw behavior
- Updated `ErrorHandlingIntegrationTests.cs`: Error-H10-003 updated to expect exception
  instead of truncated body (new correct behavior)

Verified:
- Build: 0 errors, 0 warnings
- Unit tests: 3652/3652 pass
- Stream tests: 810/810 pass
- H10 ErrorHandling: 17/17 pass
- H10 Resilience: 8/8 pass
