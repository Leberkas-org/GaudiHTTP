using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerDecoder
{
    private const string PseudoHeaderSection = "RFC 9114 §4.3.1";
    private const string UppercaseSection = "RFC 9114 §4.2";
    private const string TokenSection = "RFC 9114 §10.3";
    private const string FieldValueSection = "RFC 9114 §10.3";
    private const string ConnectionSection = "RFC 9114 §4.2";

    private readonly QpackTableSync _tableSync;
    private readonly int _maxFieldSectionSize;

    public Http3ServerDecoder(QpackTableSync tableSync, int maxFieldSectionSize = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tableSync);
        _tableSync = tableSync;
        _maxFieldSectionSize = maxFieldSectionSize;
    }

    public ReadOnlyMemory<byte> DecoderInstructions => _tableSync.Decoder.DecoderInstructions;

    public bool DecodeHeaders(HeadersFrame frame, StreamState state)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(state);

        var result = _tableSync.TryDecodeOrBlock(frame.HeaderBlock, (int)state.StreamId);

        if (result.IsBlocked)
        {
            return false;
        }

        var headers = result.Headers!;
        ValidateRequestHeaders(headers);
        ValidateFieldSectionSize(headers, state.StreamId);

        var request = new HttpRequestMessage();
        var isConnect = AssembleRequest(headers, request, state);

        if (!isConnect)
        {
            var path = state.GetPseudoHeader(WellKnownHeaders.Path);
            var scheme = state.GetPseudoHeader(WellKnownHeaders.Scheme);
            var authority = state.GetPseudoHeader(WellKnownHeaders.Authority);

            request.RequestUri = new Uri(string.Concat(scheme, "://", authority, path));
        }

        request.Version = new Version(3, 0);

        state.InitRequest(request);

        return true;
    }

    internal static void ValidateRequestHeaders(IReadOnlyList<(string Name, string Value)> headers)
    {
        PseudoHeaderValidator.ValidateRequestPseudoHeaders(
            headers,
            static h => h.Name,
            static h => h.Value,
            PseudoHeaderSection);

        Semantics.FieldValidator.Validate(
            headers,
            static h => h.Name,
            static h => h.Value,
            UppercaseSection,
            TokenSection,
            FieldValueSection,
            ConnectionSection);
    }

    private static bool AssembleRequest(
        IReadOnlyList<(string Name, string Value)> headers,
        HttpRequestMessage request,
        StreamState state)
    {
        var isConnect = false;

        foreach (var h in headers)
        {
            if (h.Name == WellKnownHeaders.Method.Name)
            {
                request.Method = new HttpMethod(h.Value);
                if (h.Value == WellKnownHeaders.Connect)
                {
                    isConnect = true;
                }
            }
            else if (h.Name == WellKnownHeaders.Path)
            {
                state.AddPseudoHeader(WellKnownHeaders.Path, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Scheme)
            {
                state.AddPseudoHeader(WellKnownHeaders.Scheme, h.Value);
            }
            else if (h.Name == WellKnownHeaders.Authority)
            {
                state.AddPseudoHeader(WellKnownHeaders.Authority, h.Value);
            }
            else if (!h.Name.StartsWith(':'))
            {
                request.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (ContentHeaderClassifier.IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }

        return isConnect;
    }

    private void ValidateFieldSectionSize(IReadOnlyList<(string Name, string Value)> headers, long streamId)
    {
        if (_maxFieldSectionSize == int.MaxValue)
        {
            return;
        }

        var totalSize = 0L;
        foreach (var (name, value) in headers)
        {
            totalSize += name.Length + value.Length + 32;
        }

        if (totalSize > _maxFieldSectionSize)
        {
            throw new HttpProtocolException(
                "RFC 9114 §4.2.2: Received field section exceeds SETTINGS_MAX_FIELD_SECTION_SIZE");
        }
    }
}
