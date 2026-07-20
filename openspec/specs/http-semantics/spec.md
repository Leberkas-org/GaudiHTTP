# HTTP Semantics Layer

The semantics layer (`Protocol/Semantics/`) implements RFC 9110 and RFC 9111 concerns that sit
above wire-level encoding and below the Akka Streams stage pipeline. These are pure-logic
components: method classification, redirect handling, retry evaluation, content negotiation,
conditional requests, body framing classification, header validation, and HTTP caching. They
are consumed by the BidiStage feature stages and by the protocol state machines.

## Requirements

### Requirement: Method classification

`MethodProperties` classifies HTTP methods per RFC 9110 section 9.2. Three orthogonal predicates
gate downstream behavior: retry evaluation uses `IsIdempotent`, cache storage uses `IsCacheable`,
and safe-method checks use `IsSafe`.

#### Scenario: Safe methods
- **WHEN** the method is GET, HEAD, OPTIONS, or TRACE
- **THEN** `IsSafe` returns true
- **AND** `IsIdempotent` also returns true (safe implies idempotent)

#### Scenario: Idempotent non-safe methods
- **WHEN** the method is PUT or DELETE
- **THEN** `IsIdempotent` returns true
- **AND** `IsSafe` returns false

#### Scenario: Non-idempotent methods
- **WHEN** the method is POST, PATCH, or any unknown method
- **THEN** `IsIdempotent` returns false
- **AND** `IsSafe` returns false

#### Scenario: Cacheable methods
- **WHEN** the method is GET, HEAD, or POST
- **THEN** `IsCacheable` returns true
- **AND** all other methods return false

---

### Requirement: Redirect policy contract

`RedirectHandler` follows redirects per RFC 9110 section 15.4. It enforces a configurable
maximum, detects loops via visited-URI tracking, rewrites methods per status code, strips
security-sensitive headers on cross-origin redirects, and blocks HTTPS-to-HTTP downgrades
by default.

#### Scenario: Recognized redirect status codes
- **WHEN** the response status code is 301, 302, 303, 307, or 308
- **THEN** `IsRedirect` returns true
- **AND** no other status codes are treated as redirects

#### Scenario: Maximum redirect limit
- **WHEN** the redirect count reaches the `MaxRedirects` policy value (default 10)
- **THEN** `BuildRedirectRequest` throws `RedirectException` with error `MaxRedirectsExceeded`

#### Scenario: Redirect loop detection
- **WHEN** a redirect Location resolves to a URI already visited in the current chain
- **THEN** `BuildRedirectRequest` throws `RedirectException` with error `RedirectLoop`
- **AND** URI comparison normalizes scheme/host to lowercase, preserves path/query case, discards fragment

#### Scenario: Missing or invalid Location header
- **WHEN** the redirect response has no Location header or an empty/invalid value
- **THEN** `BuildRedirectRequest` throws `RedirectException` with error `MissingLocationHeader` or `InvalidLocationHeader`

#### Scenario: HTTPS-to-HTTP downgrade protection
- **WHEN** the original request used HTTPS and the Location points to HTTP
- **AND** `AllowHttpsToHttpDowngrade` is false (default)
- **THEN** `BuildRedirectRequest` throws `RedirectException` with error `ProtocolDowngrade`

#### Scenario: 303 See Other always rewrites to GET
- **WHEN** a 303 response is received regardless of the original method
- **THEN** the redirect request uses GET
- **AND** the original request body is not preserved

#### Scenario: 307 and 308 preserve method and body
- **WHEN** a 307 Temporary Redirect or 308 Permanent Redirect is received
- **THEN** the redirect request preserves the original method
- **AND** the original request body is buffered and re-sent

#### Scenario: 301 and 302 rewrite POST to GET (historical practice)
- **WHEN** a 301 or 302 response is received and the original method was POST
- **THEN** the redirect request uses GET and the body is dropped
- **WHEN** the original method was not POST (e.g. GET, PUT)
- **THEN** the original method is preserved

#### Scenario: Cross-origin credential stripping
- **WHEN** the redirect crosses origin (different scheme, host, or port)
- **THEN** the Authorization and Proxy-Authorization headers are removed from the redirected request
- **AND** the Host header is not copied (it is derived from the new URI)
- **AND** the Cookie header is not blindly forwarded (must be re-evaluated via CookieJar)

#### Scenario: Redirect with CookieJar re-evaluation
- **WHEN** `BuildRedirectRequest` is called with a `CookieJar`
- **THEN** Set-Cookie headers from the redirect response are processed into the jar first
- **AND** cookies applicable to the new redirect URI are re-applied based on domain, path, Secure, and expiry

#### Scenario: Relative Location URI resolution
- **WHEN** the Location header contains a relative URI
- **THEN** it is resolved against the original request URI per RFC 9110 section 10.2.2

---

### Requirement: Retry policy contract

`RetryEvaluator` determines whether a failed request may be automatically retried per
RFC 9110 section 9.2.2. Only idempotent methods are retried; non-replayable bodies block retry;
only network failures and specific status codes (408, 503) trigger retry.

#### Scenario: Non-idempotent methods are never retried
- **WHEN** the request method is POST, PATCH, or any non-idempotent method
- **THEN** `Evaluate` returns `ShouldRetry = false`
- **AND** the reason cites RFC 9110 section 9.2.2

#### Scenario: Partially consumed body blocks retry
- **WHEN** the request body was partially sent or consumed (cannot be rewound)
- **THEN** `Evaluate` returns `ShouldRetry = false` regardless of method or status code

#### Scenario: Network failure on idempotent method
- **WHEN** a network-level failure occurs (no response received) on an idempotent method
- **THEN** `Evaluate` returns `ShouldRetry = true`

#### Scenario: 408 Request Timeout triggers retry
- **WHEN** the response is 408 and the method is idempotent
- **THEN** `Evaluate` returns `ShouldRetry = true`
- **AND** if `RespectRetryAfter` is true, the Retry-After header value is parsed and included in the decision

#### Scenario: 503 Service Unavailable triggers retry
- **WHEN** the response is 503 and the method is idempotent
- **THEN** `Evaluate` returns `ShouldRetry = true`
- **AND** if `RespectRetryAfter` is true, the Retry-After header value is parsed and included in the decision

#### Scenario: Other error status codes are not retried
- **WHEN** the response is any status code other than 408 or 503 (e.g. 500, 502, 400)
- **THEN** `Evaluate` returns `ShouldRetry = false`

#### Scenario: Retry limit enforcement
- **WHEN** `attemptCount >= MaxRetries` (default 3)
- **THEN** `Evaluate` returns `ShouldRetry = false` regardless of the failure type

#### Scenario: Retry-After header parsing
- **WHEN** the response contains a Retry-After header with a delay-seconds integer
- **THEN** `RetryAfterDelay` is set to that many seconds
- **WHEN** the Retry-After header contains an HTTP-date
- **THEN** `RetryAfterDelay` is set to the difference from now (clamped to zero if in the past)

---

### Requirement: Expect 100-Continue flow

`Expect100Policy` and `ExpectContinueBidiStage` implement RFC 9110 section 10.1.1. The stage
adds the `Expect: 100-continue` header when the request body meets a size threshold, then
handles the interim 100 response before forwarding the final response.

#### Scenario: Body at or above threshold triggers Expect header
- **WHEN** the request has a body with Content-Length >= `MinBodySizeBytes` (default 1024)
- **THEN** the `Expect: 100-continue` header is added to the outgoing request

#### Scenario: Small body passes through unchanged
- **WHEN** the request body is smaller than the threshold or has no body
- **THEN** no `Expect` header is added and the request passes through as-is

#### Scenario: 100 Continue response is consumed silently
- **WHEN** the server responds with 100 Continue
- **THEN** the stage consumes the interim response and pulls the next (final) response

#### Scenario: 417 Expectation Failed is forwarded
- **WHEN** the server responds with 417 Expectation Failed
- **THEN** the response is forwarded to the caller

#### Scenario: No policy disables the stage
- **WHEN** no `Expect100Policy` is configured
- **THEN** the stage is a pass-through in both directions

---

### Requirement: Content encoding negotiation and application

`ContentEncodingSupport`, `ContentEncoding`, `CompressionPolicy`, and `ContentEncodingBidiStage`
implement RFC 9110 section 8.4. The system supports gzip, deflate, br (Brotli), identity, and
legacy x-gzip. Request bodies can be compressed outbound; response bodies are decompressed inbound.

#### Scenario: Supported content codings
- **WHEN** the encoding is gzip, x-gzip, deflate, br, or identity
- **THEN** `IsSupported` returns true
- **AND** any other encoding returns false

#### Scenario: Stacked encodings validation
- **WHEN** a Content-Encoding header contains a comma-separated list (e.g. "gzip, br")
- **THEN** `IsSupported` returns true only if all tokens are individually supported
- **AND** decompression is applied in reverse order (outermost encoding decoded first)

#### Scenario: Request body compression
- **WHEN** a `CompressionPolicy` is configured and the body is >= `MinBodySizeBytes` (default 1024)
- **THEN** the request body is compressed with the specified encoding (default gzip)
- **AND** the Content-Encoding header is set on the request

#### Scenario: Automatic response decompression
- **WHEN** automatic decompression is enabled (default true)
- **AND** the response has a Content-Encoding header with a supported encoding
- **THEN** the response body is decompressed
- **AND** the Content-Encoding header is removed from the response

#### Scenario: Unknown encoding throws
- **WHEN** `CreateDecompressor` or `CreateCompressor` is called with an unrecognized encoding
- **THEN** an `HttpProtocolException` is thrown citing RFC 9110 section 8.4

---

### Requirement: Body framing classification

`BodySemantics` classifies HTTP message bodies per RFC 9112 sections 6.1-6.3. It determines
whether a message has a body and which framing mechanism applies: Content-Length, chunked
Transfer-Encoding, or read-until-close.

#### Scenario: HEAD response has no body
- **WHEN** the original request method was HEAD
- **THEN** the response body framing is `None` regardless of headers

#### Scenario: 1xx, 204, 304 responses have no body
- **WHEN** the response status code is 1xx, 204, or 304
- **THEN** the body framing is `None`

#### Scenario: Content-Length determines body length
- **WHEN** the message has a Content-Length header and no Transfer-Encoding
- **THEN** the body framing is `Length` with the parsed content length

#### Scenario: Chunked Transfer-Encoding
- **WHEN** the message has Transfer-Encoding with "chunked" as the final coding
- **AND** the HTTP version is 1.1 or higher
- **THEN** the body framing is `Chunked`

#### Scenario: Both Transfer-Encoding and Content-Length is rejected
- **WHEN** both Transfer-Encoding and Content-Length are present
- **THEN** an `HttpProtocolException` is thrown (request smuggling defense)

#### Scenario: Transfer-Encoding not allowed in HTTP/1.0
- **WHEN** Transfer-Encoding is present on an HTTP/1.0 message
- **THEN** an `HttpProtocolException` is thrown

#### Scenario: Chunked must be final and appear only once
- **WHEN** "chunked" appears in Transfer-Encoding but not as the final coding
- **OR** "chunked" appears more than once
- **THEN** the message is not classified as chunked
- **AND** for requests, an `HttpProtocolException` is thrown

#### Scenario: Duplicate Content-Length values
- **WHEN** the Content-Length header contains multiple comma-separated values
- **AND** all values are identical
- **THEN** the duplicate is normalized to a single value
- **WHEN** the values differ
- **THEN** parsing fails (invalid Content-Length)

#### Scenario: Response with no framing indicators reads until close
- **WHEN** a response has neither Transfer-Encoding nor Content-Length
- **THEN** the body framing is `Close` (read until connection closes)

---

### Requirement: Content-Length semantics

`ContentLengthSemantics` validates Content-Length values per RFC 9112 sections 6.2-6.3.
It uses strict parsing (no leading signs, no whitespace) to prevent request smuggling
differentials.

#### Scenario: Strict numeric parsing
- **WHEN** the Content-Length value is a non-negative integer without leading sign or whitespace
- **THEN** `TryParse` returns true with the parsed value
- **WHEN** the value contains "+", "-", spaces, or non-digit characters
- **THEN** `TryParse` returns false

#### Scenario: Body-not-required status codes
- **WHEN** the status code is 1xx, 204, 304, or a 2xx response to CONNECT
- **OR** the method is HEAD
- **THEN** `BodyRequired` returns false

---

### Requirement: Header validation

`HeaderValidation` and `FieldValidator` enforce RFC 9110 section 5 field name and value
constraints. Field names must be valid tokens (no uppercase for H2/H3); field values must
not contain NUL, CR, or LF; connection-specific headers are forbidden in H2/H3.

#### Scenario: Token character validation for field names
- **WHEN** a field name contains only tchar characters (ALPHA, DIGIT, and `!#$%&'*+-.^_`|~`)
- **THEN** `IsToken` returns true
- **WHEN** the field name is empty or contains non-tchar characters
- **THEN** `IsToken` returns false

#### Scenario: Uppercase field names rejected in H2/H3
- **WHEN** a field name contains uppercase ASCII letters (A-Z)
- **THEN** `ValidateFieldName` throws `HttpProtocolException`

#### Scenario: Field value character validation
- **WHEN** a field value contains NUL (0x00), CR (0x0D), or LF (0x0A)
- **THEN** `ValidateFieldValue` throws `HttpProtocolException`
- **WHEN** the value contains only SP, HTAB, VCHAR (0x21-0x7E), or obs-text (0x80-0xFF)
- **THEN** `IsValidFieldValue` returns true

#### Scenario: Connection-specific headers forbidden in H2/H3
- **WHEN** a header is Connection, Keep-Alive, Transfer-Encoding, Upgrade, Proxy-Authenticate, or Proxy-Authorization
- **THEN** `ValidateConnectionSpecific` throws `HttpProtocolException`
- **WHEN** the header is TE with a value other than "trailers"
- **THEN** `ValidateConnectionSpecific` throws `HttpProtocolException`

#### Scenario: OWS trimming
- **WHEN** a header value has leading or trailing optional whitespace (SP or HTAB)
- **THEN** `TrimOws` returns the value with whitespace removed
- **AND** the original string is returned (no allocation) when no trimming is needed

---

### Requirement: Pseudo-header validation (H2/H3)

`PseudoHeaderValidator` enforces RFC 9113 section 8.3 pseudo-header constraints for HTTP/2
and HTTP/3 messages.

#### Scenario: Request pseudo-headers must be complete and ordered
- **WHEN** a request header block is validated
- **THEN** all four pseudo-headers (:method, :path, :scheme, :authority) must be present
- **AND** all pseudo-headers must appear before any regular headers
- **AND** no pseudo-header may appear more than once

#### Scenario: CONNECT request pseudo-header rules
- **WHEN** :method is CONNECT
- **THEN** :scheme and :path must NOT be present
- **AND** :authority MUST be present

#### Scenario: Response pseudo-headers
- **WHEN** a response header block is validated
- **THEN** exactly one :status pseudo-header must be present
- **AND** it must appear before any regular headers

#### Scenario: Unknown pseudo-headers rejected
- **WHEN** a pseudo-header name not in the known set is encountered
- **THEN** an `HttpProtocolException` is thrown

---

### Requirement: Connection persistence semantics

`ConnectionSemantics` determines whether a connection is persistent per RFC 9110 section 7.6.1
and classifies hop-by-hop headers.

#### Scenario: HTTP/1.0 defaults to non-persistent
- **WHEN** the HTTP version is 1.0
- **THEN** `IsPersistent` returns true only if `Connection: keep-alive` is present

#### Scenario: HTTP/1.1 defaults to persistent
- **WHEN** the HTTP version is 1.1
- **THEN** `IsPersistent` returns true unless `Connection: close` is present

#### Scenario: HTTP/2+ is always persistent
- **WHEN** the HTTP version is 2.0 or higher
- **THEN** `IsPersistent` returns true regardless of Connection header

#### Scenario: Hop-by-hop header classification
- **WHEN** a header name is Connection, Keep-Alive, Transfer-Encoding, TE, Upgrade, Proxy-Authenticate, Proxy-Authorization, or Trailer
- **THEN** `IsHopByHop` returns true

---

### Requirement: Conditional request evaluation

`ConditionalEvaluator` implements RFC 9110 section 13.2 conditional request semantics with
strict evaluation order: If-Match, If-None-Match, If-Unmodified-Since, If-Modified-Since.

#### Scenario: Evaluation order is critical
- **WHEN** multiple conditional headers are present
- **THEN** they are evaluated in order: If-Match first, then If-None-Match, then If-Unmodified-Since, then If-Modified-Since
- **AND** the first decisive result wins

#### Scenario: If-Match failure returns 412
- **WHEN** If-Match is present and the current ETag does not match any listed tag
- **THEN** the result is `PreconditionFailed`

#### Scenario: If-None-Match hit on GET/HEAD returns 304
- **WHEN** If-None-Match is present and the current ETag matches a listed tag
- **AND** the method is GET or HEAD
- **THEN** the result is `NotModified`
- **WHEN** the method is not GET/HEAD
- **THEN** the result is `PreconditionFailed`

#### Scenario: If-Modified-Since on unchanged resource returns 304
- **WHEN** If-Modified-Since is present on a GET or HEAD request
- **AND** the resource's Last-Modified date is at or before the specified date
- **THEN** the result is `NotModified`

#### Scenario: Wildcard ETag matching
- **WHEN** the conditional header value is "*"
- **THEN** it matches any existing ETag

---

### Requirement: ETag comparison

`ETagComparer` implements RFC 9110 section 8.8.3 entity-tag comparison with both strong
and weak matching modes.

#### Scenario: Strong comparison rejects weak ETags
- **WHEN** either ETag has a W/ prefix
- **THEN** `StrongMatch` returns false (weak ETags never match in strong comparison)

#### Scenario: Weak comparison ignores W/ prefix
- **WHEN** two ETags have the same opaque-tag but one has W/ and the other does not
- **THEN** `WeakMatch` returns true

#### Scenario: Surrounding quotes are stripped
- **WHEN** ETags are compared
- **THEN** surrounding double-quotes are removed before opaque-tag comparison

---

### Requirement: Content negotiation

`QualityValue` and `AcceptMatcher` implement RFC 9110 sections 12.4-12.5 content negotiation
for media types, encodings, and language tags.

#### Scenario: Quality value parsing and sorting
- **WHEN** a comma-separated list of quality values is parsed (e.g. "text/html;q=0.9, application/json")
- **THEN** values without q default to 1.0
- **AND** the list is sorted descending by quality
- **AND** quality values are clamped to [0, 1]

#### Scenario: Media type matching with wildcards
- **WHEN** the accept pattern is `*/*`
- **THEN** it matches any offered media type
- **WHEN** the pattern is `text/*`
- **THEN** it matches any text subtype (e.g. text/html, text/plain)

#### Scenario: Encoding matching
- **WHEN** the accept-encoding pattern is `*`
- **THEN** it matches any offered encoding
- **WHEN** the pattern is `identity`
- **THEN** it matches any offered encoding (identity is always acceptable)

#### Scenario: Language tag prefix matching
- **WHEN** the accept-language pattern is "en"
- **THEN** it matches "en-US", "en-GB", and "en" itself

---

### Requirement: Range request handling

`RangeParser` and related validators implement RFC 9110 section 14 byte range handling.

#### Scenario: Range header parsing
- **WHEN** the Range header is "bytes=0-499"
- **THEN** a single ByteRange with Start=0, End=499 is returned
- **WHEN** the header is "bytes=-500"
- **THEN** a suffix range with SuffixLength=500 is returned
- **WHEN** the header is "bytes=500-"
- **THEN** an open-ended range with Start=500 is returned

#### Scenario: Multiple ranges
- **WHEN** the Range header contains comma-separated ranges (e.g. "bytes=0-499,500-999")
- **THEN** all ranges are returned in order

#### Scenario: Content-Range response parsing
- **WHEN** the Content-Range header is "bytes 0-499/1000"
- **THEN** Start=0, End=499, CompleteLength=1000 is parsed
- **WHEN** the header is "bytes */1000" (unsatisfied)
- **THEN** IsUnsatisfied=true with CompleteLength=1000

#### Scenario: 206 Partial Content validation
- **WHEN** a 206 response has neither Content-Range nor multipart/byteranges Content-Type
- **THEN** `PartialContentValidator.Validate` reports IsValid=false

#### Scenario: If-Range validation
- **WHEN** If-Range is present without a Range header
- **THEN** `IfRangeValidator` throws (RFC 9110 section 13.1.5)
- **WHEN** If-Range contains a weak ETag
- **THEN** `IfRangeValidator` throws (weak ETags forbidden)

---

### Requirement: Trailer field validation

`TrailerFieldValidator` enforces RFC 9110 sections 6.5-6.6.2 restrictions on which headers
may appear in the trailer section.

#### Scenario: Restricted headers in trailers
- **WHEN** a trailer field is Transfer-Encoding, Content-Encoding, Content-Length, Connection, Keep-Alive, Trailer, TE, Upgrade, Proxy-Authenticate, or Proxy-Authorization
- **THEN** `IsAllowedInTrailer` returns false

#### Scenario: Regular fields in trailers
- **WHEN** a trailer field is not in the restricted set (e.g. "grpc-status", "server-timing")
- **THEN** `IsAllowedInTrailer` returns true

---

### Requirement: URI sanitization

`UriSanitizer` implements RFC 9110 section 4.2.4 rules for stripping userinfo from URIs
and formatting authority components.

#### Scenario: Userinfo stripping
- **WHEN** a URI contains userinfo (e.g. "http://user:pass@host/path")
- **THEN** `StripUserInfo` returns the URI without the userinfo component

#### Scenario: Authority formatting
- **WHEN** a URI has a default port (80 for HTTP, 443 for HTTPS)
- **THEN** `FormatAuthority` omits the port
- **WHEN** the URI has a non-default port
- **THEN** the port is included

#### Scenario: CONNECT authority always includes port
- **WHEN** `FormatAuthorityWithPort` is called
- **THEN** the port is always included even for default ports (RFC 9110 section 9.3.6)

#### Scenario: IPv6 bracket wrapping
- **WHEN** the host is an IPv6 address
- **THEN** it is enclosed in brackets (e.g. "[::1]:8080")

---

### Requirement: Authentication challenge parsing

`AuthChallenge` parses RFC 9110 section 11 authentication challenges from WWW-Authenticate
and Proxy-Authenticate headers.

#### Scenario: Basic challenge parsing
- **WHEN** the header value is `Basic realm="example.com"`
- **THEN** Scheme="basic", Realm="example.com"

#### Scenario: Bearer token68 parsing
- **WHEN** the header value is `Bearer <token>` (no = in the rest)
- **THEN** Scheme="bearer", Token68=`<token>`

#### Scenario: Multiple challenges
- **WHEN** the header contains comma-separated challenges
- **THEN** `ParseList` returns all challenges, handling quoted strings correctly

---

### Requirement: Status code classification

`StatusCodeSemantics` classifies HTTP status codes per RFC 9110 section 15.1 and identifies
heuristically cacheable status codes.

#### Scenario: Status code class by first digit
- **WHEN** the status code is 100-199 -> Informational, 200-299 -> Successful, 300-399 -> Redirection, 400-499 -> ClientError, 500-599 -> ServerError

#### Scenario: Heuristically cacheable status codes
- **WHEN** the status code is 200, 203, 204, 206, 300, 301, 308, 404, 405, 410, 414, or 501
- **THEN** `IsHeuristicallyCacheable` returns true
- **AND** all other codes return false

---

### Requirement: HeaderCollection contract

`HeaderCollection` is the internal ordered multi-valued header store used by the protocol
state machines. It preserves insertion order, supports case-insensitive lookup, and provides
zero-allocation enumeration.

#### Scenario: Case-insensitive lookup
- **WHEN** headers are retrieved by name
- **THEN** matching is case-insensitive (OrdinalIgnoreCase)

#### Scenario: Combined value with single-value fast path
- **WHEN** `GetCombined` is called and only one matching header exists
- **THEN** the stored string is returned directly (no StringBuilder allocation)
- **WHEN** multiple matching headers exist
- **THEN** values are joined with ", " (comma-space)

#### Scenario: Struct enumerator for zero-allocation foreach
- **WHEN** iterating with `foreach` over a HeaderCollection
- **THEN** the List struct enumerator is used (no IEnumerator boxing)

---

### Requirement: Cache storability rules

`Cache.ShouldStore` determines whether a response may be cached per RFC 9111.

#### Scenario: Only GET and HEAD responses are cached
- **WHEN** the request method is not GET or HEAD
- **THEN** the response is not stored

#### Scenario: Non-cacheable status codes are rejected
- **WHEN** the response status code is not in the heuristically cacheable set
- **THEN** the response is not stored

#### Scenario: no-store directive prevents storage
- **WHEN** the request or response Cache-Control contains no-store
- **THEN** the response is not stored

#### Scenario: Partial responses are not cached
- **WHEN** the response is 206 Partial Content or has a Content-Range header
- **THEN** the response is not stored

#### Scenario: must-understand with unrecognized status code
- **WHEN** the response Cache-Control contains must-understand
- **AND** the status code is not understood (not heuristically cacheable)
- **THEN** the response is not stored

#### Scenario: Shared cache rejects private responses
- **WHEN** the cache is configured as a shared cache (`SharedCache = true`)
- **AND** the response Cache-Control has `private` without field names
- **THEN** the response is not stored

#### Scenario: Body size limit
- **WHEN** the response body exceeds `MaxBodyBytes` (default 50 MiB)
- **THEN** the response is not stored and the body buffer is disposed

---

### Requirement: Cache freshness evaluation

`CacheFreshnessEvaluator` computes freshness lifetime and current age per RFC 9111
sections 4.2.1-4.2.3.

#### Scenario: Freshness lifetime priority
- **WHEN** the response has s-maxage and the cache is shared
- **THEN** s-maxage is used
- **WHEN** the response has max-age (and no s-maxage for shared)
- **THEN** max-age is used
- **WHEN** the response has Expires and Date
- **THEN** lifetime = Expires - Date
- **WHEN** the response has Last-Modified and Date (and no explicit lifetime)
- **THEN** heuristic freshness = 10% of (Date - Last-Modified), capped at 1 day

#### Scenario: Current age calculation
- **WHEN** the current age of a cached entry is computed
- **THEN** it uses the RFC 9111 section 4.2.3 algorithm:
  corrected_age = max(apparent_age, age_value + response_delay) + resident_time

#### Scenario: Age header injection
- **WHEN** a cached response is served
- **THEN** an Age header is injected with the computed current age in seconds

---

### Requirement: Cache lookup and request directive evaluation

`CacheFreshnessEvaluator.Evaluate` applies both request and response Cache-Control directives
per RFC 9111 section 4 to determine the cache lookup outcome.

#### Scenario: Request no-cache forces revalidation
- **WHEN** the request Cache-Control contains no-cache
- **THEN** the lookup result is `MustRevalidate`

#### Scenario: Response unqualified no-cache forces revalidation
- **WHEN** the response Cache-Control has no-cache without field names
- **THEN** the lookup result is `MustRevalidate`

#### Scenario: Fresh entry serves from cache
- **WHEN** freshness_lifetime > current_age
- **THEN** the lookup result is `Fresh`

#### Scenario: min-fresh tightens freshness requirement
- **WHEN** the request Cache-Control specifies min-fresh
- **AND** the remaining freshness is less than min-fresh
- **THEN** the entry is treated as stale

#### Scenario: must-revalidate on stale entry
- **WHEN** the cached entry is stale and the response has must-revalidate
- **THEN** the lookup result is `MustRevalidate`

#### Scenario: max-stale accepts stale entries
- **WHEN** the request Cache-Control specifies max-stale
- **AND** the staleness is within the max-stale tolerance
- **THEN** the lookup result is `Stale` (entry may be served)

---

### Requirement: Cache validation (conditional requests)

`CacheValidationRequestBuilder` implements RFC 9111 section 4.3 conditional revalidation.

#### Scenario: Building conditional GET request
- **WHEN** a stale entry has an ETag
- **THEN** the conditional request includes If-None-Match with the ETag
- **WHEN** the entry has a Last-Modified date
- **THEN** the conditional request includes If-Modified-Since

#### Scenario: Merging 304 Not Modified response
- **WHEN** the origin returns 304 Not Modified
- **THEN** headers from the 304 response override stored headers
- **AND** the cached body is reused (200 OK response is reconstructed)

#### Scenario: HEAD validation
- **WHEN** a HEAD validation request is built
- **THEN** it uses HEAD method and includes If-None-Match / If-Modified-Since
- **AND** the request has no body

#### Scenario: Freshening from HEAD 304
- **WHEN** a HEAD 304 response is received and the ETag strongly matches the stored entry
- **THEN** stored response headers are updated from the 304 response
- **WHEN** the ETag does not strongly match
- **THEN** the freshening attempt returns false

---

### Requirement: Cache Vary support

The cache supports RFC 9111 Vary header matching with variant-keyed storage.

#### Scenario: Vary header matching
- **WHEN** a cached entry has Vary header names
- **THEN** a cache hit requires the request to have matching values for all Vary header names

#### Scenario: Vary: * never matches
- **WHEN** a Vary header name is "*"
- **THEN** the cache lookup never matches (each response is unique)

#### Scenario: LRU eviction
- **WHEN** the cache exceeds `MaxEntries` (default 1000) or `MaxTotalBytes` (default 256 MiB)
- **THEN** the least-recently-used entries are evicted until within limits
- **AND** evicted entries have their body buffers disposed

---

### Requirement: Connection header parsing

`ConnectionHeaderSemantics` parses RFC 9110 section 7.6.1 Connection header values.

#### Scenario: close option detection
- **WHEN** the Connection header contains "close" (case-insensitive)
- **THEN** `HasCloseOption` returns true

#### Scenario: upgrade option detection
- **WHEN** the Connection header contains "upgrade" (case-insensitive)
- **THEN** `HasUpgradeOption` returns true

#### Scenario: Token parsing
- **WHEN** the Connection header is parsed
- **THEN** comma-separated tokens are returned in lowercase

---

### Requirement: Date header caching

`DateHeaderCache` provides a pre-formatted Date header value per RFC 9110 section 6.6.1,
refreshed at most once per second to avoid per-response formatting overhead.

#### Scenario: Cached value refresh
- **WHEN** `GetValue` or `GetDateHeaderLine` is called
- **AND** more than 1 second has elapsed since the last refresh
- **THEN** a new RFC 1123 formatted date string is generated
- **AND** a pre-encoded ASCII byte array for direct wire writing is cached

#### Scenario: Thread safety
- **WHEN** multiple connection threads call `GetDateHeaderLine` concurrently
- **THEN** the reference is swapped atomically via `Volatile.Read/Write`

---

### Requirement: Content-Length string caching

`ContentLengthCache` avoids per-response string allocations for common Content-Length values.

#### Scenario: Small values served from cache
- **WHEN** the Content-Length is between 0 and 2048
- **THEN** a pre-computed string is returned (no allocation)
- **WHEN** the value exceeds 2048
- **THEN** a new string is allocated via `ToString`

---

### Requirement: Reconnect backoff

`ReconnectBackoff` and `ReconnectPolicy` provide exponential-backoff-with-jitter for client
reconnect retries, shared across all protocol versions to avoid tight reconnect loops.

#### Scenario: Exponential growth with jitter
- **WHEN** successive reconnect attempts fail
- **THEN** the delay grows geometrically by the multiplier (default 2.0)
- **AND** symmetric jitter (default 0.2) is applied
- **AND** the delay is capped at `maxBackoff`
- **AND** the delay is never less than 1 ms

#### Scenario: Attempt exhaustion
- **WHEN** the maximum number of reconnect attempts is reached
- **THEN** the buffered work is returned to the caller for failure
- **AND** a `DisconnectTransport` is emitted
