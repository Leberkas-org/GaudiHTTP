using System.Collections;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.Features;

namespace TurboHTTP.Context.Features;

internal sealed class TurboFeatureCollection : ITurboFeatureCollection, IFeatureCollection
{
    private ITurboRequestFeature? _request;
    private ITurboResponseFeature? _response;
    private ITurboConnectionFeature? _connection;
    private ITurboResponseBodyFeature? _responseBody;
    private ITurboRequestBodyFeature? _requestBody;
    private ITurboRequestBodyDetectionFeature? _bodyDetection;
    private ITurboRequestLifetimeFeature? _lifetime;
    private ITurboRequestIdentifierFeature? _identifier;
    private ITurboResponseTrailersFeature? _trailers;
    private ITurboResetFeature? _reset;
    private Dictionary<Type, object>? _extras;
    private int _revision;

    public T? Get<T>() where T : class
    {
        if (typeof(T) == typeof(ITurboRequestFeature) || typeof(T) == typeof(IHttpRequestFeature))
        {
            return Unsafe.As<T>(_request);
        }

        if (typeof(T) == typeof(ITurboResponseFeature) || typeof(T) == typeof(IHttpResponseFeature))
        {
            return Unsafe.As<T>(_response);
        }

        if (typeof(T) == typeof(ITurboConnectionFeature))
        {
            return Unsafe.As<T>(_connection);
        }

        if (typeof(T) == typeof(ITurboResponseBodyFeature) || typeof(T) == typeof(IHttpResponseBodyFeature))
        {
            return Unsafe.As<T>(_responseBody);
        }

        if (typeof(T) == typeof(ITurboRequestBodyFeature))
        {
            return Unsafe.As<T>(_requestBody);
        }

        if (typeof(T) == typeof(ITurboRequestBodyDetectionFeature) ||
            typeof(T) == typeof(IHttpRequestBodyDetectionFeature))
        {
            return Unsafe.As<T>(_bodyDetection);
        }

        if (typeof(T) == typeof(ITurboRequestLifetimeFeature) || typeof(T) == typeof(IHttpRequestLifetimeFeature))
        {
            return Unsafe.As<T>(_lifetime);
        }

        if (typeof(T) == typeof(ITurboRequestIdentifierFeature) || typeof(T) == typeof(IHttpRequestIdentifierFeature))
        {
            return Unsafe.As<T>(_identifier);
        }

        if (typeof(T) == typeof(ITurboResponseTrailersFeature) || typeof(T) == typeof(IHttpResponseTrailersFeature))
        {
            return Unsafe.As<T>(_trailers);
        }

        if (typeof(T) == typeof(ITurboResetFeature))
        {
            return Unsafe.As<T>(_reset);
        }

        return _extras is not null && _extras.TryGetValue(typeof(T), out var val) ? (T)val : null;
    }

    public void Set<T>(T? feature) where T : class
    {
        if (typeof(T) == typeof(ITurboRequestFeature) || typeof(T) == typeof(IHttpRequestFeature))
        {
            _request = Unsafe.As<ITurboRequestFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboResponseFeature) || typeof(T) == typeof(IHttpResponseFeature))
        {
            _response = Unsafe.As<ITurboResponseFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboConnectionFeature))
        {
            _connection = Unsafe.As<ITurboConnectionFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboResponseBodyFeature) || typeof(T) == typeof(IHttpResponseBodyFeature))
        {
            _responseBody = Unsafe.As<ITurboResponseBodyFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboRequestBodyFeature))
        {
            _requestBody = Unsafe.As<ITurboRequestBodyFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboRequestBodyDetectionFeature) ||
            typeof(T) == typeof(IHttpRequestBodyDetectionFeature))
        {
            _bodyDetection = Unsafe.As<ITurboRequestBodyDetectionFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboRequestLifetimeFeature) || typeof(T) == typeof(IHttpRequestLifetimeFeature))
        {
            _lifetime = Unsafe.As<ITurboRequestLifetimeFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboRequestIdentifierFeature) || typeof(T) == typeof(IHttpRequestIdentifierFeature))
        {
            _identifier = Unsafe.As<ITurboRequestIdentifierFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboResponseTrailersFeature) || typeof(T) == typeof(IHttpResponseTrailersFeature))
        {
            _trailers = Unsafe.As<ITurboResponseTrailersFeature>(feature);
            _revision++;
            return;
        }

        if (typeof(T) == typeof(ITurboResetFeature))
        {
            _reset = Unsafe.As<ITurboResetFeature>(feature);
            _revision++;
            return;
        }

        if (feature is null)
        {
            _extras?.Remove(typeof(T));
        }
        else
        {
            _extras ??= new Dictionary<Type, object>();
            _extras[typeof(T)] = feature;
        }

        _revision++;
    }

    bool IFeatureCollection.IsReadOnly => false;
    int IFeatureCollection.Revision => _revision;

    object? IFeatureCollection.this[Type key]
    {
        get => _extras is not null && _extras.TryGetValue(key, out var val) ? val : null;
        set
        {
            if (value is null)
            {
                _extras?.Remove(key);
            }
            else
            {
                _extras ??= new Dictionary<Type, object>();
                _extras[key] = value;
            }

            _revision++;
        }
    }

    TFeature? IFeatureCollection.Get<TFeature>() where TFeature : default
    {
        if (typeof(TFeature).IsValueType)
        {
            return default;
        }

        // Cast to object, then use reflection to call the class-constrained Get<T>
        var result = GetCore(typeof(TFeature));
        return (TFeature?)result;
    }

    void IFeatureCollection.Set<TFeature>(TFeature? instance) where TFeature : default
    {
        if (typeof(TFeature).IsValueType)
        {
            return;
        }

        SetCore(typeof(TFeature), instance);
    }

    private object? GetCore(Type type)
    {
        if (type == typeof(ITurboRequestFeature))
        {
            return _request;
        }

        if (type == typeof(IHttpRequestFeature))
        {
            return _request;
        }

        if (type == typeof(ITurboResponseFeature))
        {
            return _response;
        }

        if (type == typeof(IHttpResponseFeature))
        {
            return _response;
        }

        if (type == typeof(ITurboConnectionFeature))
        {
            return _connection;
        }

        if (type == typeof(ITurboResponseBodyFeature))
        {
            return _responseBody;
        }

        if (type == typeof(IHttpResponseBodyFeature))
        {
            return _responseBody;
        }

        if (type == typeof(ITurboRequestBodyFeature))
        {
            return _requestBody;
        }

        if (type == typeof(ITurboRequestBodyDetectionFeature))
        {
            return _bodyDetection;
        }

        if (type == typeof(IHttpRequestBodyDetectionFeature))
        {
            return _bodyDetection;
        }

        if (type == typeof(ITurboRequestLifetimeFeature))
        {
            return _lifetime;
        }

        if (type == typeof(IHttpRequestLifetimeFeature))
        {
            return _lifetime;
        }

        if (type == typeof(ITurboRequestIdentifierFeature))
        {
            return _identifier;
        }

        if (type == typeof(IHttpRequestIdentifierFeature))
        {
            return _identifier;
        }

        if (type == typeof(ITurboResponseTrailersFeature))
        {
            return _trailers;
        }

        if (type == typeof(IHttpResponseTrailersFeature))
        {
            return _trailers;
        }

        if (type == typeof(ITurboResetFeature))
        {
            return _reset;
        }

        return _extras is not null && _extras.TryGetValue(type, out var val) ? val : null;
    }

    private void SetCore(Type type, object? instance)
    {
        if (type == typeof(ITurboRequestFeature) || type == typeof(IHttpRequestFeature))
        {
            _request = (ITurboRequestFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboResponseFeature) || type == typeof(IHttpResponseFeature))
        {
            _response = (ITurboResponseFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboConnectionFeature))
        {
            _connection = (ITurboConnectionFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboResponseBodyFeature) || type == typeof(IHttpResponseBodyFeature))
        {
            _responseBody = (ITurboResponseBodyFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboRequestBodyFeature))
        {
            _requestBody = (ITurboRequestBodyFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboRequestBodyDetectionFeature) || type == typeof(IHttpRequestBodyDetectionFeature))
        {
            _bodyDetection = (ITurboRequestBodyDetectionFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboRequestLifetimeFeature) || type == typeof(IHttpRequestLifetimeFeature))
        {
            _lifetime = (ITurboRequestLifetimeFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboRequestIdentifierFeature) || type == typeof(IHttpRequestIdentifierFeature))
        {
            _identifier = (ITurboRequestIdentifierFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboResponseTrailersFeature) || type == typeof(IHttpResponseTrailersFeature))
        {
            _trailers = (ITurboResponseTrailersFeature?)instance;
            _revision++;
            return;
        }

        if (type == typeof(ITurboResetFeature))
        {
            _reset = (ITurboResetFeature?)instance;
            _revision++;
            return;
        }

        if (instance is null)
        {
            _extras?.Remove(type);
        }
        else
        {
            _extras ??= new Dictionary<Type, object>();
            _extras[type] = instance;
        }

        _revision++;
    }

    IEnumerator<KeyValuePair<Type, object>> IEnumerable<KeyValuePair<Type, object>>.GetEnumerator()
    {
        if (_request is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboRequestFeature), _request);
        }

        if (_response is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboResponseFeature), _response);
        }

        if (_connection is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboConnectionFeature), _connection);
        }

        if (_responseBody is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboResponseBodyFeature), _responseBody);
        }

        if (_requestBody is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboRequestBodyFeature), _requestBody);
        }

        if (_bodyDetection is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboRequestBodyDetectionFeature), _bodyDetection);
        }

        if (_lifetime is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboRequestLifetimeFeature), _lifetime);
        }

        if (_identifier is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboRequestIdentifierFeature), _identifier);
        }

        if (_trailers is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboResponseTrailersFeature), _trailers);
        }

        if (_reset is not null)
        {
            yield return new KeyValuePair<Type, object>(typeof(ITurboResetFeature), _reset);
        }

        if (_extras is not null)
        {
            foreach (var kv in _extras) yield return kv;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<KeyValuePair<Type, object>>)this).GetEnumerator();
}