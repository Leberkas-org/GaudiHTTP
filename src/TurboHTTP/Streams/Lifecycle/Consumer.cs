using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class Consumer : ReceiveActor
{
    internal sealed record ConsumerSinkCompleted(Exception? Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer = Context.Materializer();
    private readonly Guid _consumerId;
    private readonly ChannelReader<HttpRequestMessage> _requestReader;
    private readonly Func<TurboRequestOptions> _optionsFactory;
    private readonly ChannelWriter<HttpResponseMessage> _responseEgress;
    private readonly Sink<HttpRequestMessage, NotUsed> _requestIngress;
    private readonly Source<HttpResponseMessage, NotUsed> _responseFanoutSource;

    private UniqueKillSwitch? _sinkKillSwitch;

    // Non-null sentinel for requests dropped by failure isolation in the ingress. Akka.Streams
    // forbids null elements (Reactive Streams rule 2.13), so a failed enrichment returns this
    // marker and is filtered out before the shared MergeHub. Never sent or mutated.
    private static readonly HttpRequestMessage DroppedRequest = new();

    public static Props Props(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        Func<TurboRequestOptions> optionsFactory,
        ChannelWriter<HttpResponseMessage> responseWriter,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource,
        IMaterializer materializer) => Akka.Actor.Props.CreateBy(new ConsumerActorProducer(
            consumerId, requestReader, optionsFactory,
            responseWriter, requestIngress,
            responseFanoutSource));

    private sealed class ConsumerActorProducer(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        Func<TurboRequestOptions> optionsFactory,
        ChannelWriter<HttpResponseMessage> fallbackResponseWriter,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource) : IIndirectActorProducer
    {
        public Type ActorType => typeof(Consumer);

        public ActorBase Produce() => new Consumer(
            consumerId, requestReader,
            fallbackResponseWriter, optionsFactory,
            requestIngress, responseFanoutSource);

        public void Release(ActorBase actor)
        {
        }
    }

    private Consumer(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        ChannelWriter<HttpResponseMessage> responseEgress,
        Func<TurboRequestOptions> optionsFactory,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource)
    {
        _consumerId = consumerId;
        _requestReader = requestReader;
        _optionsFactory = optionsFactory;
        _responseEgress = responseEgress;
        _requestIngress = requestIngress;
        _responseFanoutSource = responseFanoutSource;

        Receive<ConsumerSinkCompleted>(HandleSinkCompleted);
    }

    protected override void PreStart()
    {
        MaterializeIngress();
        MaterializeResponseSink();
    }

    private void MaterializeIngress()
    {
        var enricher = new RequestEnricher(_optionsFactory);
        var cid = _consumerId;

        ChannelSource.FromReader(_requestReader)
            .Select(request => TryEnrich(request, enricher, cid))
            .Where(static request => !ReferenceEquals(request, DroppedRequest))
            .RunWith(_requestIngress, _materializer);
    }

    /// <summary>
    /// Stamps the consumer id and enriches a request, isolating any per-request failure so it can
    /// never fail the SHARED <see cref="Akka.Streams.Dsl.MergeHub"/> ingress. If enrichment throws —
    /// e.g. the caller disposed the <see cref="HttpRequestMessage"/> after cancelling and the pipeline
    /// then dereferenced it (<see cref="HttpRequestMessage.Version"/> set throws ObjectDisposedException) —
    /// a bare Select would propagate the failure into the MergeHub, tear this consumer's producer off
    /// the hub, and strand every other in-flight request on the client. Instead we complete the
    /// offending request's pending with the error (version-guarded, so a pooled/reused pending is never
    /// corrupted) and drop the element from the stream.
    /// </summary>
    private HttpRequestMessage TryEnrich(HttpRequestMessage request, RequestEnricher enricher, Guid cid)
    {
        try
        {
            if (!request.Options.TryGetValue(OptionsKey.ConsumerIdKey, out _))
            {
                request.Options.Set(OptionsKey.ConsumerIdKey, cid);
            }

            return enricher.Enrich(request);
        }
        catch (Exception ex)
        {
            if (request.Options.TryGetValue(OptionsKey.Key, out var pending)
                && request.Options.TryGetValue(OptionsKey.VersionKey, out var version))
            {
                pending.TrySetException(ex, version);
            }

            _log.Debug("Consumer {0} dropped a request whose enrichment failed: {1}", _consumerId, ex.Message);
            return DroppedRequest;
        }
    }

    private void MaterializeResponseSink()
    {
        var fallback = _responseEgress;
        var (killSwitch, completionTask) = _responseFanoutSource
            .ViaMaterialized(KillSwitches.Single<HttpResponseMessage>(), Keep.Right)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(response =>
                {
                    if (response.RequestMessage is { } req
                        && req.Options.TryGetValue(OptionsKey.Key, out var pending)
                        && req.Options.TryGetValue(OptionsKey.VersionKey, out var ver))
                    {
                        if (!pending.TrySetResult(response, ver))
                        {
                            response.Dispose();
                        }

                        return;
                    }

                    fallback.TryWrite(response);
                }),
                Keep.Both)
            .Run(_materializer);

        _sinkKillSwitch = killSwitch;

        completionTask.PipeTo(Self, Self,
            () => new ConsumerSinkCompleted(null),
            ex => new ConsumerSinkCompleted(ex.GetBaseException()));
    }

    private void HandleSinkCompleted(ConsumerSinkCompleted completed)
    {
        _sinkKillSwitch = null;
        if (completed.Error is not null and not OperationCanceledException)
        {
            _log.Warning(completed.Error, "Consumer {0} sink completed with error", _consumerId);
            Context.Stop(Self);
        }
    }

    protected override void PostStop()
    {
        if (_sinkKillSwitch is null) return;
        _sinkKillSwitch.Abort(new OperationCanceledException("Consumer stopped"));
        _sinkKillSwitch = null;
    }
}