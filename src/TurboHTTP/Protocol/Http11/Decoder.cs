using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http11;

/// <summary>
/// RFC 9112 compliant HTTP/1.1 response decoder with zero-allocation patterns.
/// Uses MemoryPool for buffer management to minimize GC pressure.
/// </summary>
internal sealed class Decoder : IDisposable
{
    private delegate HttpDecodeResult ParseOneFunc(ReadOnlySpan<byte> buffer, out HttpResponseMessage? response,
        out int consumed);

    private IMemoryOwner<byte>? _remainderOwner;
    private int _remainderLength;

    private IMemoryOwner<byte>? _bodyOwner;
    private int _bodyLength;

    private bool _disposed;

    private HttpResponseMessage? _pendingCloseDelimitedResponse;
    private List<TransportBuffer>? _closeDelimitedChunks;
    private byte[]? _closeDelimitedInitialBytes;

    private readonly List<HttpResponseMessage> _decodeBuffer = [];

    private const int DefaultMaxHeaderSize = 16 * 1024; // 16 KB per header field
    private const int DefaultMaxTotalHeaderSize = 64 * 1024; // 64 KB total headers
    private const int DefaultMaxBodySize = 10_485_760; // 10 MB
    private const int DefaultMaxHeaderCount = 100;

    private readonly HeaderDecoder _headerDecoder;
    private readonly HeaderDecoder _trailerDecoder;
    private readonly int _maxTotalHeaderSize;
    private readonly int _maxBodySize;

    /// <summary>
    /// Creates a new HTTP/1.1 decoder with configurable limits.
    /// </summary>
    /// <param name="maxHeaderSize">Maximum single header field size in bytes (default: 16KB)</param>
    /// <param name="maxTotalHeaderSize">Maximum total header size in bytes (default: 64KB)</param>
    /// <param name="maxBodySize">Maximum body size in bytes (default: 10MB)</param>
    /// <param name="maxHeaderCount">Maximum number of header fields allowed (default: 100)</param>
    public Decoder(
        int maxHeaderSize = DefaultMaxHeaderSize,
        int maxTotalHeaderSize = DefaultMaxTotalHeaderSize,
        int maxBodySize = DefaultMaxBodySize,
        int maxHeaderCount = DefaultMaxHeaderCount)
    {
        _headerDecoder = new HeaderDecoder(maxHeaderSize, maxTotalHeaderSize, maxHeaderCount);
        _trailerDecoder = new HeaderDecoder(maxHeaderSize, maxTotalHeaderSize, maxHeaderCount);
        _maxTotalHeaderSize = maxTotalHeaderSize;
        _maxBodySize = maxBodySize;
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data.
    /// </summary>
    /// <param name="incomingData">New data received from the network</param>
    /// <param name="responses">Decoded responses (may contain multiple for pipelining)</param>
    /// <returns>True if at least one response was decoded</returns>
    public bool TryDecode(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DecodeLoop(incomingData, TryParseOne, out responses);
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data where the original
    /// request was a HEAD request. Parses headers only and always returns an empty body,
    /// regardless of any <c>Content-Length</c> value in the response headers.
    /// </summary>
    /// <remarks>
    /// RFC 9112 §6.3: Any response to a HEAD request is terminated by the first empty
    /// line after the header fields and cannot contain a message body.
    /// </remarks>
    public bool TryDecodeHead(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DecodeLoop(incomingData, TryParseOneNoBody, out responses);
    }

    private bool DecodeLoop(ReadOnlyMemory<byte> incomingData, ParseOneFunc parseOne,
        out IReadOnlyList<HttpResponseMessage> responses)
    {
        _decodeBuffer.Clear();
        responses = [];

        // Combine remainder with incoming data using pooled buffer
        ReadOnlySpan<byte> working;
        IMemoryOwner<byte>? combinedOwner = null;

        if (_remainderLength > 0)
        {
            var combinedLength = _remainderLength + incomingData.Length;
            combinedOwner = MemoryPool<byte>.Shared.Rent(combinedLength);

            _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(combinedOwner.Memory.Span);
            incomingData.Span.CopyTo(combinedOwner.Memory.Span[_remainderLength..]);

            working = combinedOwner.Memory.Span[..combinedLength];
            ClearRemainder();
        }
        else
        {
            working = incomingData.Span;
        }

        try
        {
            var consumed = 0;

            while (consumed < working.Length)
            {
                var result = parseOne(working[consumed..], out var response, out var bytesConsumed);

                if (result.Success)
                {
                    consumed += bytesConsumed;

                    // Skip 1xx informational responses (RFC 9112 Section 4)
                    if ((int)response!.StatusCode >= 100 && (int)response.StatusCode < 200)
                    {
                        continue;
                    }

                    _decodeBuffer.Add(response);
                    continue;
                }

                if (result.Error == HttpDecoderError.NeedMoreData)
                {
                    // Store remainder in pooled buffer
                    StoreRemainder(working[consumed..]);
                    break;
                }

                ClearRemainder();
                throw new HttpDecoderException(result.Error!.Value);
            }
        }
        finally
        {
            combinedOwner?.Dispose();
        }

        if (_decodeBuffer.Count <= 0)
        {
            return false;
        }

        responses = new List<HttpResponseMessage>(_decodeBuffer);
        return true;
    }

    /// <summary>
    /// Attempts to decode HTTP/1.1 responses from incoming data where the original
    /// request was a CONNECT request. A successful (2xx) CONNECT response has no body
    /// (the connection transitions to a tunnel), regardless of any Content-Length or
    /// Transfer-Encoding headers. Non-2xx responses are decoded with normal body handling.
    /// </summary>
    /// <remarks>
    /// RFC 9110 §9.3.6: A server MUST NOT send Content-Length or Transfer-Encoding
    /// in a 2xx (Successful) response to CONNECT. A client MUST ignore any such
    /// header fields received in a successful CONNECT response.
    /// </remarks>
    public bool TryDecodeConnect(ReadOnlyMemory<byte> incomingData, out IReadOnlyList<HttpResponseMessage> responses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DecodeLoop(incomingData, TryParseOneConnect, out responses);
    }

    /// <summary>
    /// Attempts to complete a partially buffered response when the connection has closed cleanly.
    /// Called when a TLS close_notify or TCP FIN is received and the server used
    /// connection-close framing (no Content-Length, no Transfer-Encoding).
    /// </summary>
    /// <remarks>
    /// RFC 9112 §9.8: A server MAY close the connection at the end of a response when
    /// the response does not include Content-Length or Transfer-Encoding.
    /// The entire remainder after the header section is treated as the message body.
    /// </remarks>
    /// <param name="response">The completed response, or null if no valid header section was buffered.</param>
    /// <returns>True if a complete response was assembled from the remainder buffer.</returns>
    public bool TryDecodeEof(out HttpResponseMessage? response)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        response = null;
        if (_remainderLength == 0)
        {
            return false;
        }

        var working = _remainderOwner!.Memory.Span[.._remainderLength];

        // Find header/body boundary (CRLF CRLF)
        var headerEnd = BufferSearch.FindCrlfCrlf(working);
        if (headerEnd < 0)
        {
            return false;
        }

        // Include the CRLF that terminates the last header
        var headerSection = working[..(headerEnd + 2)];

        // Parse status line
        var statusLineEnd = BufferSearch.FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return false;
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!StatusLineDecoder.TryParse(statusLine, out var statusCode, out var reasonPhrase))
        {
            return false;
        }

        // Parse headers
        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = _headerDecoder.Parse(headersData);

        // RFC 9112 §7.1: Chunked encoding MUST be terminated by a zero-length chunk.
        // If the connection closes before the zero-chunk, the response is incomplete.
        if (GetSingleHeader(headers, WellKnownHeaders.Names.TransferEncoding) is not null)
        {
            ClearRemainder();
            return false;
        }

        var bodyStart = headerEnd + 4;
        var bodySpan = bodyStart < _remainderLength
            ? working[bodyStart..]
            : ReadOnlySpan<byte>.Empty;

        // RFC 9112 §6.2: If Content-Length is present, the body MUST be exactly that many bytes.
        // A connection close before the full body is received means a truncated (incomplete) response.
        var contentLength = GetContentLengthHeader(headers);
        if (contentLength.HasValue && bodySpan.Length < contentLength.Value)
        {
            ClearRemainder();
            return false;
        }

        response = ResponseBuilder.BuildFromRemainder(statusCode, reasonPhrase, headers, bodySpan);
        ClearRemainder();
        return true;
    }

    /// <summary>
    /// Returns any buffered remainder bytes and clears the remainder.
    /// Used by <see cref="Http11ConnectionStage"/> to extract
    /// body data that was in the same chunk as headers for connection-close-delimited responses.
    /// </summary>
    public byte[] FlushRemainder()
    {
        if (_remainderLength == 0)
        {
            return [];
        }

        var result = new byte[_remainderLength];
        _remainderOwner!.Memory.Span[.._remainderLength].CopyTo(result);
        ClearRemainder();
        return result;
    }

    internal bool HasPendingCloseDelimited => _pendingCloseDelimitedResponse is not null;

    internal void BeginCloseDelimited(HttpResponseMessage partialResponse, byte[]? initialBytes)
    {
        _pendingCloseDelimitedResponse = partialResponse;
        _closeDelimitedChunks = [];
        _closeDelimitedInitialBytes = initialBytes;
    }

    internal void AccumulateCloseDelimited(TransportBuffer buffer)
    {
        _closeDelimitedChunks ??= [];
        _closeDelimitedChunks.Add(buffer);
    }

    internal bool TryCompleteCloseDelimited(out HttpResponseMessage? response)
    {
        response = null;
        if (_pendingCloseDelimitedResponse is null)
        {
            return false;
        }

        var content = PooledBodyContent.FromChunks(_closeDelimitedInitialBytes, _closeDelimitedChunks);
        _pendingCloseDelimitedResponse.Content = content;
        response = _pendingCloseDelimitedResponse;

        _pendingCloseDelimitedResponse = null;
        _closeDelimitedChunks = null;
        _closeDelimitedInitialBytes = null;
        return true;
    }

    internal void DiscardCloseDelimited()
    {
        if (_closeDelimitedChunks is not null)
        {
            foreach (var buf in _closeDelimitedChunks)
            {
                buf.Dispose();
            }
        }

        _pendingCloseDelimitedResponse?.Dispose();
        _pendingCloseDelimitedResponse = null;
        _closeDelimitedChunks = null;
        _closeDelimitedInitialBytes = null;
    }

    /// <summary>
    /// Resets decoder state for reuse on a new connection.
    /// </summary>
    public void Reset()
    {
        ClearRemainder();
        ClearBody();
        DiscardCloseDelimited();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _remainderOwner?.Dispose();
        _remainderOwner = null;

        _bodyOwner?.Dispose();
        _bodyOwner = null;

        DiscardCloseDelimited();
    }

    private static int? GetContentLengthHeader(Dictionary<string, List<string>> headers)
    {
        if (!headers.TryGetValue(WellKnownHeaders.Names.ContentLength, out var values) || values.Count == 0)
        {
            return null;
        }

        if (values.Count > 1)
        {
            var first = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (!values[i].Equals(first, StringComparison.Ordinal))
                {
                    throw new HttpDecoderException(
                        HttpDecoderError.MultipleContentLengthValues,
                        $"Values '{first}' and '{values[i]}' conflict.");
                }
            }
        }

        return int.TryParse(values[0], out var len) && len >= 0 ? len : null;
    }

    private static string? GetSingleHeader(Dictionary<string, List<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count > 0
            ? values[0]
            : null;

    private void StoreRemainder(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if (_remainderOwner == null || _remainderOwner.Memory.Length < data.Length)
        {
            _remainderOwner?.Dispose();
            _remainderOwner = MemoryPool<byte>.Shared.Rent(data.Length);
        }

        data.CopyTo(_remainderOwner.Memory.Span);
        _remainderLength = data.Length;
    }

    private void ClearRemainder()
    {
        _remainderLength = 0;
        // Keep buffer for reuse
    }

    private void ClearBody()
    {
        _bodyLength = 0;
        // Keep buffer for reuse
    }

    private void EnsureBodyCapacity(int required)
    {
        if (_bodyOwner != null && _bodyOwner.Memory.Length >= required)
        {
            return;
        }

        var newOwner = MemoryPool<byte>.Shared.Rent(required);
        if (_bodyOwner != null)
        {
            _bodyOwner.Memory.Span[.._bodyLength].CopyTo(newOwner.Memory.Span);
            _bodyOwner.Dispose();
        }

        _bodyOwner = newOwner;
    }

    private HttpDecodeResult TryParseOneConnect(ReadOnlySpan<byte> buffer,
        out HttpResponseMessage? response, out int consumed)
    {
        var statusCode = StatusLineDecoder.PeekCode(buffer);
        return statusCode is >= 200 and < 300
            ? TryParseOneNoBody(buffer, out response, out consumed)
            : TryParseOne(buffer, out response, out consumed);
    }

    /// <summary>
    /// Parses one response but always returns an empty body (used for HEAD responses).
    /// </summary>
    private HttpDecodeResult TryParseOneNoBody(ReadOnlySpan<byte> buffer, out HttpResponseMessage? response,
        out int consumed)
    {
        response = null;
        consumed = 0;

        var headerEnd = BufferSearch.FindCrlfCrlf(buffer);
        if (headerEnd < 0)
        {
            return HttpDecodeResult.Incomplete();
        }

        if (headerEnd > _maxTotalHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.TotalHeadersTooLarge);
        }

        var headerSection = buffer[..(headerEnd + 2)];

        var statusLineEnd = BufferSearch.FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!StatusLineDecoder.TryParse(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = _headerDecoder.Parse(headersData);

        response = ResponseBuilder.BuildNoBody(statusCode, reasonPhrase, headers);
        consumed = headerEnd + 4;
        return HttpDecodeResult.Ok();
    }

    private HttpDecodeResult TryParseOne(ReadOnlySpan<byte> buffer, out HttpResponseMessage? response, out int consumed)
    {
        response = null;
        consumed = 0;

        var headerEnd = BufferSearch.FindCrlfCrlf(buffer);
        if (headerEnd < 0)
        {
            return HttpDecodeResult.Incomplete();
        }

        if (headerEnd > _maxTotalHeaderSize)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.TotalHeadersTooLarge);
        }

        var headerSection = buffer[..(headerEnd + 2)];

        var statusLineEnd = BufferSearch.FindCrlf(headerSection, 0);
        if (statusLineEnd < 0)
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var statusLine = headerSection[..statusLineEnd];
        if (!StatusLineDecoder.TryParse(statusLine, out var statusCode, out var reasonPhrase))
        {
            return HttpDecodeResult.Fail(HttpDecoderError.InvalidStatusLine);
        }

        var headersData = headerSection[(statusLineEnd + 2)..];
        var headers = _headerDecoder.Parse(headersData);

        if (ResponseBuilder.IsNoBodyResponse(statusCode))
        {
            response = ResponseBuilder.BuildNoBody(statusCode, reasonPhrase, headers);
            consumed = headerEnd + 4;
            return HttpDecodeResult.Ok();
        }

        var bodyStart = headerEnd + 4;
        var bodyData = buffer[bodyStart..];

        var (bodyResult, bodyOwner, bodyLength, bodyConsumed, trailerHeaders) = ParseBody(bodyData, headers);
        if (!bodyResult.Success)
        {
            return bodyResult;
        }

        response = ResponseBuilder.Build(statusCode, reasonPhrase, headers, bodyOwner, bodyLength, trailerHeaders);
        consumed = bodyStart + bodyConsumed;
        return HttpDecodeResult.Ok();
    }

    private (HttpDecodeResult result, IMemoryOwner<byte>? bodyOwner, int bodyLength, int consumed,
        Dictionary<string, List<string>>? trailers)
        ParseBody(ReadOnlySpan<byte> data, Dictionary<string, List<string>> headers)
    {
        var transferEncoding = GetSingleHeader(headers, WellKnownHeaders.Names.TransferEncoding);
        var contentLength = GetContentLengthHeader(headers);

        // RFC 9112 Section 6.3: Transfer-Encoding takes precedence
        if (!string.IsNullOrEmpty(transferEncoding) &&
            transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            // RFC 9112 §6.3 / Security: Reject responses with both Transfer-Encoding and Content-Length
            // to prevent HTTP request smuggling attacks.
            if (contentLength.HasValue)
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.ChunkedWithContentLength), null, 0, 0, null);
            }

            return ParseChunkedBody(data);
        }

        if (!contentLength.HasValue)
        {
            return (HttpDecodeResult.Ok(), null, 0, 0, null);
        }

        var len = contentLength.Value;

        if (len > _maxBodySize)
        {
            return (HttpDecodeResult.Fail(HttpDecoderError.InvalidContentLength), null, 0, 0, null);
        }

        if (data.Length < len)
        {
            return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
        }

        // Rent from MemoryPool instead of allocating a new byte[] via ToArray()
        var owner = MemoryPool<byte>.Shared.Rent(len);
        data[..len].CopyTo(owner.Memory.Span);
        return (HttpDecodeResult.Ok(), owner, len, len, null);

        // No Content-Length and no Transfer-Encoding: empty body
    }

    private (HttpDecodeResult result, IMemoryOwner<byte>? bodyOwner, int bodyLength, int consumed,
        Dictionary<string, List<string>>? trailers)
        ParseChunkedBody(ReadOnlySpan<byte> data)
    {
        ClearBody();
        var pos = 0;

        while (pos < data.Length)
        {
            var lineEnd = BufferSearch.FindCrlf(data, pos);
            if (lineEnd < 0)
            {
                return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
            }

            var sizeLine = data[pos..lineEnd];
            if (!TryParseChunkHeader(sizeLine, out var chunkSize, out var error))
            {
                return (HttpDecodeResult.Fail(error!.Value), null, 0, 0, null);
            }

            pos = lineEnd + 2;

            if (chunkSize == 0)
            {
                return ParseTrailers(data, pos);
            }

            if (chunkSize > _maxBodySize || _bodyLength + chunkSize > _maxBodySize)
            {
                return (HttpDecodeResult.Fail(HttpDecoderError.InvalidContentLength), null, 0, 0, null);
            }

            if (pos + chunkSize + 2 > data.Length)
            {
                return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
            }

            EnsureBodyCapacity(_bodyLength + chunkSize);
            data.Slice(pos, chunkSize).CopyTo(_bodyOwner!.Memory.Span[_bodyLength..]);
            _bodyLength += chunkSize;

            pos += chunkSize + 2;
        }

        return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
    }

    private static bool TryParseChunkHeader(ReadOnlySpan<byte> sizeLine, out int chunkSize, out HttpDecoderError? error)
    {
        chunkSize = 0;
        error = null;

        var semiIdx = sizeLine.IndexOf((byte)';');
        var sizeSpan = semiIdx >= 0 ? sizeLine[..semiIdx] : sizeLine;
        var extSpan = semiIdx >= 0 ? sizeLine[(semiIdx + 1)..] : ReadOnlySpan<byte>.Empty;

        if (!ChunkExtensionParser.TryParse(extSpan))
        {
            error = HttpDecoderError.InvalidChunkExtension;
            return false;
        }

        if (!BufferSearch.TryParseHex(sizeSpan, out chunkSize))
        {
            error = HttpDecoderError.InvalidChunkSize;
            return false;
        }

        return true;
    }

    private (HttpDecodeResult result, IMemoryOwner<byte>? bodyOwner, int bodyLength, int consumed,
        Dictionary<string, List<string>>? trailers)
        ParseTrailers(ReadOnlySpan<byte> data, int pos)
    {
        var remaining = data[pos..];

        if (remaining.Length >= 2 && remaining[0] == '\r' && remaining[1] == '\n')
        {
            var (owner, len) = RentBodyOwner();
            return (HttpDecodeResult.Ok(), owner, len, pos + 2, null);
        }

        var trailerEnd = BufferSearch.FindCrlfCrlf(remaining);
        if (trailerEnd >= 0)
        {
            var trailerData = remaining[..(trailerEnd + 2)];
            var trailers = _trailerDecoder.Parse(trailerData);

            var (owner, len) = RentBodyOwner();
            return (HttpDecodeResult.Ok(), owner, len, pos + trailerEnd + 4, trailers);
        }

        return (HttpDecodeResult.Incomplete(), null, 0, 0, null);
    }

    /// <summary>
    /// Rents a <see cref="IMemoryOwner{T}"/> from <see cref="MemoryPool{T}.Shared"/> and copies
    /// the accumulated chunked body into it. Returns null owner for empty bodies.
    /// </summary>
    private (IMemoryOwner<byte>? owner, int length) RentBodyOwner()
    {
        if (_bodyLength == 0)
        {
            return (null, 0);
        }

        var owner = MemoryPool<byte>.Shared.Rent(_bodyLength);
        _bodyOwner!.Memory.Span[.._bodyLength].CopyTo(owner.Memory.Span);
        return (owner, _bodyLength);
    }
}