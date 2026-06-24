using System.Net;

namespace GaudiHTTP.Protocol.Semantics;

internal static class MessageVersionCodec
{
    public static bool TryParse(ReadOnlySpan<byte> span, out Version version)
    {
        if (span.SequenceEqual(WellKnownHeaders.Http10))
        {
            version = HttpVersion.Version10;
            return true;
        }

        if (span.SequenceEqual(WellKnownHeaders.Http11))
        {
            version = HttpVersion.Version11;
            return true;
        }

        if (span.SequenceEqual(WellKnownHeaders.Http20))
        {
            version = HttpVersion.Version20;
            return true;
        }

        if (span.SequenceEqual(WellKnownHeaders.Http30))
        {
            version = HttpVersion.Version30;
            return true;
        }

        return TryParseWithLeadingZeros(span, out version);
    }

    private static bool TryParseWithLeadingZeros(ReadOnlySpan<byte> span, out Version version)
    {
        version = null!;

        if (!span.StartsWith(WellKnownHeaders.Http))
        {
            return false;
        }

        var afterPrefix = span[WellKnownHeaders.Http.Bytes.Length..];
        var dot = afterPrefix.IndexOf((byte)'.');
        if (dot <= 0 || dot >= afterPrefix.Length - 1)
        {
            return false;
        }

        if (!TryParseDigits(afterPrefix[..dot], out var major) ||
            !TryParseDigits(afterPrefix[(dot + 1)..], out var minor))
        {
            return false;
        }

        version = new Version(major, minor);
        return true;
    }

    private static bool TryParseDigits(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        if (span.IsEmpty)
        {
            return false;
        }

        foreach (var b in span)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = value * 10 + (b - '0');
        }

        return true;
    }

    public static bool TryParse(string text, out Version version)
    {
        if (text.Equals(WellKnownHeaders.Http10))
        {
            version = HttpVersion.Version10;
            return true;
        }

        if (text.Equals(WellKnownHeaders.Http11))
        {
            version = HttpVersion.Version11;
            return true;
        }

        if (text.Equals(WellKnownHeaders.Http20))
        {
            version = HttpVersion.Version20;
            return true;
        }

        if (text.Equals(WellKnownHeaders.Http30))
        {
            version = HttpVersion.Version30;
            return true;
        }

        version = null!;
        return false;
    }

    public static string ToWireFormat(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Equals(HttpVersion.Version10))
        {
            return WellKnownHeaders.Http10;
        }

        if (version.Equals(HttpVersion.Version11))
        {
            return WellKnownHeaders.Http11;
        }

        if (version.Equals(HttpVersion.Version20))
        {
            return WellKnownHeaders.Http20;
        }

        if (version.Equals(HttpVersion.Version30))
        {
            return WellKnownHeaders.Http30;
        }

        throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported HTTP version.");
    }

    public static ReadOnlySpan<byte> ToWireBytes(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Equals(HttpVersion.Version10))
        {
            return "HTTP/1.0"u8;
        }

        if (version.Equals(HttpVersion.Version11))
        {
            return "HTTP/1.1"u8;
        }

        if (version.Equals(HttpVersion.Version20))
        {
            return "HTTP/2"u8;
        }

        if (version.Equals(HttpVersion.Version30))
        {
            return "HTTP/3"u8;
        }

        throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported HTTP version.");
    }
}