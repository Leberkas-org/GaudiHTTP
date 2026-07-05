# The Story: From TurboHTTP to GaudiHTTP

Every project has an origin story. This one starts in February 2026 with a commit
literally named `init TurboHttp`, followed shortly by two of the most honest commit
messages in the history: `First try` and `fix`.

This page is the story of how a "let's see if I can build an HTTP client on
Akka.Streams" experiment turned into a full HTTP/1.0–HTTP/3 client *and* server —
and everything learned along the way.

## February 2026 — "First try"

TurboHTTP began as a question: **what would an HTTP client look like if the entire
pipeline — framing, correlation, flow control, retries — were expressed as
Akka.Streams stages instead of hand-rolled async plumbing?**

The name "Turbo" said everything about the ambition and nothing about the reality.
The early commit log doesn't hide it: `First try`, `fix`, `Big Steps`, `Better
Version`, and at one memorable low point, simply `KEKW`. The first working pipeline
could speak HTTP/1.1 to a real server — sometimes.

The first real lesson came fast: **you cannot bolt correctness onto a protocol
implementation afterwards.** HTTP looks simple from the outside; it is a minefield
of edge cases (chunked trailers, pipelining, connection reuse, header folding)
that only an RFC can settle.

## March 2026 — The RFC discipline

So the project changed its working style. Instead of "write code, then see what
breaks," every protocol behavior became a task traceable to a specific RFC section:
request-line compliance to RFC 1945, chunked transfer to RFC 9112 §7.1, HTTP/2
frame handling to RFC 9113 §4.1, and so on. Hundreds of test tasks later, the
suite had grown into thousands of specs — each one tagged with the RFC section it
proves.

To make that sustainable, all 393 relevant RFC sections were converted into a
local, structured knowledge vault. When a test asserts something about GOAWAY
handling, the exact normative text is one lookup away.

That same month, HTTP/3 work began — QUIC variable-length integers (RFC 9000 §16),
QPACK, control streams, the works. Lesson two arrived immediately: **HTTP/3 is not
"HTTP/2 over UDP."** It is a different world with its own stream model, and the
first design (an actor per QUIC connection) had to be rethought more than once
before the multiplexing was right.

## April–May 2026 — Becoming a real project

Spring was about turning an experiment into an engineered system:

- A proper **transport layer** split (TCP and QUIC as clean, swappable providers).
- A unified **state-machine architecture**: every protocol version (H1.0, H1.1,
  H2, H3) implements the same `IHttpStateMachine` contract, driven by a shared
  generic connection stage. This was the single biggest architectural payoff of
  the whole project — new protocol behavior slots in without touching the stream
  topology.
- **Permanent tracing instrumentation** instead of ad-hoc debug output — every
  state machine and stage emits structured trace events at five levels.
- Automated releases with release-please: 0.2.0 in early May, then a rapid climb
  through 0.4.0, 0.9.x, **1.0.0 and 2.0.0 within a single week in late May**.

And then the plot twist: in mid-May, the project grew a **server**. If the stream
topology can decode requests as a client, it can decode them as a listener — so
TurboHTTP.Server was born as a drop-in ASP.NET Core `IServer`, a Kestrel
alternative built on the same protocol layer as the client.

Lesson three: **symmetry is a feature.** Client and server share the frame
decoders, the semantics validators, the header machinery. A bug fixed once is
fixed twice.

## June 2026 — The performance era (and the humility that comes with it)

With correctness largely settled (a dedicated bug audit closed out 32 of 32
tracked issues), June was about speed — benchmarking against Kestrel and
`HttpClient` with proper methodology: process-wide allocation tracking via
EventPipe, out-of-process servers to avoid contamination, fixed-iteration
monitoring runs for heavy workloads.

The results were both flattering and brutal:

- The HTTP/2 server outran Kestrel by **2–2.4×** on plaintext throughput.
- The client's built-in RFC 9111 cache delivered roughly **13×** the goodput of
  raw `HttpClient` on cache-hit workloads (which has no cache at all).
- And the upload path allocated memory at a rate best described as *catastrophic* —
  at one point 83 GB in a benchmark run, traced to unbounded body pumping across
  thread boundaries.

That last number triggered the hardest engineering month of the project: the
**body-handling saga**. The outbound body path was redesigned around pumps with a
credit system, adaptive budgets (AIMD), and flow-control window reservation. It
was built, partially reverted, rebuilt, and reverted again before landing on the
current architecture — serial, multiplexed, and flow-controlled pumps on the way
out; queued and buffered readers on the way in.

Lesson four, the expensive one: **when you find yourself stacking a second
workaround on the same component, stop patching and question the architecture.**
Both full reverts were the right call, and each rebuild was better because the
failed attempt had mapped the terrain.

## June 24, 2026 — Gaudi

By midsummer the name "TurboHTTP" no longer fit. The project wasn't a turbo
anything — it was a carefully structured system with an unusual architecture,
built section by section against the RFCs, by someone visibly enjoying the craft.

So it was rebranded **GaudiHTTP**. The name works twice over: in Bavarian, *a
Gaudi* is a great time — which this project genuinely is — and it tips its hat to
Antoni Gaudí, who also built unconventional structures that turned out to be
load-bearing.

## Where it stands today

Nearly 1,900 commits in, GaudiHTTP is:

- A **client and server** for HTTP/1.0, 1.1, 2, and 3 (QUIC), built on
  Akka.Streams.
- Backed by **thousands of RFC-tagged specs** plus three integration suites that
  exercise the client against real servers, the server against real clients, and
  both ends against each other.
- Faster than Kestrel on the workloads it targets, with the benchmark
  infrastructure to prove it honestly — and a public list of the places where it
  isn't yet.

The learning hasn't stopped; it has just moved up the stack — outbound
flow-control redesign, HTTP/3 hardening, allocation hunting. But that's the next
chapter.

## The short version

| | |
|---|---|
| **Feb 2026** | `init TurboHttp` — an experiment in streams-based HTTP |
| **Mar 2026** | RFC-driven testing discipline; HTTP/3 and QUIC work begins |
| **Apr–May 2026** | Unified state-machine architecture; transports; the server is born; 1.0 → 2.0 |
| **Jun 2026** | Performance era: beats Kestrel on H2; body-handling redesign; 3.0 alpha line |
| **Jun 24, 2026** | Rebranded to **GaudiHTTP** |
| **Today** | ~1,900 commits, thousands of specs, and still *a Gaudi* |
