# TurboHTTP Knowledge Base

Central hub for all TurboHTTP RFC reference knowledge.

See [[VAULT_STYLE_GUIDE|Vault Style Guide]] for vault conventions and frontmatter standards.

---

## RFC Reference Documents

### HTTP Semantics & Messaging

| RFC | Title | Description |
|-----|-------|-------------|
| [[RFC/RFC9110/RFC9110\|RFC 9110]] | HTTP Semantics | Methods, status codes, content negotiation, conditional requests, authentication |
| [[RFC/RFC9112/RFC9112\|RFC 9112]] | HTTP/1.1 | Message framing, chunked transfer coding, persistent connections |
| [[RFC/RFC9111/RFC9111\|RFC 9111]] | HTTP Caching | Freshness, validation, Cache-Control directives, Vary-based secondary keys |
| [[RFC/RFC1945/RFC1945\|RFC 1945]] | HTTP/1.0 | Original HTTP spec — request/response format, GET/HEAD/POST, status codes |
| [[RFC/RFC6265/RFC6265\|RFC 6265]] | HTTP Cookies | Set-Cookie/Cookie headers, domain/path matching, Secure/HttpOnly/SameSite attributes |

### HTTP/2

| RFC | Title | Description |
|-----|-------|-------------|
| [[RFC/RFC9113/RFC9113\|RFC 9113]] | HTTP/2 | Binary framing, stream multiplexing, flow control, SETTINGS, server push |
| [[RFC/RFC7541/RFC7541\|RFC 7541]] | HPACK | Header compression for HTTP/2 — static table, dynamic table, Huffman encoding |
| [[RFC/RFC7838/RFC7838\|RFC 7838]] | Alt-Svc | HTTP Alternative Services — ALTSVC frame, Alt-Svc header, caching rules |

### HTTP/3 & QUIC

| RFC | Title | Description |
|-----|-------|-------------|
| [[RFC/RFC9114/RFC9114\|RFC 9114]] | HTTP/3 | QUIC-based HTTP — variable-length frames, QPACK integration, stream types |
| [[RFC/RFC9204/RFC9204\|RFC 9204]] | QPACK | Header compression for HTTP/3 — encoder/decoder streams, blocking references |
| [[RFC/RFC9000/RFC9000\|RFC 9000]] | QUIC | UDP-based multiplexed transport with built-in TLS 1.3 |

---

## RFC Dependency Map

```
RFC 9110 (Semantics)
├── RFC 9112 (HTTP/1.1) ──────── depends on RFC 9110
├── RFC 9111 (Caching) ───────── depends on RFC 9110
├── RFC 9113 (HTTP/2) ────────── depends on RFC 9110 + RFC 7541
│   └── RFC 7838 (Alt-Svc) ───── used by HTTP/2 ALTSVC frame
└── RFC 9114 (HTTP/3) ────────── depends on RFC 9110 + RFC 9204 + RFC 9000
    └── RFC 7838 (Alt-Svc) ───── used by HTTP/3 Alt-Svc header

RFC 1945 (HTTP/1.0) ──────────── superseded by RFC 9112
RFC 6265 (Cookies) ───────────── extends HTTP semantics
```

---

## Known Bugs

| Note | Status | Description |
|------|--------|-------------|
| [[Bugs/H2-response-truncation-race\|H2 Response Truncation Race]] | open | Concurrent multiplexed H2 streams intermittently lose whole response DATA frames (truncation by multiples of 16384) or corrupt payloads; surfaced as HTTP 200 |
