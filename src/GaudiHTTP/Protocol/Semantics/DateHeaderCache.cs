using System.Globalization;
using System.Text;

namespace GaudiHTTP.Protocol.Semantics;

internal static class DateHeaderCache
{
    private sealed class Snapshot(string value, byte[] headerLine)
    {
        public string Value { get; } = value;

        // Pre-encoded "date: <value>\r\n" for direct wire writing (H1.x hot path).
        public byte[] HeaderLine { get; } = headerLine;
    }

    private static Snapshot _snapshot = BuildSnapshot();
    private static long _cachedTicks = Environment.TickCount64;

    public static string GetValue()
    {
        Refresh();
        return Volatile.Read(ref _snapshot).Value;
    }

    /// <summary>
    /// Returns the pre-encoded ASCII bytes for the full Date header line,
    /// including name, colon-space, value, and CRLF (e.g. "date: Thu, 01 Jan 2026 00:00:00 GMT\r\n").
    /// Safe to call from multiple connection threads — the reference is swapped atomically.
    /// </summary>
    public static ReadOnlySpan<byte> GetDateHeaderLine()
    {
        Refresh();
        return Volatile.Read(ref _snapshot).HeaderLine;
    }

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        if (now - _cachedTicks >= 1000)
        {
            _cachedTicks = now;
            Volatile.Write(ref _snapshot, BuildSnapshot());
        }
    }

    private static Snapshot BuildSnapshot()
    {
        var value = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        var headerLine = Encoding.ASCII.GetBytes("Date: " + value + "\r\n");
        return new Snapshot(value, headerLine);
    }
}
