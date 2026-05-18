using System.Buffers;
using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Features.Caching;

internal sealed class Cache
{
    private readonly ICacheStore _store;
    private readonly CachePolicy _policy;

    private readonly LinkedList<string> _lruOrder = [];
    private readonly Dictionary<string, LinkedListNode<string>> _lruIndex = new();

    // primaryKey → list of (compoundKey, varyValues) for variant tracking
    private readonly Dictionary<string, List<(string compoundKey, Dictionary<string, string?> varyValues)>>
        _variantIndex = new();

    public Cache(CachePolicy? policy = null)
        : this(new MemoryCacheStore(), policy)
    {
    }

    public Cache(ICacheStore store, CachePolicy? policy = null)
    {
        _store = store;
        _policy = policy ?? CachePolicy.Default;
    }

    public int Count => _lruOrder.Count;

    public ICacheEntry? Get(HttpRequestMessage request)
    {
        var primaryKey = GetPrimaryKey(request);

        if (!_variantIndex.TryGetValue(primaryKey, out var variants))
        {
            return null;
        }

        foreach (var (compoundKey, varyValues) in variants)
        {
            if (!VaryMatchesRequest(varyValues, request))
            {
                continue;
            }

            if (!_store.TryGet(compoundKey, out var storeEntry))
            {
                continue;
            }

            // Promote in LRU
            if (_lruIndex.TryGetValue(compoundKey, out var node))
            {
                _lruOrder.Remove(node);
                _lruOrder.AddFirst(node);
            }

            return new CacheStoreEntryAdapter(storeEntry);
        }

        return null;
    }

    public void Put(
        HttpRequestMessage request,
        HttpResponseMessage response,
        IMemoryOwner<byte> bodyOwner,
        int bodyLength,
        DateTimeOffset requestTime,
        DateTimeOffset responseTime)
    {
        if (!ShouldStore(request, response))
        {
            bodyOwner.Dispose();
            return;
        }

        if (_policy.SharedCache)
        {
            if (response.Headers.TryGetValues("Cache-Control", out var ccVals))
            {
                var cc = CacheControlParser.Parse(string.Join(", ", ccVals));
                if (cc is { Private: true, PrivateFields: null })
                {
                    bodyOwner.Dispose();
                    return;
                }
            }

            StripPrivateFields(response);
        }

        StripConnectionHeaders(response);

        if (bodyLength > _policy.MaxBodyBytes)
        {
            bodyOwner.Dispose();
            return;
        }

        var body = new CacheBody(bodyOwner, bodyLength);

        var storeEntry = BuildStoreEntry(response, body, requestTime, responseTime, request);
        var primaryKey = GetPrimaryKey(request);
        var compoundKey = primaryKey + "|" + GetVaryKey(storeEntry.VaryRequestValues);

        RemoveMatching(primaryKey, storeEntry.VaryRequestValues);

        // LRU eviction
        while (_lruOrder.Count >= _policy.MaxEntries)
        {
            var lastNode = _lruOrder.Last!;
            var lastKey = lastNode.Value;
            _lruOrder.RemoveLast();
            _lruIndex.Remove(lastKey);
            _store.Remove(lastKey);

            RemoveFromVariantIndex(lastKey);
        }

        _store.Set(compoundKey, storeEntry);
        var lruNode = _lruOrder.AddFirst(compoundKey);
        _lruIndex[compoundKey] = lruNode;

        if (!_variantIndex.TryGetValue(primaryKey, out var variants))
        {
            variants = [];
            _variantIndex[primaryKey] = variants;
        }

        variants.Add((compoundKey, new Dictionary<string, string?>(
            storeEntry.VaryRequestValues, StringComparer.OrdinalIgnoreCase)));
    }

    public void Invalidate(Uri uri)
    {
        var primaryKey = NormalizeUri(uri);

        if (!_variantIndex.TryGetValue(primaryKey, out var variants))
        {
            return;
        }

        foreach (var (compoundKey, _) in variants.ToList())
        {
            _store.Remove(compoundKey);

            if (_lruIndex.TryGetValue(compoundKey, out var node))
            {
                _lruOrder.Remove(node);
                _lruIndex.Remove(compoundKey);
            }
        }

        _variantIndex.Remove(primaryKey);
    }

    public static bool IsCacheable(HttpResponseMessage response)
        => StatusCodeSemantics.IsHeuristicallyCacheable(response.StatusCode);

    public static bool ShouldStore(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return false;
        }

        if (!IsCacheable(response))
        {
            return false;
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            return false;
        }

        if (response.Content?.Headers?.ContentRange is not null)
        {
            return false;
        }

        if (request.Headers.TryGetValues("Cache-Control", out var reqCcValues))
        {
            var reqCc = CacheControlParser.Parse(string.Join(", ", reqCcValues));
            if (reqCc?.NoStore == true)
            {
                return false;
            }
        }

        if (response.Headers.TryGetValues("Cache-Control", out var resCcValues))
        {
            var resCc = CacheControlParser.Parse(string.Join(", ", resCcValues));
            if (resCc?.NoStore == true)
            {
                return false;
            }

            if (resCc?.MustUnderstand == true && !IsUnderstoodStatusCode(response))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnderstoodStatusCode(HttpResponseMessage response)
        => IsCacheable(response);

    public void Clear()
    {
        _store.Clear();
        _lruOrder.Clear();
        _lruIndex.Clear();
        _variantIndex.Clear();
    }

    public static (IMemoryOwner<byte> owner, int length) RentBody(ReadOnlySpan<byte> source)
    {
        var owner = MemoryPool<byte>.Shared.Rent(source.Length);
        source.CopyTo(owner.Memory.Span);
        return (owner, source.Length);
    }

    public static async Task<(IMemoryOwner<byte> owner, int length)> RentBodyFromStreamAsync(Stream source,
        int sizeHint = 4096)
    {
        var bufferSize = Math.Max(sizeHint, 256);
        var owner = MemoryPool<byte>.Shared.Rent(bufferSize);
        var written = 0;

        try
        {
            while (true)
            {
                if (written == owner.Memory.Length)
                {
                    var next = MemoryPool<byte>.Shared.Rent(owner.Memory.Length * 2);
                    owner.Memory[..written].CopyTo(next.Memory);
                    owner.Dispose();
                    owner = next;
                }

                var read = await source.ReadAsync(owner.Memory[written..]).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                written += read;
            }

            return (owner, written);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private static CacheStoreEntry BuildStoreEntry(
        HttpResponseMessage response,
        CacheBody body,
        DateTimeOffset requestTime,
        DateTimeOffset responseTime,
        HttpRequestMessage request)
    {
        CacheControl? cc = null;
        if (response.Headers.TryGetValues("Cache-Control", out var ccValues))
        {
            cc = CacheControlParser.Parse(string.Join(", ", ccValues));
        }

        string? etag = null;
        if (response.Headers.ETag is not null)
        {
            etag = response.Headers.ETag.ToString();
        }

        DateTimeOffset? lastModified = null;
        if (response.Content.Headers.LastModified.HasValue)
        {
            lastModified = response.Content.Headers.LastModified;
        }

        DateTimeOffset? expires = null;
        if (response.Content.Headers.Expires.HasValue)
        {
            expires = response.Content.Headers.Expires;
        }

        DateTimeOffset? date = null;
        if (response.Headers.Date.HasValue)
        {
            date = response.Headers.Date;
        }

        int? ageSeconds = null;
        if (response.Headers.Age.HasValue)
        {
            ageSeconds = (int)response.Headers.Age.Value.TotalSeconds;
        }

        var varyNames = new List<string>();
        if (response.Headers.TryGetValues("Vary", out var varyValues))
        {
            foreach (var v in varyValues)
            {
                varyNames.AddRange(v.Split(',').Select(part => part.Trim()));
            }
        }

        var varyRequestValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in varyNames)
        {
            string? reqValue = null;
            if (request.Headers.TryGetValues(name, out var reqHeaderValues))
            {
                reqValue = string.Join(", ", reqHeaderValues);
            }

            varyRequestValues[name] = reqValue;
        }

        return new CacheStoreEntry
        {
            Response = response,
            Body = body,
            RequestTime = requestTime,
            ResponseTime = responseTime,
            ETag = etag,
            LastModified = lastModified,
            Expires = expires,
            Date = date,
            AgeSeconds = ageSeconds,
            CacheControl = cc is not null ? CacheControlStoreEntry.FromCacheControl(cc) : null,
            VaryHeaderNames = varyNames.ToArray(),
            VaryRequestValues = varyRequestValues
        };
    }

    private static bool VaryMatchesRequest(
        IReadOnlyDictionary<string, string?> cachedVaryValues,
        HttpRequestMessage request)
    {
        foreach (var (name, cachedValue) in cachedVaryValues)
        {
            if (name == "*")
            {
                return false;
            }

            string? currentValue = null;
            if (request.Headers.TryGetValues(name, out var vals))
            {
                currentValue = string.Join(", ", vals);
            }

            if (!string.Equals(cachedValue, currentValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void RemoveMatching(string primaryKey, IReadOnlyDictionary<string, string?> varyValues)
    {
        if (!_variantIndex.TryGetValue(primaryKey, out var variants))
        {
            return;
        }

        for (var i = variants.Count - 1; i >= 0; i--)
        {
            var (compoundKey, existingVary) = variants[i];
            var same = true;

            foreach (var kvp in varyValues)
            {
                var existingVal = existingVary.GetValueOrDefault(kvp.Key);
                if (!string.Equals(existingVal, kvp.Value, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                if (_store.TryGet(compoundKey, out var entry))
                {
                    _store.Remove(compoundKey);
                    entry.Dispose();
                }

                if (_lruIndex.TryGetValue(compoundKey, out var node))
                {
                    _lruOrder.Remove(node);
                    _lruIndex.Remove(compoundKey);
                }

                variants.RemoveAt(i);
            }
        }

        if (variants.Count == 0)
        {
            _variantIndex.Remove(primaryKey);
        }
    }

    private void RemoveFromVariantIndex(string compoundKey)
    {
        foreach (var (primaryKey, variants) in _variantIndex)
        {
            for (var i = variants.Count - 1; i >= 0; i--)
            {
                if (variants[i].compoundKey == compoundKey)
                {
                    variants.RemoveAt(i);
                    break;
                }
            }

            if (variants.Count == 0)
            {
                _variantIndex.Remove(primaryKey);
                break;
            }
        }
    }

    private static string GetPrimaryKey(HttpRequestMessage request)
        => NormalizeUri(request.RequestUri!);

    private static string NormalizeUri(Uri uri)
        => uri.GetLeftPart(UriPartial.Query).ToLowerInvariant();

    private static void StripPrivateFields(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Cache-Control", out var ccValues))
        {
            return;
        }

        var cc = CacheControlParser.Parse(string.Join(", ", ccValues));
        if (cc?.PrivateFields is not { Count: > 0 } fields)
        {
            return;
        }

        foreach (var field in fields)
        {
            response.Headers.Remove(field);
            response.Content?.Headers.Remove(field);
        }
    }

    private static void StripConnectionHeaders(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Connection", out var connectionValues))
        {
            foreach (var value in connectionValues)
            {
                foreach (var field in value.Split(','))
                {
                    var trimmed = field.Trim();
                    if (trimmed.Length > 0)
                    {
                        response.Headers.Remove(trimmed);
                        response.Content?.Headers.Remove(trimmed);
                    }
                }
            }
        }

        response.Headers.Remove("Connection");
        response.Headers.Remove("Keep-Alive");
        response.Headers.Remove("Proxy-Authenticate");
        response.Headers.Remove("Proxy-Authorization");
        response.Headers.Remove("TE");
        response.Headers.Remove("Trailer");
        response.Headers.Remove("Transfer-Encoding");
        response.Headers.Remove("Upgrade");
    }

    private static string GetVaryKey(IReadOnlyDictionary<string, string?> varyRequestValues)
    {
        if (varyRequestValues.Count == 0)
        {
            return "";
        }

        var parts = varyRequestValues
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}={kvp.Value}");

        return string.Join("&", parts);
    }

    private sealed class CacheStoreEntryAdapter : ICacheEntry
    {
        private readonly CacheStoreEntry _entry;

        public CacheStoreEntryAdapter(CacheStoreEntry entry)
        {
            _entry = entry;
        }

        public HttpResponseMessage Response => _entry.Response;
        public ReadOnlyMemory<byte> Body => _entry.Body.Memory;
        public DateTimeOffset RequestTime => _entry.RequestTime;
        public DateTimeOffset ResponseTime => _entry.ResponseTime;
        public string? ETag => _entry.ETag;
        public DateTimeOffset? LastModified => _entry.LastModified;
        public DateTimeOffset? Expires => _entry.Expires;
        public DateTimeOffset? Date => _entry.Date;
        public int? AgeSeconds => _entry.AgeSeconds;
        public CacheControl? CacheControl => _entry.CacheControl?.ToCacheControl();
        public IReadOnlyList<string> VaryHeaderNames => _entry.VaryHeaderNames;
        public IReadOnlyDictionary<string, string?> VaryRequestValues => _entry.VaryRequestValues;

        public void Dispose()
        {
        }
    }
}