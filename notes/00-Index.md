# TurboHttp Knowledge Base

This is the central hub for all TurboHttp project knowledge — connecting session logs, architecture decisions, RFC compliance notes, and feature planning.

## Architecture & Design Decisions

- [[Architecture/01-LAYERED_ARCHITECTURE|Layered Architecture]] — Client → Handlers → Streams → Protocol → Transport
- [[Architecture/02-STAGE_PATTERNS|GraphStage Patterns]] — Port naming, conventions, stage lifecycle
- [[Architecture/03-KNOWN_GAPS_AND_LIMITATIONS|Known Gaps & Limitations]] — Critical issues, workarounds, priority roadmap
- [[Architecture/04-CURRENT_STATE_SUMMARY|Current State Summary]] — Implementation completeness, status, next milestones
- [[Architecture/05-BENCHMARK_PATTERNS|Benchmark Patterns]] — BDN conventions, port assignments, TCP TIME_WAIT workarounds
- [[Architecture/06-DECODER_PIPELINE_ARCHITECTURE|Decoder Pipeline Architecture]] — Three-layer Pipeline/EventAggregator/CompletionDecoder pattern
- [[Architecture/07-HTTP10_RECONNECTION_LIMITATION|HTTP/1.0 Reconnection Limitation]] — ExtractOptionsStage single-emit bug
- [[Architecture/08-HTTP2_DECODER_MIGRATION|Http2Decoder Migration]] — Phases 39-62, ProtocolSession migration mapping
- [[Architecture/09-CLAUDE_PREFERENCES|Claude Preferences]] — Language, knowledge capture, response style
- [[Architecture/10-TEST_ORGANIZATION|Test Organization]] — Test projects, base classes, fixtures, conventions, completed phases

See [Architecture Notes](./Architecture/) for full decision records.

## RFC Compliance & Coverage

**Overall Compliance**: 86/100 — Production-Ready for HTTP/1.0, 1.1, 2.0

- [[RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] — Detailed compliance scores, gaps, and priorities (⭐ START HERE)
- All RFC reference documents are in the [rfc/](./rfc/) folder

## Features

- [[Features/Feature024_Benchmark_Comparison|Feature 024: Benchmark Comparison]] — TurboHttp vs HttpClient performance comparison

## Active Debugging

See [Debugging Notes](./Debugging/) for active investigations.

## Templates

- [Session-Log](./Templates/Session-Log.md) — Daily work capture
- [ADR](./Templates/ADR.md) — Architecture Decision Records
- [RFC-Note](./Templates/RFC-Note.md) — RFC compliance tracking
- [Bug-Investigation](./Templates/Bug-Investigation.md) — Structured debugging

## Getting Started

- [[VAULT_STYLE_GUIDE|Vault Style Guide]] — Structure, frontmatter, formatting conventions
- [[OBSIDIAN_CSS_SETUP|CSS Setup Instructions]] — Visual consistency
