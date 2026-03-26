# RFC Specifications Index

Complete reference for official RFC documents with cross-links to TurboHttp implementation notes.

## HTTP Protocol Stack

### HTTP/1.0 — RFC 1945
- **Document**: [RFC1945.md](RFC1945/RFC1945.md)
- **Vault Notes**:
  - [RFC 1945 Analysis Index](../notes/RFC/RFC1945_ANALYSIS_INDEX.md)
  - [RFC 1945 Client Requirements](../notes/RFC/RFC1945_CLIENT_REQUIREMENTS.md)
  - [RFC 1945 Quick Reference](../notes/RFC/RFC1945_QUICK_REFERENCE.md)
- **Implementation**: `TurboHttp/Protocol/RFC1945/`
- **Compliance Score**: 92/100 ✅

### HTTP/1.1 — RFC 9112
- **Document**: [RFC9112.md](RFC9112%20Definition.md)
- **Vault Notes**:
  - [RFC 9112 Client Requirements](../notes/RFC/RFC9112_CLIENT_REQUIREMENTS.md)
- **Implementation**: `TurboHttp/Protocol/RFC9112/`, `TurboHttp/Streams/`
- **Compliance Score**: 91.5/100 ✅

### HTTP/2 — RFC 9113
- **Document**: [RFC9113.md](RFC9113%20Definition.md)
- **Vault Notes**: See RFC/00-RFC_STATUS_MATRIX.md
- **Implementation**: `TurboHttp/Protocol/RFC9113/`, `TurboHttp/Streams/`
- **Compliance Score**: 87/100 ✅

### HTTP/3 — RFC 9114
- **Document**: [RFC9114.md](RFC9114%20Definition.md)
- **Vault Notes**:
  - [RFC 9114 Analysis Summary](../notes/RFC/RFC9114_ANALYSIS_SUMMARY.txt)
  - [RFC 9114 Client MUST Requirements](../notes/RFC/RFC9114_CLIENT_MUST_REQUIREMENTS.md)
  - [RFC 9114 Extraction Report](../notes/RFC/RFC9114_EXTRACTION_REPORT.md)
- **Implementation**: `TurboHttp/Protocol/RFC9114/` (partial)
- **Compliance Score**: 60/100 🔶 (in progress)

## Transport Layer

### QUIC — RFC 9000
- **Document**: [RFC9000.md](RFC9000%20Definition.md)
- **Vault Notes**: See RFC/00-RFC_STATUS_MATRIX.md
- **Implementation**: `TurboHttp/Transport/`, Akka host integration
- **Compliance Score**: 50/100 🔶 (partial)

## Header Compression

### HPACK (HTTP/2) — RFC 7541
- **Document**: [RFC7541.md](RFC7541%20Definition.md)
- **Vault Notes**: See RFC/00-RFC_STATUS_MATRIX.md
- **Implementation**: `TurboHttp/Protocol/RFC7541/`
- **Compliance Score**: 90/100 ✅

### QPACK (HTTP/3) — RFC 9204
- **Document**: [RFC9204.md](RFC9204%20Definition.md)
- **Vault Notes**: See RFC/00-RFC_STATUS_MATRIX.md
- **Implementation**: `TurboHttp/Protocol/RFC9204/` (partial)
- **Compliance Score**: 40/100 🟡 (draft)

## HTTP Semantics & Features

### HTTP Semantics — RFC 9110
- **Document**: [RFC9110.md](RFC9110%20Definition.md)
- **Vault Notes**:
  - [RFC 9110 Client Requirements](../notes/RFC/RFC9110_CLIENT_REQUIREMENTS.md)
- **Implementation**:
  - Redirects: `TurboHttp/Protocol/RFC9110/RedirectHandler.cs`
  - Retries: `TurboHttp/Protocol/RFC9110/RetryEvaluator.cs`
- **Compliance Score**: 82/100 ✅

### HTTP Caching — RFC 9111
- **Document**: [RFC9111.md](RFC9111%20Definition.md)
- **Vault Notes**:
  - [RFC 9111 Client Cache Requirements](../notes/RFC/RFC9111_CLIENT_CACHE_REQUIREMENTS.md)
  - [RFC 9111 Compliance Index](../notes/RFC/RFC9111_COMPLIANCE_INDEX.md)
  - [RFC 9111 Quick Reference](../notes/RFC/RFC9111_COMPLIANCE_QUICK_REFERENCE.md)
- **Implementation**: `TurboHttp/Protocol/RFC9111/`
- **Compliance Score**: 90.6/100 ✅

### HTTP State Management (Cookies) — RFC 6265
- **Document**: [RFC6265.md](RFC6265%20Definition.md)
- **Vault Notes**: See RFC/00-RFC_STATUS_MATRIX.md
- **Implementation**: `TurboHttp/Protocol/RFC6265/`
- **Compliance Score**: 80/100 ✅

## Master Status

See [../notes/RFC/00-RFC_STATUS_MATRIX.md](../notes/RFC/00-RFC_STATUS_MATRIX.md) for comprehensive compliance matrix and roadmap.

**Overall Client-Side Implementation**: 86/100 — Production-ready for HTTP/1.0–2.0, HTTP/3 in progress

## Document Format

All documents are Markdown (`.md`) conversions of official IETF RFC Editor specifications. Original RFC text is wrapped in code blocks for readability. No modifications have been made to the RFC content.
