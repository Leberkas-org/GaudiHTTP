using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerDecoder
{
    private const string PseudoHeaderSection = "RFC 9113 §8.3.1";
    private const string UppercaseSection = "RFC 9113 §8.2.1";
    private const string TokenSection = "RFC 9113 §10.3";
    private const string ConnectionSection = "RFC 9113 §8.2.2";

    private HpackDecoder _hpack;
    private readonly int _maxHeaderSize;
    private readonly int _maxTotalHeaderSize;
    private readonly int _maxHeaderCount;
    private readonly int _headerTableSize;

    public Http2ServerDecoder(Http2ServerDecoderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxHeaderSize = options.MaxHeaderBytes;
        _maxTotalHeaderSize = options.MaxFieldSectionSize;
        _maxHeaderCount = options.MaxHeaderCount;
        _headerTableSize = options.HeaderTableSize;
        _hpack = CreateHpack();
    }

    private HpackDecoder CreateHpack()
    {
        var hpack = new HpackDecoder();
        // RFC 9113 §6.5.2: enforce the cumulative decoded header-list size (MAX_HEADER_LIST_SIZE) inside
        // the HPACK decoder so a decompression bomb is rejected mid-decode, before the full list is built.
        hpack.SetMaxHeaderListSize(_maxTotalHeaderSize);
        // RFC 7541 §4.2: the decoder must enforce the SETTINGS_HEADER_TABLE_SIZE the server advertised,
        // so a Dynamic Table Size Update is accepted up to (and only up to) the configured value.
        hpack.SetMaxAllowedTableSize(_headerTableSize);
        return hpack;
    }

    public void ResetHpack()
    {
        _hpack = CreateHpack();
    }

    public TurboHttpRequestFeature? DecodeHeadersToFeature(int streamId, bool endStream, StreamState state)
    {
        var feature = new TurboHttpRequestFeature();
        PopulateRequestFeature(streamId, state, feature);

        if (!endStream)
        {
            return null;
        }

        return feature;
    }

    public void PopulateRequestFeature(int streamId, StreamState state, TurboHttpRequestFeature feature)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        ValidateHeaderSize(headers, streamId);
        ValidateRequestHeaders(headers);

        feature.Protocol = WellKnownHeaders.Http20;
        var headerDict = feature.Headers;

        string? path = null;
        string? scheme = null;
        var isConnect = false;

        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Method)
            {
                feature.Method = h.Value;
                if (h.Value == WellKnownHeaders.Connect)
                {
                    isConnect = true;
                }
            }
            else if (h.Name == WellKnownHeaders.Path)
            {
                path = h.Value;
                state.AddPseudoHeader(WellKnownHeaders.Path, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Scheme)
            {
                scheme = h.Value;
                state.AddPseudoHeader(WellKnownHeaders.Scheme, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Authority)
            {
                state.AddPseudoHeader(WellKnownHeaders.Authority, h.Value);
            }
            else if (!h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                if (headerDict.TryGetValue(h.Name, out var existing))
                {
                    headerDict[h.Name] = Microsoft.Extensions.Primitives.StringValues.Concat(existing, h.Value);
                }
                else
                {
                    headerDict[h.Name] = h.Value;
                }

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        if (!isConnect && path is not null)
        {
            feature.Scheme = scheme ?? "https";
            feature.RawTarget = path;

            var queryIdx = path.IndexOf('?');
            feature.Path = queryIdx >= 0 ? path[..queryIdx] : path;
            feature.QueryString = queryIdx >= 0 ? path[queryIdx..] : string.Empty;
        }

        state.InitRequestFeature(feature);
    }

    public List<(string Name, string Value)> DecodeTrailers(StreamState state)
    {
        var headers = _hpack.Decode(state.GetHeaderSpan());
        var trailers = new List<(string Name, string Value)>();

        foreach (var h in headers)
        {
            if (h.Name.StartsWith(WellKnownHeaders.Colon))
            {
                throw new HttpProtocolException(
                    "RFC 9113 §8.1: Pseudo-headers are not allowed in trailers.");
            }

            if (TrailerFieldValidator.IsAllowedInTrailer(h.Name))
            {
                trailers.Add((h.Name, h.Value));
            }
        }

        return trailers;
    }

    private static void ValidateRequestHeaders(List<HpackHeader> headers)
    {
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
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
        if (headers.Count > _maxHeaderCount)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §10.5.1: Header count {headers.Count} exceeds limit ({_maxHeaderCount}) on stream {streamId}.");
        }

        // Cumulative header-list size is enforced inside the HPACK decoder (MAX_HEADER_LIST_SIZE); here we
        // only bound the size of any single header field (RFC 9113 §10.5.1).
        for (var i = 0; i < headers.Count; i++)
        {
            var headerSize = headers[i].Name.Length + headers[i].Value.Length;

            if (headerSize > _maxHeaderSize)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §10.5.1: Single header field size {headerSize} bytes " +
                    $"exceeds MaxHeaderSize limit ({_maxHeaderSize} bytes) " +
                    $"on stream {streamId} - header '{headers[i].Name}'.");
            }
        }
    }
}
