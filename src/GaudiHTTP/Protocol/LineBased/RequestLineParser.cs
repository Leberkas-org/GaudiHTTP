using System.Diagnostics.CodeAnalysis;
using System.Text;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class RequestLineParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> data,
        int maxLength,
        [NotNullWhen(true)] out HttpMethod? method,
        [NotNullWhen(true)] out string? targetText,
        [NotNullWhen(true)] out Version? version,
        [NotNullWhen(true)] out int? consumed)
    {
        method = null!;
        targetText = null!;
        version = null!;
        consumed = 0;

        var crlf = BufferSearch.FindCrlf(data, 0);
        if (crlf < 0)
        {
            return false;
        }

        if (crlf > maxLength)
        {
            throw new HttpProtocolException("Request-line exceeds maximum length.");
        }

        var line = data[..crlf];
        var firstSpace = BufferSearch.FindSpace(line, 0);
        if (firstSpace <= 0)
        {
            return false;
        }

        var secondSpace = BufferSearch.FindSpace(line, firstSpace + 1);
        if (secondSpace <= firstSpace + 1)
        {
            return false;
        }

        var methodSpan = line[..firstSpace];
        if (!HeaderValidation.IsToken(methodSpan))
        {
            throw new ArgumentException($"Invalid HTTP method token: '{Encoding.ASCII.GetString(methodSpan)}'.");
        }

        method = GetOrCreateHttpMethod(methodSpan);
        targetText = GetOrInternTarget(line[(firstSpace + 1)..secondSpace]);

        var versionSpan = line[(secondSpace + 1)..];
        if (!versionSpan.StartsWith(WellKnownHeaders.Http))
        {
            throw new ArgumentException($"Invalid HTTP version string: '{Encoding.ASCII.GetString(versionSpan)}'.");
        }

        if (!MessageVersionCodec.TryParse(versionSpan, out version))
        {
            return false;
        }

        consumed = crlf + 2;
        return true;
    }

    private static readonly byte[] _slashPlaintext = [(byte)'/', (byte)'p', (byte)'l', (byte)'a', (byte)'i', (byte)'n', (byte)'t', (byte)'e', (byte)'x', (byte)'t'];
    private static readonly byte[] _slashJson = [(byte)'/', (byte)'j', (byte)'s', (byte)'o', (byte)'n'];

    private static string GetOrInternTarget(ReadOnlySpan<byte> span)
    {
        return span.Length switch
        {
            1 when span[0] == (byte)'/' => "/",
            1 when span[0] == (byte)'*' => "*",
            5 when span.SequenceEqual(_slashJson) => "/json",
            10 when span.SequenceEqual(_slashPlaintext) => "/plaintext",
            _ => Encoding.ASCII.GetString(span)
        };
    }

    private static HttpMethod GetOrCreateHttpMethod(ReadOnlySpan<byte> span)
    {
        return span.Length switch
        {
            3 when span.SequenceEqual(WellKnownHeaders.Get) => HttpMethod.Get,
            3 when span.SequenceEqual(WellKnownHeaders.Put) => HttpMethod.Put,
            4 when span.SequenceEqual(WellKnownHeaders.Post) => HttpMethod.Post,
            4 when span.SequenceEqual(WellKnownHeaders.Head) => HttpMethod.Head,
            5 when span.SequenceEqual(WellKnownHeaders.Patch) => HttpMethod.Patch,
            5 when span.SequenceEqual(WellKnownHeaders.Trace) => HttpMethod.Trace,
            6 when span.SequenceEqual(WellKnownHeaders.Delete) => HttpMethod.Delete,
            7 when span.SequenceEqual(WellKnownHeaders.Options) => HttpMethod.Options,
            7 when span.SequenceEqual(WellKnownHeaders.Connect) => HttpMethod.Connect,
            _ => new HttpMethod(Encoding.ASCII.GetString(span))
        };
    }
}