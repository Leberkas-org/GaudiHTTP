using System.Diagnostics.Tracing;

namespace TurboHTTP.Benchmarks.LoadTest;

// In-process allocation profiler. Subscribes to the runtime's GCAllocationTick events (emitted once
// per ~100 KB allocated, carrying the allocated type name) and aggregates bytes-by-type while armed.
// This is a statistical sample, not exact accounting, but it reliably ranks the dominant allocators
// on the request hot path — which is what we need to target, since aggregate alloc/req is too coarse
// and per-run RPS is too noisy on this in-process loopback box.
internal sealed class AllocationProfiler : EventListener
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

    public void Report(long totalRequests, int top = 20)
    {
        List<KeyValuePair<string, (long Bytes, long Hits)>> snapshot;
        lock (_lock)
        {
            snapshot = _byType.OrderByDescending(kv => kv.Value.Bytes).Take(top).ToList();
        }

        Console.WriteLine();
        Console.WriteLine($"Allocation sample by type (top {top}, ~100KB/tick — relative ranking, not exact):");
        Console.WriteLine($"{"Type",-62}{"Sampled MB",14}{"Hits",10}{"B/req est",12}");
        Console.WriteLine(new string('-', 98));
        foreach (var (type, (bytes, hits)) in snapshot)
        {
            var shortType = type.Length > 60 ? "…" + type[^59..] : type;
            var perReq = totalRequests == 0 ? 0 : (double)bytes / totalRequests;
            Console.WriteLine($"{shortType,-62}{bytes / (1024.0 * 1024.0),14:N1}{hits,10:N0}{perReq,12:N1}");
        }
    }
}
