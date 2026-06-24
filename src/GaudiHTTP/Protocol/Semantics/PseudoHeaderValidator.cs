namespace TurboHTTP.Protocol.Semantics;

internal static class PseudoHeaderValidator
{
    [Flags]
    private enum PseudoFlags
    {
        None = 0,
        Method = 1,
        Path = 2,
        Scheme = 4,
        Authority = 8,
        AllRequired = Method | Path | Scheme | Authority
    }

    private static readonly (string Name, PseudoFlags Flag)[] PseudoMapping =
    [
        (WellKnownHeaders.Method, PseudoFlags.Method),
        (WellKnownHeaders.Path, PseudoFlags.Path),
        (WellKnownHeaders.Scheme, PseudoFlags.Scheme),
        (WellKnownHeaders.Authority, PseudoFlags.Authority)
    ];

    internal static void ValidateRequestPseudoHeaders<T>(
        IReadOnlyList<T> headers,
        Func<T, string> getName,
        Func<T, string> getValue,
        string rfcSection)
    {
        var seen = PseudoFlags.None;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;
        string? methodValue = null;
        string? pathValue = null;

        for (var i = 0; i < headers.Count; i++)
        {
            var name = getName(headers[i]);

            if (!name.StartsWith(':'))
            {
                if (firstRegularIndex == int.MaxValue)
                {
                    firstRegularIndex = i;
                }

                continue;
            }

            lastPseudoIndex = i;
            var matched = false;

            for (var j = 0; j < PseudoMapping.Length; j++)
            {
                if (name != PseudoMapping[j].Name)
                {
                    continue;
                }

                var flag = PseudoMapping[j].Flag;
                if ((seen & flag) != 0)
                {
                    throw new HttpProtocolException($"{rfcSection}: Duplicate {name} pseudo-header");
                }

                seen |= flag;
                matched = true;

                if (flag == PseudoFlags.Method)
                {
                    methodValue = getValue(headers[i]);
                }
                else if (flag == PseudoFlags.Path)
                {
                    pathValue = getValue(headers[i]);
                }

                break;
            }

            if (!matched)
            {
                throw new HttpProtocolException($"{rfcSection}: Unknown request pseudo-header '{name}'");
            }
        }

        if (lastPseudoIndex > firstRegularIndex)
        {
            throw new HttpProtocolException(
                $"{rfcSection}: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }

        if (string.Equals(methodValue, WellKnownHeaders.Connect, StringComparison.Ordinal))
        {
            ValidateConnectRequest(seen, rfcSection);
            return;
        }

        var missing = seen ^ PseudoFlags.AllRequired;
        if (missing != PseudoFlags.None)
        {
            throw new HttpProtocolException(
                $"{rfcSection}: Missing required pseudo-headers: {FormatMissing(missing)}");
        }

        if (pathValue is not null && pathValue.Length == 0)
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": :path pseudo-header MUST NOT be empty for non-CONNECT requests"));
        }
    }

    private static void ValidateConnectRequest(PseudoFlags seen, string rfcSection)
    {
        if ((seen & PseudoFlags.Scheme) != 0)
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": CONNECT request MUST NOT include :scheme pseudo-header"));
        }

        if ((seen & PseudoFlags.Path) != 0)
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": CONNECT request MUST NOT include :path pseudo-header"));
        }

        if ((seen & PseudoFlags.Authority) == 0)
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": CONNECT request MUST include :authority pseudo-header"));
        }
    }

    private static string FormatMissing(PseudoFlags missing)
    {
        return missing switch
        {
            PseudoFlags.AllRequired => ":method, :path, :scheme, :authority",
            _ => string.Join(WellKnownHeaders.CommaSpace, EnumerateMissing(missing))
        };

        static IEnumerable<string> EnumerateMissing(PseudoFlags m)
        {
            if ((m & PseudoFlags.Method) != 0)
            {
                yield return WellKnownHeaders.Method;
            }

            if ((m & PseudoFlags.Path) != 0)
            {
                yield return WellKnownHeaders.Path;
            }

            if ((m & PseudoFlags.Scheme) != 0)
            {
                yield return WellKnownHeaders.Scheme;
            }

            if ((m & PseudoFlags.Authority) != 0)
            {
                yield return WellKnownHeaders.Authority;
            }
        }
    }

    internal static void ValidateResponsePseudoHeaders<T>(
        IReadOnlyList<T> headers,
        Func<T, string> getName,
        string rfcSection)
    {
        var hasStatus = false;
        var lastPseudoIndex = -1;
        var firstRegularIndex = int.MaxValue;

        for (var i = 0; i < headers.Count; i++)
        {
            var name = getName(headers[i]);

            if (!name.StartsWith(WellKnownHeaders.Colon))
            {
                if (firstRegularIndex == int.MaxValue)
                {
                    firstRegularIndex = i;
                }

                continue;
            }

            lastPseudoIndex = i;

            if (name == WellKnownHeaders.Status.Name)
            {
                if (hasStatus)
                {
                    throw new HttpProtocolException(
                        string.Concat(rfcSection, ": Duplicate :status pseudo-header"));
                }

                hasStatus = true;
            }
            else
            {
                throw new HttpProtocolException(
                    $"{rfcSection}: Unknown response pseudo-header '{name}'");
            }
        }

        if (lastPseudoIndex > firstRegularIndex)
        {
            throw new HttpProtocolException(
                $"{rfcSection}: Pseudo-header at index {lastPseudoIndex} appears after regular header at index {firstRegularIndex}");
        }

        if (!hasStatus)
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": Missing required :status pseudo-header"));
        }
    }
}