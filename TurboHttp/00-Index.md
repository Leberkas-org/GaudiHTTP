# TurboHttp Knowledge Base

This is the central hub for all TurboHttp project knowledge — connecting session logs, architecture decisions, RFC compliance notes, and feature planning.

## Feature Plans

Current and completed features:

- [Feature 026: HTTP/3 DataItem Wrapping](../.maggus/features/feature_026.md) — Ensure HTTP/3 engine wraps all buffers in typed message items
- [Feature 002: Error Handling Integration](../.maggus/features/feature_002.md)
- [Feature 021: Diagnosis & Traces](../.maggus/features/feature_021.md)

See [All Features](../Features/) for additional links and planning notes.

## Architecture & Design Decisions

Key architectural patterns and decisions:

- [[Architecture/message-types-and-items|Message Types & Items]] — DataItem, ConnectItem, ControlItem hierarchy
- [[Architecture/stage-conventions|Stage Inlet/Outlet Naming]] — Port naming conventions for GraphStages
- [[Architecture/pipeline-flow|Pipeline Data Flow]] — How items flow through the HTTP/1.0, 1.1, 2.0, 3.0 engines

See [Architecture Notes](./Architecture/) for full decision records.

## RFC Compliance & Coverage

RFC compliance tracking across all HTTP versions:

- [RFC 1945 (HTTP/1.0)](https://www.rfc-editor.org/rfc/rfc1945) — Basic HTTP
- [RFC 9112 (HTTP/1.1)](https://www.rfc-editor.org/rfc/rfc9112) — Messaging and connection management
- [RFC 9113 (HTTP/2)](https://www.rfc-editor.org/rfc/rfc9113) — Binary framing, multiplexing, flow control
- [RFC 7541 (HPACK)](https://www.rfc-editor.org/rfc/rfc7541) — Header compression for HTTP/2
- [RFC 9114 (HTTP/3)](https://www.rfc-editor.org/rfc/rfc9114) — HTTP semantics over QUIC
- [RFC 9110 (HTTP Semantics)](https://www.rfc-editor.org/rfc/rfc9110) — Redirects, retries, content negotiation
- [RFC 6265 (Cookies)](https://www.rfc-editor.org/rfc/rfc6265) — State management
- [RFC 9111 (Caching)](https://www.rfc-editor.org/rfc/rfc9111) — Cache control, freshness, validation

Full coverage matrix: [RFC_COVERAGE.md](../TEST_MATRIX.md)

All RFC reference documents are in the [rfc/](./rfc/) folder — quick references, requirements analysis, and compliance summaries for each RFC version.

See [RFC Notes](./RFC/) for detailed compliance tracking.

## Active Debugging

Ongoing investigations and bug reports:

See [Debugging Notes](./Debugging/) for active investigations and trace logs.

## Recent Sessions

Session work logs and session notes:

See [Session Logs](./Sessions/) for session-by-session activity logs and decisions.

## Project Resources

**External Documentation:**
- [VitePress Site](../docs/) — User guides, architecture diagrams, API reference
- [README](../README.md) — Project overview
- [CLAUDE.md](../CLAUDE.md) — Dev environment setup and conventions

**Project Directories:**
- [`.maggus/`](../.maggus/) — Feature plans, bug reports, diagnostic logs
- [`docs/`](../docs/) — VitePress documentation and LikeC4 diagrams
- [`rfc/`](../rfc/) — RFC reference documents

## Getting Started with This Vault

1. **Create a new session log:** Use `Insert Template > Session-Log` to capture daily work
2. **Document decisions:** Use `Insert Template > ADR` for architecture decisions
3. **Track RFC compliance:** Use `Insert Template > RFC-Note` for RFC sections you're implementing
4. **Investigate bugs:** Use `Insert Template > Bug-Investigation` for debugging sessions

All templates are in [Templates](./Templates/) folder.
