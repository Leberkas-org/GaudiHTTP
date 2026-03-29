<!-- maggus-id: 20260326-163730-feature-028 -->

# Feature 028: Phase 3 — Performance Optimizations (Allocation & CPU Reduction)

## Introduction

Optimize two critical performance bottlenecks:
1. **Streaming Request Encoding** — Reduce allocations for large request bodies (HTTP/1.0, HTTP/1.1)
2. **SIMD CRLF Detection** — Faster line parsing in HTTP/1.1 decoder using SIMD instructions

These optimizations target 10-20% latency improvement and 30% memory reduction for large payloads.

### Architecture Context
- Components: `Http10Encoder`, `Http11Encoder`, `Http11DecoderPipeline`, `Http2RequestEncoder`
- New patterns: SIMD-optimized utilities (Vector<T>, SSE2/AVX2 intrinsics)
- Leverages existing `Span<T>` patterns

## Goals
1. Implement streaming request encoding (non-buffered headers)
2. Add SIMD CRLF detection (>20% faster line parsing)
3. Reduce GC pressure (fewer allocations, no buffer pooling)
4. Measure before/after with benchmarks (P99 latency improvement)

## Tasks

### TASK-028-003: Add SIMD CRLF Detection Utility
**Token Estimate:** ~45k | **Predecessors:** none | **Successors:** TASK-028-005 | **Parallel:** yes (with 001, 002)

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp/Utilities/SimdCrlfFinder.cs` with optimized CRLF detection
- [ ] Use `Vector<T>` or low-level SIMD intrinsics (System.Runtime.Intrinsics)
- [ ] Benchmark vs. string.IndexOf: target >20% improvement
- [ ] Fallback to non-SIMD path on platforms without SIMD support
- [ ] Validate correctness with comprehensive unit tests

---

### TASK-028-005: Integrate SIMD CRLF Detection into Http11DecoderPipeline
**Token Estimate:** ~30k | **Predecessors:** TASK-028-003 | **Successors:** TASK-028-006 | **Parallel:** no

**Acceptance Criteria:**
- [ ] Update `Http11DecoderPipeline` to use `SimdCrlfFinder` for line parsing
- [ ] Drop in replacement: same API, faster implementation
- [ ] All decoder tests pass
- [ ] Benchmark validates >20% improvement on typical responses

---

## Task Dependency Graph
```
TASK-028-001 ──→ TASK-028-004 ──→ TASK-028-006
TASK-028-002 ──→↗
TASK-028-003 ──→ TASK-028-005 ──→↗
```

## Functional Requirements

2. **FR-1:** SIMD CRLF detection SHALL be >20% faster than string.IndexOf

## Non-Goals
- No changes to public API
- No HTTP/2 optimizations (separate phase)
- No changes to header compression (HPACK/QPACK separate)

## Success Metrics
2. SIMD CRLF detection improves latency by ≥20%
3. P99 latency improved by ≥10% overall
4. All benchmarks pass with stable results
5. Zero regressions in existing tests
