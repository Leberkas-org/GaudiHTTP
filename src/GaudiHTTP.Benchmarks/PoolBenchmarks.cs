using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using GaudiHTTP.Benchmarks.Internal;

namespace GaudiHTTP.Benchmarks;

/// <summary>
/// Compares the legacy O(n) array-scan object pool against the modern O(1) fast-slot +
/// ConcurrentQueue pool (the design Microsoft.Extensions.ObjectPool.DefaultObjectPool uses), under
/// the concurrent rent/return pattern that the transport wrapper pools see. Allocation is measured
/// process-wide via <see cref="MicroBenchmarkConfig"/> (EventPipe) because the rent/return runs on
/// background Task threads the calling-thread MemoryDiagnoser would miss. Replaces <c>--pool-bench</c>.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class PoolBenchmarks
{
    public enum PoolKind
    {
        LegacyArrayScan,
        ModernConcurrentQueue,
    }

    private const int Producers = 32;
    private const int InFlightPerProducer = 16;
    private const int IterationsPerProducer = 2_000;

    [Params(64, 256, 1024)]
    public int Capacity { get; set; }

    [Params(PoolKind.LegacyArrayScan, PoolKind.ModernConcurrentQueue)]
    public PoolKind Kind { get; set; }

    private Func<Wrapper> _rent = null!;
    private Action<Wrapper> _return = null!;

    [GlobalSetup]
    public void Setup()
    {
        switch (Kind)
        {
            case PoolKind.LegacyArrayScan:
                var legacy = new LegacyArrayPool<Wrapper>(Capacity);
                for (var i = 0; i < Capacity; i++)
                {
                    legacy.Return(new Wrapper());
                }

                _rent = () => legacy.TryRent(out var w) ? w : new Wrapper();
                _return = legacy.Return;
                break;

            default:
                var modern = new ModernQueuePool<Wrapper>(Capacity);
                for (var i = 0; i < Capacity; i++)
                {
                    modern.Return(new Wrapper());
                }

                _rent = () => modern.TryRent(out var w) ? w : new Wrapper();
                _return = modern.Return;
                break;
        }
    }

    [Benchmark]
    public void ConcurrentRentReturn()
    {
        var tasks = new Task[Producers];
        for (var p = 0; p < Producers; p++)
        {
            tasks[p] = Task.Run(() =>
            {
                var held = new Wrapper[InFlightPerProducer];
                for (var it = 0; it < IterationsPerProducer; it++)
                {
                    for (var k = 0; k < InFlightPerProducer; k++)
                    {
                        held[k] = _rent();
                    }

                    for (var k = 0; k < InFlightPerProducer; k++)
                    {
                        _return(held[k]);
                    }
                }
            });
        }

        Task.WaitAll(tasks);
    }

    private sealed class Wrapper;

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

            item = null!;
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

            item = null!;
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
}
