using System.Net;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;

namespace GaudiHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientDecoder(int maxHeaderSize, int maxTotalHeaderSize)
{
    private const string PseudoHeaderSection = "RFC 9113 §8.1.2.2";
    private const string UppercaseSection = "RFC 9113 §8.2.1";
    private const string TokenSection = "RFC 9113 §10.3";
    private const string ConnectionSection = "RFC 9113 §8.2.2";

    private static readonly HttpContent SharedEmptyContent = new ByteArrayContent([]);

    // RFC 9113 §6.5.2: enforce the cumulative decoded header-list size (MAX_HEADER_LIST_SIZE) inside the
    // HPACK decoder so a decompression bomb is rejected mid-decode, before the full list is materialized.
    private HpackDecoder _hpack = CreateHpack(maxTotalHeaderSize);

    private static HpackDecoder CreateHpack(int maxHeaderListSize)
    {
        var hpack = new HpackDecoder();
        hpack.SetMaxHeaderListSize(maxHeaderListSize);
        return hpack;
    }

    public void SetMaxAllowedTableSize(int size)
    {
        _hpack.SetMaxAllowedTableSize(size);
    }

    public void ResetHpack()
    {
        _hpack = CreateHpack(maxTotalHeaderSize);
    }

    public HttpResponseMessage? DecodeHeaders(int streamId, bool endStream, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateResponseHeaders(headers);

        var response = new HttpResponseMessage();
        AssembleResponse(headers, response, state);

        if ((int)response.StatusCode < 200)
        {
            return response;
        }

        state.InitResponse(response);

        if (!endStream)
        {
            return null;
        }

        response.Content = state.HasContentHeaders
            ? new ByteArrayContent([])
            : SharedEmptyContent;
        state.ApplyContentHeadersTo(response.Content);

        return response;
    }

    public HttpResponseMessage DecodeHeadersForStreaming(int streamId, StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateResponseHeaders(headers);

        var response = new HttpResponseMessage();
        AssembleResponse(headers, response, state);

        if ((int)response.StatusCode >= 200)
        {
            state.InitResponse(response);
        }

        return response;
    }

    public void DecodeTrailers(StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());

        foreach (var h in headers)
        {
            if (h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                continue;
            }

            if (TrailerFieldValidator.IsAllowedInTrailer(h.Name))
            {
                state.GetResponse().TrailingHeaders.TryAddWithoutValidation(h.Name, h.Value);
            }
        }
    }

    internal static void ValidateResponseHeaders(List<HpackHeader> headers)
    {
        PseudoHeaderValidator.ValidateResponsePseudoHeaders(
            headers,
            static h => h.Name,
            PseudoHeaderSection);

        FieldValidator.Validate(
            headers,
            static h => h.Name,
            static h => h.Value,
            UppercaseSection,
            TokenSection,
            TokenSection,
            ConnectionSection);
    }

    private void ValidateHeaderSize(List<HpackHeader> headers, int streamId)
    {
        // Cumulative header-list size is enforced inside the HPACK decoder (MAX_HEADER_LIST_SIZE); here we
        // only bound the size of any single header field (RFC 9113 §10.5.1).
        for (var i = 0; i < headers.Count; i++)
        {
            var headerSize = headers[i].Name.Length + headers[i].Value.Length;

            if (headerSize > maxHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                    $"exceeds MaxHeaderSize limit ({maxHeaderSize} bytes) " +
                    $"on stream {streamId} - header '{headers[i].Name}'.");
            }
        }
    }

    private static void AssembleResponse(List<HpackHeader> headers, HttpResponseMessage response, StreamState state)
    {
        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Status)
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
            }
            else if (!h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }
    }
}
