using System.Collections;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace TurboHTTP.Server.Context.Adapters;

internal sealed class TurboRequestHeaderDictionary : IHeaderDictionary
{
    private readonly HttpRequestHeaders _requestHeaders;
    private readonly HttpContentHeaders? _contentHeaders;

    public TurboRequestHeaderDictionary(HttpRequestHeaders requestHeaders, HttpContentHeaders? contentHeaders)
    {
        _requestHeaders = requestHeaders ?? throw new ArgumentNullException(nameof(requestHeaders));
        _contentHeaders = contentHeaders;
    }

    public StringValues this[string key]
    {
        get
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (_requestHeaders.TryGetValues(key, out var requestValues))
            {
                return new StringValues(requestValues.ToArray());
            }

            if (_contentHeaders != null && _contentHeaders.TryGetValues(key, out var contentValues))
            {
                return new StringValues(contentValues.ToArray());
            }

            return StringValues.Empty;
        }
        set
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            _requestHeaders.Remove(key);
            _contentHeaders?.Remove(key);

            if (!StringValues.IsNullOrEmpty(value))
            {
                foreach (var v in value)
                {
                    _requestHeaders.TryAddWithoutValidation(key, v);
                }
            }
        }
    }

    public long? ContentLength
    {
        get => _contentHeaders?.ContentLength;
        set => _contentHeaders?.ContentLength = value;
    }

    public int Count
    {
        get
        {
            var count = _requestHeaders.Count();
            if (_contentHeaders != null)
            {
                count += _contentHeaders.Count();
            }

            return count;
        }
    }

    public bool IsReadOnly => false;

    public ICollection<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(_requestHeaders.Select(h => h.Key), StringComparer.OrdinalIgnoreCase);
            if (_contentHeaders != null)
            {
                foreach (var key in _contentHeaders.Select(h => h.Key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }
    }

    public ICollection<StringValues> Values
    {
        get
        {
            var values = new List<StringValues>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in _requestHeaders)
            {
                values.Add(new StringValues(header.Value.ToArray()));
                seenKeys.Add(header.Key);
            }

            if (_contentHeaders != null)
            {
                foreach (var header in _contentHeaders)
                {
                    if (!seenKeys.Contains(header.Key))
                    {
                        values.Add(new StringValues(header.Value.ToArray()));
                        seenKeys.Add(header.Key);
                    }
                }
            }

            return values;
        }
    }

    public void Add(string key, StringValues value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        foreach (var v in value)
        {
            _requestHeaders.TryAddWithoutValidation(key, v);
        }
    }

    public void Add(KeyValuePair<string, StringValues> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        _requestHeaders.Clear();
        _contentHeaders?.Clear();
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        if (TryGetValue(item.Key, out var value))
        {
            return value.Equals(item.Value);
        }

        return false;
    }

    public bool ContainsKey(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_requestHeaders.TryGetValues(key, out _))
        {
            return true;
        }

        if (_contentHeaders != null && _contentHeaders.TryGetValues(key, out _))
        {
            return true;
        }

        return false;
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (arrayIndex < 0 || arrayIndex > array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        var index = arrayIndex;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in _requestHeaders)
        {
            if (index >= array.Length)
            {
                throw new ArgumentException("Destination array is too small.");
            }

            array[index++] =
                new KeyValuePair<string, StringValues>(header.Key, new StringValues(header.Value.ToArray()));
            seenKeys.Add(header.Key);
        }

        if (_contentHeaders != null)
        {
            foreach (var header in _contentHeaders)
            {
                if (!seenKeys.Contains(header.Key))
                {
                    if (index >= array.Length)
                    {
                        throw new ArgumentException("Destination array is too small.");
                    }

                    array[index++] =
                        new KeyValuePair<string, StringValues>(header.Key, new StringValues(header.Value.ToArray()));
                    seenKeys.Add(header.Key);
                }
            }
        }
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in _requestHeaders)
        {
            yield return new KeyValuePair<string, StringValues>(header.Key, new StringValues(header.Value.ToArray()));
            seenKeys.Add(header.Key);
        }

        if (_contentHeaders != null)
        {
            foreach (var header in _contentHeaders)
            {
                if (!seenKeys.Contains(header.Key))
                {
                    yield return new KeyValuePair<string, StringValues>(header.Key,
                        new StringValues(header.Value.ToArray()));
                    seenKeys.Add(header.Key);
                }
            }
        }
    }

    public bool Remove(string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var removed = _requestHeaders.Remove(key);
        if (_contentHeaders != null)
        {
            removed |= _contentHeaders.Remove(key);
        }

        return removed;
    }

    public bool Remove(KeyValuePair<string, StringValues> item)
    {
        if (TryGetValue(item.Key, out var value) && value.Equals(item.Value))
        {
            return Remove(item.Key);
        }

        return false;
    }

    public bool TryGetValue(string key, out StringValues value)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (_requestHeaders.TryGetValues(key, out var requestValues))
        {
            value = new StringValues(requestValues.ToArray());
            return true;
        }

        if (_contentHeaders != null && _contentHeaders.TryGetValues(key, out var contentValues))
        {
            value = new StringValues(contentValues.ToArray());
            return true;
        }

        value = StringValues.Empty;
        return false;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}