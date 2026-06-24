using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.Syntax.Http2.Client;

/// <summary>
/// Encodes HTTP request messages as HTTP/2 frame sequences.
/// Stateful: maintains HPACK encoder and stream ID counter.
/// One instance per connection.
/// </summary>
internal sealed class Http2ClientEncoder(bool useHuffman)
{
    private HpackEncoder _hpack = new(useHuffman);

    /// <summary>
    /// Maximum payload size for frames this client may send, in bytes. Starts at the RFC 9113
    /// default (16,384) and is raised only when the server advertises a larger
    /// SETTINGS_MAX_FRAME_SIZE via <see cref="ApplyServerSettings"/>. This is the peer's receive
    /// limit - it is intentionally NOT driven by the client's own MaxFrameSize option.
    /// </summary>
    public int MaxFrameSize { get; private set; } = 16 * 1024;

    // Per-encoder scratch buffer for HPACK encoding. Grown on demand (grow-and-replace).
    // Actor-thread-confined: no synchronization needed. The caller (Http2ClientSessionManager)
    // copies all frame data into a TransportBuffer before the next Encode() call, so this
    // buffer is only needed for the duration of a single Encode() invocation.
    private byte[] _hpackScratch = new byte[4 * 1024];

    // Reused across Encode() calls to avoid List<HpackHeader> allocation per request.
    private readonly List<HpackHeader> _reusableHeaders = new(16);

    // Reused across Encode() calls to avoid List<Http2Frame> allocation per request.
    // Safe: callers consume the list immediately in a foreach before the next Encode() call.
    private readonly List<Http2Frame> _reusableFrames = new(8);

    /// <summary>
    /// Encodes a request to HTTP/2 frames. Returns the stream ID and frame list.
    /// Thread-safety: not thread-safe (one stream at a time per connection).
    /// </summary>
    public IReadOnlyList<Http2Frame> Encode(HttpRequestMessage request, int streamId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        if (streamId < 0)
        {
            throw new HttpProtocolException("HTTP/2 stream ID space exhausted: all client stream IDs have been used.");
        }

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);

        var needed = EstimateHpackBufferSize(_reusableHeaders);
        if (_hpackScratch.Length < needed)
        {
            _hpackScratch = new byte[needed];
        }

        var hpackWritable = _hpackScratch.AsSpan();
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman);
        var headerBlock = new ReadOnlyMemory<byte>(_hpackScratch, 0, hpackBytesWritten);
        var hasBody = request.Content != null;

        _reusableFrames.Clear();
        EncodeHeaders(_reusableFrames, streamId, headerBlock, hasBody);

        return _reusableFrames;
    }

    // The HPACK encoder writes into a single rented span, so it must hold the whole header block.
    // A fixed 4096-byte buffer overflowed (IndexOutOfRange) on large header sets, failing the
    // request. Size to a literal-encoding upper bound (Huffman only shrinks); ×2 guards octet
    // expansion. CONTINUATION fragmentation still applies downstream.
    private static int EstimateHpackBufferSize(List<HpackHeader> headers)
    {
        var size = 128;
        for (var i = 0; i < headers.Count; i++)
        {
            size += (headers[i].Name.Length + headers[i].Value.Length) * 2 + 16;
        }

        return Math.Max(4096, size);
    }

    /// <summary>
    /// TEST ONLY: Encodes a request and extracts the raw HPACK header block.
    /// Used by RFC compliance tests to verify header encoding details.
    /// </summary>
    internal byte[] EncodeToHpackBlock(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        _reusableHeaders.Clear();
        BuildHeaderList(request, _reusableHeaders);
        ValidatePseudoHeaders(_reusableHeaders);
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var hpackWritable = owner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, useHuffman);
        return owner.Memory[..hpackBytesWritten].ToArray(); // TEST ONLY: copy intentional - callers own the byte[]
    }

    private void EncodeHeaders(List<Http2Frame> frames, int streamId, ReadOnlyMemory<byte> headerBlock, bool hasBody)
    {
        if (headerBlock.Length <= MaxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock, endStream: !hasBody, endHeaders: true));
            return;
        }

        // Fragmented header block - first chunk goes in HEADERS frame
        frames.Add(new HeadersFrame(streamId, headerBlock[..MaxFrameSize], endStream: false,
            endHeaders: false));

        var pos = MaxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, MaxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId, headerBlock[pos..(pos + chunkSize)],
                endHeaders: isLast));
            pos += chunkSize;
        }
    }

    private static void BuildHeaderList(HttpRequestMessage request, List<HpackHeader> headers)
    {
        var uri = request.RequestUri!;
        var pathAndQuery = string.IsNullOrEmpty(uri.Query)
            ? uri.AbsolutePath
            : string.Concat(uri.AbsolutePath, uri.Query);

        headers.Add(new HpackHeader(WellKnownHeaders.Method, request.Method.Method));
        headers.Add(new HpackHeader(WellKnownHeaders.Path, pathAndQuery));
        headers.Add(new HpackHeader(WellKnownHeaders.Scheme, uri.Scheme));
        headers.Add(new HpackHeader(WellKnownHeaders.Authority, UriSanitizer.FormatAuthority(uri)));

        foreach (var h in request.Headers)
        {
            if (!ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
            {
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
            }
        }

        if (request.Content == null)
        {
            return;
        }

        foreach (var h in request.Content.Headers)
        {
            headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), ContentHeaderClassifier.JoinHeaderValues(h.Value)));
        }
    }

    internal static void ValidatePseudoHeaders(List<HpackHeader> headers) =>
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
            "RFC 9113 §8.3.1");

    /// <summary>
    /// Applies server settings to the encoder (e.g., MAX_FRAME_SIZE).
    /// RFC 9113 §6.5: Received SETTINGS ACK updates encoder state.
    /// Note: Flow control window updates (InitialWindowSize) are handled by IFlowController.
    /// </summary>
    public void ApplyServerSettings(IEnumerable<(SettingsParameter Key, uint Value)> settings)
    {
        foreach (var (key, val) in settings)
        {
            switch (key)
            {
                case SettingsParameter.MaxFrameSize:
                    MaxFrameSize = (int)val;
                    break;
                case SettingsParameter.HeaderTableSize:
                    _hpack.AcknowledgeTableSizeChange((int)val);
                    break;
            }
        }
    }

    /// <summary>
    /// Resets HPACK encoder state for reconnect.
    /// Must be called before replaying requests on a new connection.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackEncoder(useHuffman);
    }

}