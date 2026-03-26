using System;
using System.Net.Http;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;
using TurboHttp.Transport;

namespace TurboHttp;

/// <summary>
/// Owns the Akka.Streams pipeline for a <see cref="TurboHttpClient"/>.
/// Materialises the graph once on construction and exposes raw channel endpoints.
/// <para>
/// Lifecycle: completing <see cref="Requests"/> signals the source to finish,
/// which drains the pipeline and completes <see cref="Responses"/>.
/// <see cref="Dispose"/> completes the request channel and disposes the pool.
/// </para>
/// </summary>
internal sealed class TurboClientStreamManager : IDisposable
{
    private readonly ConnectionPool _pool;
    internal ChannelWriter<HttpRequestMessage> Requests { get; }
    internal ChannelReader<HttpResponseMessage> Responses { get; }

    /// <summary>
    /// Exposes the response-channel writer so tests can inject synthetic responses
    /// without requiring a live TCP connection.
    /// </summary>
    internal ChannelWriter<HttpResponseMessage> ResponseWriter { get; private set; } = null!;

    public TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system)
        : this(clientOptions, requestOptionsFactory, system, PipelineDescriptor.Empty)
    {
    }

    internal TurboClientStreamManager(TurboClientOptions clientOptions, Func<TurboRequestOptions> requestOptionsFactory,
        ActorSystem system, PipelineDescriptor descriptor)
    {
        var requestsChannel = Channel.CreateUnbounded<HttpRequestMessage>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
        var responsesChannel = Channel.CreateUnbounded<HttpResponseMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });

        Requests = requestsChannel.Writer;
        Responses = responsesChannel.Reader;
        ResponseWriter = responsesChannel.Writer;

        // Create ConnectionPool — manages per-host connections with idle eviction.
        _pool = new ConnectionPool(clientOptions.IdleTimeout);

        // Build the full pipeline flow from Engine using the provided descriptor.
        var engine = new Engine();
        var engineFlow = engine.CreateFlow(_pool, clientOptions, requestOptionsFactory, descriptor);

        // Materialise the graph:
        //   ChannelSource (request channel) → Engine flow → ChannelSink (response channel)
        //
        // No intermediate pump task or Source.Queue — channels drive the stream directly.
        // Completing the request channel writer signals the source to finish,
        // the pipeline drains, and the sink completes the response channel writer.
        var materializerSettings = ActorMaterializerSettings.Create(system)
            .WithInputBuffer(initialSize: 4, maxSize: 16);
        var materializer = system.Materializer(
            settings: materializerSettings,
            namePrefix: $"stream-manager-{Guid.NewGuid()}");

        ChannelSource.FromReader(requestsChannel.Reader)
            .Via(engineFlow)
            .RunWith(ChannelSink.FromWriter(responsesChannel.Writer, isOwner: true), materializer);
    }

    public void Dispose()
    {
        // Complete the request channel → source finishes → pipeline drains
        // → sink completes response channel writer.
        Requests.TryComplete();
        _pool.Dispose();
    }
}
