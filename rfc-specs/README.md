# RFC Specifications

This directory contains the official RFC documents from the IETF RFC Editor, organized by RFC number. Each RFC is stored as plain text in its own folder.

## RFC Documents Included

| RFC | Title | File |
|-----|-------|------|
| [RFC 1945](RFC1945/RFC1945.md) | Hypertext Transfer Protocol -- HTTP/1.0 | RFC1945.md |
| [RFC 6265](RFC6265/RFC6265.md) | HTTP State Management Mechanism (Cookies) | RFC6265.md |
| [RFC 7541](RFC7541/RFC7541.md) | HPACK Header Compression for HTTP/2 | RFC7541.md |
| [RFC 9000](RFC9000/RFC9000.md) | QUIC: A UDP-Based Multiplexed and Secure Transport | RFC9000.md |
| [RFC 9110](RFC9110/RFC9110.md) | HTTP Semantics | RFC9110.md |
| [RFC 9111](RFC9111/RFC9111.md) | HTTP Caching | RFC9111.md |
| [RFC 9112](RFC9112/RFC9112.md) | HTTP/1.1 Semantics and Connection Management | RFC9112.md |
| [RFC 9113](RFC9113/RFC9113.md) | HTTP/2 | RFC9113.md |
| [RFC 9114](RFC9114/RFC9114.md) | HTTP/3 | RFC9114.md |
| [RFC 9204](RFC9204/RFC9204.md) | QPACK: Header Compression for HTTP/3 | RFC9204.md |

## Usage

Reference these specifications when implementing protocol features or verifying compliance. Each RFC is stored as `RFCNNNN/RFCNNNN.md` (Markdown format).

## Source

All RFC documents are obtained from the official [IETF RFC Editor](https://www.rfc-editor.org/).

## TurboHttp Alignment

These RFCs form the specification baseline for TurboHttp's HTTP client implementation:

- **HTTP/1.0**: RFC 1945 (basic HTTP protocol)
- **HTTP/1.1**: RFC 9112 (HTTP/1.1 semantics, RFC 7230-7235 consolidated)
- **HTTP/2**: RFC 9113 (binary framing), RFC 7541 (HPACK)
- **HTTP/3**: RFC 9114 (HTTP/3), RFC 9204 (QPACK), RFC 9000 (QUIC)
- **HTTP Semantics**: RFC 9110 (methods, status codes, headers, authentication, content negotiation)
- **Caching**: RFC 9111 (freshness, validation, storage)
- **Cookies**: RFC 6265 (state management)
