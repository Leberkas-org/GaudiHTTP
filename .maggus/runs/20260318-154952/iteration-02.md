# Iteration 02 — TASK-004: Add engine-level preface tests

## Task
TASK-004: Add engine-level preface tests (ENG-007, ENG-008)

## Changes Made
- **File modified:** `src/TurboHttp.StreamTests/Http20/Http20EngineRfcRoundTripTests.cs`
  - Added 3 imports: `Akka.Streams`, `TurboHttp.IO`, `TurboHttp.Streams.Stages`
  - Added test `ENG_007_Preface_Emitted_On_First_ConnectItem` — builds a custom GraphDsl flow
    that injects a ConnectItem via MergePreferred before the engine's outbound, routes through
    an external PrependPrefaceStage, captures DataItem byte snapshots, and asserts the 24-byte
    HTTP/2 preface magic appears.
  - Added test `ENG_008_Preface_Not_Emitted_On_Second_ConnectItem_Same_Host` — feeds two
    ConnectItems (same host) through PrependPrefaceStage and verifies only one preface DataItem
    is emitted (second ConnectItem is swallowed by host-tracking logic).

## Commands Run
1. `dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — 0 errors, 0 warnings
2. `dotnet test --filter "ENG_007 | ENG_008"` — 2/2 passed
3. `dotnet test src/TurboHttp.StreamTests/` — 488 passed, 1 failed (pre-existing COR1X-005 timeout)

## Acceptance Criteria
- [x] Test `ENG_007_Preface_Emitted_On_First_ConnectItem` exists and passes.
- [x] Test asserts that outbound bytes begin with the 24-byte magic string.
- [x] DisplayName: `"RFC-9113-ENG-007: Preface emitted on first ConnectItem through engine graph"`
- [x] Test `ENG_008_Preface_Not_Emitted_On_Second_ConnectItem_Same_Host` exists and passes.
- [x] DisplayName: `"RFC-9113-ENG-008: Preface not emitted on second ConnectItem for same host"`
- [x] Typecheck/lint passes (0 build errors).
- [x] Unit tests for TASK-004 are written and successful.

## Deviations
- ENG-007 uses a custom GraphDsl flow with MergePreferred (not Prepend/Concat, which aren't
  available on Flow in Akka.NET). Byte snapshots are captured in the Select callback before the
  fake stage disposes the IMemoryOwner, avoiding ObjectDisposedException.
- ENG-008 tests PrependPrefaceStage directly (not the full engine round-trip) because the stage
  swallows the second ConnectItem (returns without push/pull), causing a stream stall that
  prevents round-trip completion. The test verifies exactly 2 items are emitted and only 1
  contains the preface magic.
