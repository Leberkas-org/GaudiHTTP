using System.Text;
using Servus.Diagnostics;

namespace GaudiHTTP.Tests.Shared;

/// <summary>
/// Lock-free ring buffer over Senf trace events for post-mortem stall diagnosis: cheap enough to
/// leave on during a full suite run, dumped only when a spec's watchdog/timeout fires. Events are
/// formatted eagerly (the <see cref="TraceEvent"/> is a transient by-ref struct) with a UTC
/// timestamp so interleaved parallel-test traffic can be correlated by time, source hash and
/// stream id.
/// </summary>
public sealed class RingBufferTraceListener : IServusTraceListener
{
    private readonly string?[] _entries;
    private readonly Func<string, bool> _categoryFilter;
    private long _next;

    public RingBufferTraceListener(int capacity, Func<string, bool>? categoryFilter = null)
    {
        _entries = new string?[capacity];
        _categoryFilter = categoryFilter ?? (_ => true);
    }

    public bool IsEnabled(TraceLevel level, string category) => _categoryFilter(category);

    public void Write(in TraceEvent evt)
    {
        var entry = string.Format(
            "{0:HH:mm:ss.ffffff} [{1}][{2}] {3}#{4:X8}: {5}",
            DateTime.UtcNow, evt.Level, evt.Category, evt.SourceType, evt.SourceHash, evt.FormatMessage());
        var index = Interlocked.Increment(ref _next) - 1;
        _entries[index % _entries.Length] = entry;

        // Pool-corruption diagnostics (double-dispose/double-return stacks) must survive even when
        // no spec dumps the ring: echo them immediately.
        if (evt.Level >= TraceLevel.Warning && evt.Category == "Transport")
        {
            Console.Error.WriteLine(entry);
        }
    }

    /// <summary>Snapshot of the buffered events in arrival order (oldest first).</summary>
    public string Dump()
    {
        var next = Interlocked.Read(ref _next);
        var count = Math.Min(next, _entries.Length);
        var start = next - count;
        var sb = new StringBuilder();
        for (var i = start; i < next; i++)
        {
            var entry = _entries[i % _entries.Length];
            if (entry is not null)
            {
                sb.AppendLine(entry);
            }
        }

        return sb.ToString();
    }
}
