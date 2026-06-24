using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using GaudiHTTP.Protocol;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Server.Context;

/// <summary>
/// Marker interface that extends <see cref="IHeaderDictionary"/> to identify header dictionaries
/// managed by GaudiHTTP (e.g. for type-safe retrieval from the feature collection).
/// </summary>
public interface IGaudiHeaderDictionary : IHeaderDictionary;

internal sealed class GaudiHeaderDictionary : IGaudiHeaderDictionary
{
    private readonly Dictionary<string, StringValues> _headers =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _isReadOnly;

    private void ThrowIfReadOnly()
    {
        if (_isReadOnly)
        {
            throw new InvalidOperationException("The header dictionary is read-only.");
        }
    }

    public void SetReadOnly()
    {
        _isReadOnly = true;
    }

    public StringValues this[string key]
    {
        get => _headers.TryGetValue(key, out var value) ? value : StringValues.Empty;
        set
        {
            ThrowIfReadOnly();
            if (StringValues.IsNullOrEmpty(value))
            {
                _headers.Remove(key);
            }
            else
            {
                _headers[key] = value;
            }
        }
    }

    public long? ContentLength
    {
        get
        {
            if (_headers.TryGetValue(WellKnownHeaders.ContentLength, out var value)
                && value.Count > 0
                && long.TryParse(value[0], out var length))
            {
                return length;
            }

            return null;
        }
        set
        {
            ThrowIfReadOnly();
            if (value.HasValue)
            {
                _headers[WellKnownHeaders.ContentLength] = ContentLengthCache.GetValue(value.Value);
            }
            else
            {
                _headers.Remove(WellKnownHeaders.ContentLength);
            }
        }
    }

    public int Count => _headers.Count;

    public bool IsReadOnly => _isReadOnly;

    public ICollection<string> Keys => _headers.Keys;

    public ICollection<StringValues> Values => _headers.Values;

    public void Add(string key, StringValues value)
    {
        ThrowIfReadOnly();
        _headers.Add(key, value);
    }

    public void Add(KeyValuePair<string, StringValues> item)
    {
        ThrowIfReadOnly();
        _headers.Add(item.Key, item.Value);
    }

    public void Clear()
    {
        ThrowIfReadOnly();
        _headers.Clear();
    }

    public bool Contains(KeyValuePair<string, StringValues> item)
    {
        return _headers.TryGetValue(item.Key, out var value) && value.Equals(item.Value);
    }

    public bool ContainsKey(string key)
    {
        return _headers.ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<string, StringValues>>)_headers).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        return _headers.GetEnumerator();
    }

    public bool Remove(string key)
    {
        ThrowIfReadOnly();
        return _headers.Remove(key);
    }

    public bool Remove(KeyValuePair<string, StringValues> item)
    {
        ThrowIfReadOnly();
        if (_headers.TryGetValue(item.Key, out var value) && value.Equals(item.Value))
        {
            return _headers.Remove(item.Key);
        }

        return false;
    }

    public bool TryGetValue(string key, out StringValues value)
    {
        return _headers.TryGetValue(key, out value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal void Reset()
    {
        _isReadOnly = false;
        _headers.Clear();
    }
}