using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Streams.Lifecycle;

namespace GaudiHTTP.Client;

/// <summary>
/// Default <see cref="IGaudiHttpClient"/> implementation backed by an Akka Streams pipeline.
/// Instances are created by <see cref="IGaudiHttpClientFactory.CreateClient"/> — do not instantiate directly.
/// </summary>
public sealed class GaudiHttpClient : IGaudiHttpClient
{
    private static readonly int MaxPooledCts = Math.Max(Environment.ProcessorCount * 4, 64);

    private readonly HttpRequestMessage _defaultHeadersHolder = new();

    // Lock-free intrusive singly-linked list of in-flight PendingRequests.
    // Nodes are linked via PendingRequest.Next. Head is swapped atomically.
    // Push and remove are O(1) and O(N) respectively; CancelPendingRequests
    // (called at most once, on Dispose) atomically drains the whole list.
    private PendingRequest? _pendingHead;
    private readonly NamedClientConsumerRegistration _consumerRegistration;
    private readonly CancellationTokenSource _disposeCts = new();

    private readonly ObjectPool<CancellationTokenSource> _ctsPool = new(MaxPooledCts);
    private int _disposed;

    private Uri? _baseAddress;
    private Version _defaultRequestVersion;
    private HttpVersionPolicy _defaultVersionPolicy;
    private TimeSpan _timeout;

    private readonly ICredentials? _credentials;
    private readonly bool _preAuthenticate;
    private readonly bool _useProxy;
    private readonly IWebProxy? _proxy;

    /// <inheritdoc />
    public Uri? BaseAddress
    {
        get => _baseAddress;
        set
        {
            _baseAddress = value;
            UpdateCachedOptions();
        }
    }

    /// <inheritdoc />
    public HttpRequestHeaders DefaultRequestHeaders => _defaultHeadersHolder.Headers;

    /// <inheritdoc />
    public Version DefaultRequestVersion
    {
        get => _defaultRequestVersion;
        set
        {
            _defaultRequestVersion = value;
            UpdateCachedOptions();
        }
    }

    /// <inheritdoc />
    public HttpVersionPolicy DefaultVersionPolicy
    {
        get => _defaultVersionPolicy;
        set
        {
            _defaultVersionPolicy = value;
            UpdateCachedOptions();
        }
    }

    /// <inheritdoc />
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            UpdateCachedOptions();
        }
    }

    /// <inheritdoc />
    public ChannelWriter<HttpRequestMessage> Requests { get; }

    /// <inheritdoc />
    public ChannelReader<HttpResponseMessage> Responses { get; }

    internal Guid ConsumerId => _consumerRegistration.ConsumerId;

    internal TurboRequestOptions CachedOptions { get; private set; } = null!;

    private void UpdateCachedOptions()
    {
        CachedOptions = new TurboRequestOptions(
            _baseAddress,
            DefaultRequestHeaders,
            _defaultRequestVersion,
            _defaultVersionPolicy,
            _timeout,
            _credentials,
            _preAuthenticate,
            _useProxy,
            _proxy);
    }

    internal GaudiHttpClient(
        ChannelWriter<HttpRequestMessage> requests,
        ChannelReader<HttpResponseMessage> responses,
        TurboRequestOptions options,
        NamedClientConsumerRegistration consumerRegistration)
    {
        _baseAddress = options.BaseAddress;
        _defaultRequestVersion = options.DefaultRequestVersion;
        _defaultVersionPolicy = options.DefaultVersionPolicy;
        _timeout = options.Timeout;
        _credentials = options.Credentials;
        _preAuthenticate = options.PreAuthenticate;
        _useProxy = options.UseProxy;
        _proxy = options.Proxy;
        foreach (var header in options.DefaultRequestHeaders)
        {
            _defaultHeadersHolder.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        UpdateCachedOptions();
        Requests = requests;
        Responses = responses;
        _consumerRegistration = consumerRegistration;
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);
        request.Options.Set(OptionsKey.ConsumerIdKey, ConsumerId);

        PendingListPush(pending);

        var effectiveTimeout = request.Options.TryGetValue(OptionsKey.TimeoutKey, out var perRequestTimeout)
            ? perRequestTimeout
            : Timeout;

        // Everything that mutates request.Options must happen BEFORE the channel write:
        // once enqueued, the pipeline's RequestEnricher reads and mutates the options
        // dictionary on a stream thread, and HttpRequestOptions is not thread-safe.
        var hasTimeout = effectiveTimeout != System.Threading.Timeout.InfiniteTimeSpan;
        var callerCanCancel = cancellationToken.CanBeCanceled;
        CancellationTokenSource? cts = null;

        if (hasTimeout || callerCanCancel)
        {
            if (hasTimeout && callerCanCancel)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            else if (hasTimeout)
            {
                if (!_ctsPool.TryRent(out cts))
                {
                    cts = new CancellationTokenSource();
                }
            }

            request.SetCancellationToken(cts is not null ? cts.Token : cancellationToken);
        }

        try
        {
            try
            {
                await Requests.WriteAsync(request, cancellationToken);
            }
            catch (ChannelClosedException)
            {
                throw CreateClientDisposedException();
            }

            if (!hasTimeout && !callerCanCancel)
            {
                return await pending.GetValueTask();
            }

            if (!hasTimeout)
            {
                await using (cancellationToken.UnsafeRegister(
                                 static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                                 pending))
                {
                    return await pending.GetValueTask();
                }
            }

            cts!.CancelAfter(effectiveTimeout);
            await using (cts.Token.UnsafeRegister(
                             static (state, ct) => ((PendingRequest)state!).TrySetCanceled(ct),
                             pending))
            {
                return await pending.GetValueTask();
            }
        }
        finally
        {
            if (cts is not null)
            {
                if (callerCanCancel || !cts.TryReset())
                {
                    cts.Dispose();
                }
                else if (!_ctsPool.TryReturn(cts))
                {
                    cts.Dispose();
                }
            }

            PendingListRemove(pending);
            PendingRequest.Return(pending);
        }
    }

    /// <summary>Disposes the client, cancels all pending requests, and releases the consumer registration.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        try
        {
            _consumerRegistration.Dispose();
            CancelPendingRequests();
        }
        finally
        {
            _disposeCts.Dispose();
        }
    }

    /// <inheritdoc />
    public void CancelPendingRequests()
    {
        // Atomically drain the entire list so concurrent SendAsync callers cannot race
        // with the cancellation walk. Any request that completes normally between the
        // swap and TrySetCanceled hits the InvalidOperationException guard inside
        // TrySetCanceled and silently no-ops.
        var node = Interlocked.Exchange(ref _pendingHead, null);
        while (node is not null)
        {
            var next = node.Next;
            node.TrySetCanceled();
            node = next;
        }

        while (Responses.TryRead(out var stale))
        {
            stale.Dispose();
        }
    }

    // Push to the front of the intrusive linked list via a CAS loop (lock-free, O(1)).
    private void PendingListPush(PendingRequest item)
    {
        PendingRequest? head;
        do
        {
            head = Volatile.Read(ref _pendingHead);
            item.Next = head;
        }
        while (Interlocked.CompareExchange(ref _pendingHead, item, head) != head);
    }

    // Remove a specific node from the intrusive linked list (lock-free, O(N)).
    // Called once per request in the finally block of SendAsync.
    private void PendingListRemove(PendingRequest item)
    {
        while (true)
        {
            var head = Volatile.Read(ref _pendingHead);
            if (head is null)
            {
                return;
            }

            if (ReferenceEquals(head, item))
            {
                if (Interlocked.CompareExchange(ref _pendingHead, head.Next, head) == head)
                {
                    return;
                }

                // Head changed concurrently; retry from the top.
                continue;
            }

            // Walk to find the predecessor of item and CAS it to skip over item.
            // Nodes are only removed by their own SendAsync finally block, so once we
            // find a predecessor its Next pointer is stable for our CAS.
            var prev = head;
            var cur = head.Next;
            while (cur is not null)
            {
                if (ReferenceEquals(cur, item))
                {
                    // Best-effort unlink. If prev itself was concurrently removed by
                    // CancelPendingRequests (which swaps the whole list to null), the
                    // item becomes unreachable anyway and is safe: Return clears Next
                    // before the node is pooled.
                    Interlocked.CompareExchange(ref prev.Next!, cur.Next, cur);
                    return;
                }

                prev = cur;
                cur = cur.Next;
            }

            // Item not found — already drained by CancelPendingRequests.
            return;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw CreateClientDisposedException();
        }
    }

    private static ObjectDisposedException CreateClientDisposedException()
    {
        return new ObjectDisposedException(nameof(GaudiHttpClient),
            "Cannot send request because the client has been disposed.");
    }
}