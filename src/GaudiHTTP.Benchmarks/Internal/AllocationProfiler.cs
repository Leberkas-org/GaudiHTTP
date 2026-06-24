using System.Diagnostics.Tracing;
using System.Text;

namespace TurboHTTP.Benchmarks.Internal;

/// <summary>
/// In-process allocation profiler. Subscribes to the runtime's GCAllocationTick events
/// (sampled ~100 KB) and aggregates bytes-by-type while armed.
/// </summary>
public sealed class AllocationProfiler : EventListener, IAllocationProfiler
{
    private const int GCKeyword = 0x1;

    private readonly Dictionary<string, (long Bytes, long Hits)> _byType = new();
    private readonly object _lock = new();
    private volatile bool _armed;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
        {
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)GCKeyword);
        }
    }

    public void Arm() => _armed = true;

    public void Disarm() => _armed = false;

    public void Reset()
    {
        lock (_lock)
        {
            _byType.Clear();
        }
    }

    public string ReportText(int top = 25)
    {
        List<KeyValuePair<string, (long Bytes, long Hits)>> snapshot;
        lock (_lock)
        {
            snapshot = _byType.OrderByDescending(kv => kv.Value.Bytes).Take(top).ToList();
        }

        var sb = new StringBuilder();
        foreach (var (type, (bytes, hits)) in snapshot)
        {
            sb.Append(hits).Append('\t').Append(bytes).Append('\t').Append(type).Append('\n');
        }

        return sb.ToString();
    }

    public void Report(long totalRequests, int top = 20)
    {
        List<KeyValuePair<string, (long Bytes, long Hits)>> snapshot;
        lock (_lock)
        {
            snapshot = _byType.OrderByDescending(kv => kv.Value.Bytes).Take(top).ToList();
        }

        Console.WriteLine();
        Console.WriteLine(string.Concat("Allocation sample by type (top ", top.ToString(), ", GCAllocationTick ~100KB/tick):"));
        Console.WriteLine($"{"Type",-62}{"~B/req",12}{"Hits",10}");
        Console.WriteLine(new string('-', 84));
        foreach (var (type, (bytes, hits)) in snapshot)
        {
            var shortType = type.Length > 60 ? string.Concat("…", type.AsSpan(type.Length - 59)) : type;
            var perReq = totalRequests == 0 ? 0 : (double)bytes / totalRequests;
            Console.WriteLine($"{shortType,-62}{perReq,12:N1}{hits,10:N0}");
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (!_armed || e.EventName is null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal))
        {
            return;
        }

        var typeName = Payload(e, "TypeName") as string;
        if (typeName is null)
        {
            return;
        }

        var amount = Payload(e, "AllocationAmount64") ?? Payload(e, "AllocationAmount");
        long bytes = amount switch
        {
            ulong u => (long)u,
            long l => l,
            uint ui => ui,
            int i => i,
            _ => 0,
        };

        lock (_lock)
        {
            var current = _byType.GetValueOrDefault(typeName);
            _byType[typeName] = (current.Bytes + bytes, current.Hits + 1);
        }
    }

    private static object? Payload(EventWrittenEventArgs e, string name)
    {
        var names = e.PayloadNames;
        if (names is null || e.Payload is null)
        {
            return null;
        }

        for (var i = 0; i < names.Count && i < e.Payload.Count; i++)
        {
            if (names[i] == name)
            {
                return e.Payload[i];
            }
        }

        return null;
    }
}
