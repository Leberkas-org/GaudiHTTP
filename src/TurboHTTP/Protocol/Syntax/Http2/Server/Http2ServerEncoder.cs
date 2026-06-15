using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

/// <summary>
/// Encodes HTTP/2 response messages into HEADERS frame sequences.
/// Header-only encoder: response body streaming is handled by Http2ServerStateMachine via ResponseBodyHandle.
/// Stateful: maintains HPACK encoder for connection lifetime.
/// </summary>
internal sealed class Http2ServerEncoder
{
    private readonly Http2ServerEncoderOptions _options;
    private HpackEncoder _hpack;

    // Reused across Encode() calls to avoid List<HpackHeader> allocation per response
    private readonly List<HpackHeader> _reusableHeaders = new(16);

    // Reused across Encode() calls to avoid List<Http2Frame> allocation per response
    private readonly List<Http2Frame> _reusableFrames = new(8);

    // Tracks MemoryPool rentals from the previous EncodeHeaders() call
    private readonly List<IMemoryOwner<byte>> _rentedBodyOwners = new(4);

    private int _cachedStatusCode;
    private int _cachedHeaderCount;
    private string? _cachedContentType;
    private string? _cachedContentLength;
    private IMemoryOwner<byte>? _cachedHeaderBlockOwner;
    private int _cachedHeaderBlockLength;

    public int MaxFrameSize { get; private set; }

    public Http2ServerEncoder(Http2ServerEncoderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _hpack = new HpackEncoder(useHuffman: options.UseHuffman);
        MaxFrameSize = options.MaxFrameSize;
    }

    private void EncodeHeaderFrames(List<Http2Frame> frames, int streamId, ReadOnlyMemory<byte> headerBlock,
        bool endStream)
    {
        if (headerBlock.Length <= MaxFrameSize)
        {
            frames.Add(new HeadersFrame(streamId, headerBlock, endStream: endStream, endHeaders: true));
            return;
        }

        // Fragmented header block
        frames.Add(new HeadersFrame(streamId, headerBlock[..MaxFrameSize], endStream: false, endHeaders: false));

        var pos = MaxFrameSize;
        while (pos < headerBlock.Length)
        {
            var chunkSize = Math.Min(headerBlock.Length - pos, MaxFrameSize);
            var isLast = pos + chunkSize >= headerBlock.Length;
            frames.Add(new ContinuationFrame(streamId, headerBlock[pos..(pos + chunkSize)], endHeaders: isLast));
            pos += chunkSize;
        }
    }

    public IReadOnlyList<Http2Frame> EncodeHeaders(IFeatureCollection features, int streamId, bool hasBody)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (streamId < 0)
        {
            throw new HttpProtocolException("HTTP/2 stream ID space exhausted: all server stream IDs have been used.");
        }

        ReturnRentedBuffers();

        var responseFeature = features.Get<IHttpResponseFeature>();
        var statusCode = responseFeature?.StatusCode ?? 500;
        var responseHeaders = responseFeature?.Headers;

        if (TryUseCachedHeaderBlock(statusCode, responseHeaders, out var cachedBlock))
        {
            _reusableFrames.Clear();
            EncodeHeaderFrames(_reusableFrames, streamId, cachedBlock, endStream: !hasBody);
            return _reusableFrames;
        }

        _reusableHeaders.Clear();
        BuildHeaderList(statusCode, responseHeaders, _reusableHeaders);

        var hpackOwner = MemoryPool<byte>.Shared.Rent(EstimateHpackBufferSize(_reusableHeaders));
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, _options.UseHuffman);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];

        UpdateHeaderCache(statusCode, responseHeaders, hpackOwner, hpackBytesWritten);

        _reusableFrames.Clear();
        EncodeHeaderFrames(_reusableFrames, streamId, headerBlock, endStream: !hasBody);

        return _reusableFrames;
    }

    private bool TryUseCachedHeaderBlock(int statusCode, IHeaderDictionary? headers,
        out ReadOnlyMemory<byte> headerBlock)
    {
        if (_cachedHeaderBlockOwner is null || statusCode != _cachedStatusCode)
        {
            headerBlock = default;
            return false;
        }

        var count = headers?.Count ?? 0;
        if (count != _cachedHeaderCount)
        {
            headerBlock = default;
            return false;
        }

        string? ct = null;
        string? cl = null;
        if (headers is not null)
        {
            if (headers.TryGetValue(WellKnownHeaders.ContentType, out var ctv))
            {
                ct = ctv.ToString();
            }

            if (headers.TryGetValue(WellKnownHeaders.ContentLength, out var clv))
            {
                cl = clv.ToString();
            }
        }

        if (!string.Equals(ct, _cachedContentType, StringComparison.Ordinal)
            || !string.Equals(cl, _cachedContentLength, StringComparison.Ordinal))
        {
            headerBlock = default;
            return false;
        }

        headerBlock = _cachedHeaderBlockOwner.Memory[.._cachedHeaderBlockLength];
        return true;
    }

    private void UpdateHeaderCache(int statusCode, IHeaderDictionary? headers,
        IMemoryOwner<byte> owner, int length)
    {
        _cachedStatusCode = statusCode;
        _cachedHeaderCount = headers?.Count ?? 0;

        if (headers is not null)
        {
            _cachedContentType = headers.TryGetValue(WellKnownHeaders.ContentType, out var ctv)
                ? ctv.ToString() : null;
            _cachedContentLength = headers.TryGetValue(WellKnownHeaders.ContentLength, out var clv)
                ? clv.ToString() : null;
        }
        else
        {
            _cachedContentType = null;
            _cachedContentLength = null;
        }

        _cachedHeaderBlockOwner?.Dispose();
        var cached = MemoryPool<byte>.Shared.Rent(length);
        owner.Memory[..length].CopyTo(cached.Memory);
        _cachedHeaderBlockOwner = cached;
        _cachedHeaderBlockLength = length;
    }

    private void BuildHeaderList(int statusCode, IHeaderDictionary? responseHeaders, List<HpackHeader> headers)
    {
        headers.Add(new HpackHeader(WellKnownHeaders.Status,
            WellKnownHeaders.GetStatusCodeString(statusCode)));

        if (responseHeaders is not null)
        {
            foreach (var h in responseHeaders)
            {
                if (ContentHeaderClassifier.IsForbiddenConnectionHeader(h.Key))
                {
                    continue;
                }

                var value = ContentHeaderClassifier.JoinHeaderValues(h.Value);
                headers.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(h.Key), value));
            }
        }

        if (_options.WriteDateHeader)
        {
            var hasDate = false;
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i].Name.Equals(WellKnownHeaders.Date, StringComparison.OrdinalIgnoreCase))
                {
                    hasDate = true;
                    break;
                }
            }

            if (!hasDate)
            {
                headers.Add(new HpackHeader("date", DateHeaderCache.GetValue()));
            }
        }
    }

    /// <summary>
    /// Applies client settings to the encoder (e.g., MAX_FRAME_SIZE, HEADER_TABLE_SIZE).
    /// RFC 9113 §6.5: Received SETTINGS ACK updates encoder state.
    /// </summary>
    public void ApplyClientSettings(IEnumerable<(SettingsParameter Key, uint Value)> settings)
    {
        foreach (var (key, val) in settings)
        {
            switch (key)
            {
                case SettingsParameter.MaxFrameSize:
                    MaxFrameSize = (int)val;
                    InvalidateHeaderCache();
                    break;
                case SettingsParameter.HeaderTableSize:
                    _hpack.AcknowledgeTableSizeChange((int)val);
                    InvalidateHeaderCache();
                    break;
            }
        }
    }

    /// <summary>
    /// Encodes trailer headers into HTTP/2 HEADERS frame(s).
    /// RFC 9113 §8.1: Trailers are sent as a HEADERS frame with END_STREAM.
    /// RFC 9110 §6.5.1: Filters prohibited trailer fields (transfer-encoding, content-length, etc.).
    /// </summary>
    public IReadOnlyList<Http2Frame> EncodeTrailers(int streamId, IHeaderDictionary trailers)
    {
        ArgumentNullException.ThrowIfNull(trailers);

        ReturnRentedBuffers();

        _reusableHeaders.Clear();

        foreach (var header in trailers)
        {
            if (TrailerFieldValidator.IsAllowedInTrailer(header.Key))
            {
                _reusableHeaders.Add(new HpackHeader(ContentHeaderClassifier.ToLowerAscii(header.Key),
                    header.Value.ToString()));
            }
        }

        if (_reusableHeaders.Count == 0)
        {
            return [];
        }

        var hpackOwner = MemoryPool<byte>.Shared.Rent(EstimateHpackBufferSize(_reusableHeaders));
        _rentedBodyOwners.Add(hpackOwner);
        var hpackWritable = hpackOwner.Memory.Span;
        var hpackBytesWritten = _hpack.Encode(_reusableHeaders, ref hpackWritable, _options.UseHuffman);
        var headerBlock = hpackOwner.Memory[..hpackBytesWritten];

        _reusableFrames.Clear();
        EncodeHeaderFrames(_reusableFrames, streamId, headerBlock, endStream: true);

        return _reusableFrames;
    }

    /// <summary>
    /// Resets HPACK encoder state for reconnect.
    /// </summary>
    public void ResetHpack()
    {
        _hpack = new HpackEncoder(useHuffman: _options.UseHuffman);
        InvalidateHeaderCache();
    }

    private void InvalidateHeaderCache()
    {
        _cachedHeaderBlockOwner?.Dispose();
        _cachedHeaderBlockOwner = null;
        _cachedHeaderBlockLength = 0;
    }

    // The HPACK encoder writes into a single rented span, so it must be large enough for the whole
    // block. A fixed 4096-byte buffer overflowed (ArgumentOutOfRange/IndexOutOfRange) on large
    // header sets, dropping the response. Size to a literal-encoding upper bound (Huffman only
    // shrinks); ×2 guards any octet expansion. CONTINUATION fragmentation still applies downstream.
    private static int EstimateHpackBufferSize(List<HpackHeader> headers)
    {
        var size = 128;
        for (var i = 0; i < headers.Count; i++)
        {
            size += (headers[i].Name.Length + headers[i].Value.Length) * 2 + 16;
        }

        return Math.Max(4096, size);
    }

    private void ReturnRentedBuffers()
    {
        for (var i = 0; i < _rentedBodyOwners.Count; i++)
        {
            _rentedBodyOwners[i].Dispose();
        }

        _rentedBodyOwners.Clear();
    }
}