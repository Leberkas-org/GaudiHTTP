---
title: "Feature 024: Benchmark Comparison Framework"
description: >-
  Comparing TurboHttp ITurboHttpClient vs standard .NET HttpClient performance
tags:
  - feature
  - benchmarks
  - performance
  - comparison
aliases:
  - Feature 024
  - HttpClient Comparison
---
# Feature 024: Benchmark Comparison Framework

**Created**: 2026-03-23
**Plan File**: `.maggus/features/feature_024.md`
**Status**: Defined, ready for implementation

## Scope

Comparing TurboHttp's `ITurboHttpClient` against standard .NET `HttpClient`:
- HTTP/1.1 and HTTP/2 protocols only (no HTTP/1.0, no HTTP/3)
- Real Kestrel server with dynamic port discovery
- Performance metrics: throughput (req/sec), latency (p50/p95/p99), memory (bytes/op)
- Load scenarios: single request and concurrent (1, 4, 16, 64, 256 clients)
- Payload variants: light (minimal body) and heavy (10KB body)

## Tasks

| Task | Description | Effort |
|------|-------------|--------|
| TASK-024-001 | Shared benchmark infrastructure | ~25k tokens |
| TASK-024-002 | Kestrel test server setup | ~20k tokens |
| TASK-024-003 | HttpClient benchmarks | ~60k tokens |
| TASK-024-004 | TurboHttp benchmarks | ~50k tokens |
| TASK-024-005 | Comparison report generator | ~35k tokens |
| TASK-024-006 | Verification gate | ~15k tokens |

**Configuration**: BenchmarkDotNet conservative settings (3 warmup, 5 target, 32 invocations per benchmark)
