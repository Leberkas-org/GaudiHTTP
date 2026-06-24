using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using GaudiHTTP.Internal;

namespace GaudiHTTP.Streams.Stages.Routing;

/// <summary>
/// Implements Akka's public <see cref="IMergeBack{TIn,TMat}"/> interface so that
/// <see cref="SubFlowImpl{TIn,TOut,TMat,TClosed}"/> can drive our custom
/// host-key grouping/merging stages.
/// </summary>
internal sealed class HostKeyMergeBack<TIn, TMat>(
    IFlow<TIn, TMat> baseFlow,
    Func<TIn, RequestEndpoint> keyFunction,
    uint substreams,
    Func<RequestEndpoint, int>? maxSubstreamsPerKey = null,
    Func<RequestEndpoint, int>? maxConcurrencyPerSlot = null)
    : IMergeBack<TIn, TMat>
{
    // Called by SubFlowImpl.MergeSubstreamsWithParallelism(breadth).
    // `flow` is the accumulated per-substream Flow built up via
    // SubFlowImpl.Via() calls (starts as identity, grows with each operator).
    public IFlow<TOut, TMat> Apply<TOut>(Flow<TIn, TOut, TMat> flow, int breadth)
    {
        var maxSubstreams = Convert.ToInt32(substreams);
        var effectiveBreadth = breadth is <= 0 or int.MaxValue
            ? maxSubstreams
            : breadth;

        return baseFlow
            .Via(new GroupByRequestEndpointStage<TIn>(keyFunction, maxSubstreams, maxSubstreamsPerKey, maxConcurrencyPerSlot))
            .Via(Flow.Create<Source<TIn, NotUsed>>()
                .Select(src => src.Via(flow)))
            .Via(new MergeSubstreamsStage<TOut>(effectiveBreadth));
    }
}