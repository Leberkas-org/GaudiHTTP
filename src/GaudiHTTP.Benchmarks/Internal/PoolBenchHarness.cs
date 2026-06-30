using System.Collections.Concurrent;
using System.Diagnostics;

namespace GaudiHTTP.Benchmarks.Internal;

/// <summary>
/// Deterministic, isolated comparison of the old O(n) array-scan object pool against the new O(1)
/// fast-slot + ConcurrentQueue pool (the design Microsoft.Extensions.ObjectPool.DefaultObjectPool
/// uses). Exercises only the pool — no Kestrel, no network, no GCAllocationTick sampling — so the
/// numbers are exact: allocations come from GC.GetTotalAllocatedBytes(precise) and time from a
/// Stopwatch. Reproduces the H2/H3 failure mode: many concurrent producers each holding K wrappers
/// in flight against a shared pool whose capacity is below the peak in-flight count.
/// </summary>
internal static class PoolBenchHarness
{
    private sealed class Wrapper { public int Value; }

    // The old implementation, kept verbatim here purely as a measurement baseline.
    private sealed class LegacyArrayPool<T> where T : class
    {
        private T? _fastItem;
        private readonly T?[] _items;
        public LegacyArrayPool(int size) => _items = new T?[size];

        public bool TryRent(out T item)
        {
            var fast = _fastItem;
            if (fast is not null && Interlocked.CompareExchange(ref _fastItem, null, fast) == fast)
            {
                item = fast;
                return true;
            }
            var items = _items;
            for (var i = 0; i < items.Length; i++)
            {
                var current = items[i];
                if (current is not null && Interlocked.CompareExchange(ref items[i], null, current) == current)
                {
                    item = current;
                    return true;
                }
            }
            item = default!;
            return false;
        }

        public void Return(T item)
        {
            if (Interlocked.CompareExchange(ref _fastItem, item, null) is null)
            {
                return;
            }
            var items = _items;
            for (var i = 0; i < items.Length; i++)
            {
                if (Interlocked.CompareExchange(ref items[i], item, null) is null)
                {
                    return;
                }
            }
        }
    }

    private sealed class ModernQueuePool<T> where T : class
    {
        private readonly ConcurrentQueue<T> _items = new();
        private readonly int _maxCapacity;
        private T? _fastItem;
        private int _count;
        public ModernQueuePool(int size) => _maxCapacity = size - 1;

        public bool TryRent(out T item)
        {
            var fast = _fastItem;
            if (fast is not null && Interlocked.CompareExchange(ref _fastItem, null, fast) == fast)
            {
                item = fast;
                return true;
            }
            if (_items.TryDequeue(out var dequeued))
            {
                Interlocked.Decrement(ref _count);
                item = dequeued;
                return true;
            }
            item = default!;
            return false;
        }

        public void Return(T item)
        {
            if (_fastItem is null && Interlocked.CompareExchange(ref _fastItem, item, null) is null)
            {
                return;
            }
            if (Interlocked.Increment(ref _count) <= _maxCapacity)
            {
                _items.Enqueue(item);
                return;
            }
            Interlocked.Decrement(ref _count);
        }
    }

    public static void Run()
    {
        const int producers = 32;          // concurrent connections/streams hammering the shared pool
        const int inFlightPerProducer = 16; // wrappers each holds before returning (framing depth)
        const int iterations = 200_000;     // rent/return cycles per producer

        // Capacities mirror the real config: the legacy transport-wrapper pools were 64/256, far below
        // peak in-flight (producers*inFlightPerProducer = 512); the modernized pools are raised to 1024.
        foreach (var capacity in new[] { 64, 256, 1024 })
        {
            MeasureLegacy(capacity, producers, inFlightPerProducer, iterations);
            MeasureModern(capacity, producers, inFlightPerProducer, iterations);
            Console.WriteLine();
        }
    }

    private static void MeasureLegacy(int capacity, int producers, int inFlight, int iterations)
    {
        var pool = new LegacyArrayPool<Wrapper>(capacity);
        // Pre-warm the pool to capacity.
        var seed = new List<Wrapper>();
        for (var i = 0; i < capacity; i++) seed.Add(new Wrapper());
        foreach (var w in seed) pool.Return(w);

        Measure($"legacy O(n)  cap={capacity,5}", producers, inFlight, iterations,
            rent: () => { if (!pool.TryRent(out var w)) w = new Wrapper(); return w; },
            ret: w => pool.Return(w));
    }

    private static void MeasureModern(int capacity, int producers, int inFlight, int iterations)
    {
        var pool = new ModernQueuePool<Wrapper>(capacity);
        for (var i = 0; i < capacity; i++) pool.Return(new Wrapper());

        Measure($"modern O(1)  cap={capacity,5}", producers, inFlight, iterations,
            rent: () => { if (!pool.TryRent(out var w)) w = new Wrapper(); return w; },
            ret: w => pool.Return(w));
    }

    private static void Measure(string label, int producers, int inFlight, int iterations,
        Func<Wrapper> rent, Action<Wrapper> ret)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();

        var tasks = new Task[producers];
        for (var p = 0; p < producers; p++)
        {
            tasks[p] = Task.Run(() =>
            {
                var held = new Wrapper[inFlight];
                for (var it = 0; it < iterations; it++)
                {
                    for (var k = 0; k < inFlight; k++) held[k] = rent();
                    for (var k = 0; k < inFlight; k++) ret(held[k]);
                }
            });
        }
        Task.WaitAll(tasks);

        sw.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var ops = (long)producers * inFlight * iterations;
        Console.WriteLine($"{label}  |  {sw.ElapsedMilliseconds,6} ms  |  {allocated / (1024.0 * 1024.0),8:N1} MB  |  {allocated / (double)ops,6:N1} B/op");
    }
}
