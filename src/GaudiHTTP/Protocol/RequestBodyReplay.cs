namespace GaudiHTTP.Protocol;

/// <summary>
/// Prepares a buffered request's body to be re-sent on a reconnect replay.
///
/// The protocol encoders read the request body via <see cref="HttpContent.ReadAsStream()"/>, which
/// caches and returns the SAME stream instance on every call. After the first send attempt that
/// stream sits at (or near) EOF, so a naive replay declares the full <c>Content-Length</c> header
/// but writes 0 (or a truncated count of) body bytes onto the fresh connection — a fixed-length
/// server read then blocks forever waiting for bytes that never arrive.
///
/// This is the connection-level guard that owns the replay decision. The encoder deliberately stays
/// unaware of replay; rewinding here (not in the encoder) means it cannot be bypassed by any code
/// path that re-arms a buffered request for a new connection.
/// </summary>
internal static class RequestBodyReplay
{
    /// <summary>
    /// Rewinds a seekable request body to the start so a reconnect replay re-sends it in full.
    /// Returns <c>true</c> when the request can be replayed (no body, or a seekable body that was
    /// rewound); returns <c>false</c> for a consumed forward-only body that cannot be rewound — the
    /// caller must then fail the request rather than emit a truncated fixed-length body (RFC 9110
    /// §9.2.2: a request whose body cannot be replayed must not be replayed).
    ///
    /// This is the physical counterpart to <c>RetryBidiStage.IsBodyNonRewindable</c>: that stage
    /// decides retry eligibility by content type without touching the stream, whereas a live replay
    /// must actually reset the cached stream's position. <see cref="Stream.CanSeek"/> is the accurate
    /// runtime test here — the seekable <see cref="System.IO.MemoryStream"/> that backs
    /// <see cref="ByteArrayContent"/>/<see cref="ReadOnlyMemoryContent"/> rewinds cleanly, while a
    /// forward-only <see cref="StreamContent"/> body does not.
    /// </summary>
    public static bool TryRewindForReplay(HttpRequestMessage request)
    {
        var content = request.Content;
        if (content is null)
        {
            return true;
        }

        var body = content.ReadAsStream();
        if (!body.CanSeek)
        {
            return false;
        }

        if (body.Position != 0)
        {
            body.Position = 0;
        }

        return true;
    }
}
