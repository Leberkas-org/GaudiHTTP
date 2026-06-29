using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Pooling;
using GaudiHTTP.Server.Context.Features;

namespace GaudiHTTP.Server;

internal static class FeatureCollectionFactory
{
    [ThreadStatic] private static Stack<ArrayBufferWriter<byte>>? _bufferPool;

    private const int MaxPoolSize = 32;

    public static IFeatureCollection Create(
        ConnectionObjectPool pool,
        GaudiHttpRequestFeature requestFeature,
        bool hasBody,
        IHttpConnectionFeature? connectionFeature = null,
        TlsHandshakeFeature? tlsFeature = null,
        long? maxRequestBodySize = null)
    {
        var features = pool.Rent(static () => new GaudiFeatureCollection());
        var recycled = features.Get<IHttpResponseFeature>() is not null;

        if (!recycled || !ReferenceEquals(features.Get<IHttpRequestFeature>(), requestFeature))
        {
            features.Set<IHttpRequestFeature>(requestFeature);
        }

        return CreateCore(features, recycled, hasBody, connectionFeature, tlsFeature, maxRequestBodySize);
    }

    public static IFeatureCollection Create(
        ConnectionObjectPool pool,
        bool hasBody,
        out GaudiHttpRequestFeature requestFeature,
        IHttpConnectionFeature? connectionFeature = null,
        TlsHandshakeFeature? tlsFeature = null,
        long? maxRequestBodySize = null)
    {
        var features = pool.Rent(static () => new GaudiFeatureCollection());
        var recycled = features.Get<IHttpResponseFeature>() is not null;

        if (recycled && features.Get<IHttpRequestFeature>() is GaudiHttpRequestFeature existingRequest)
        {
            existingRequest.Reset();
            requestFeature = existingRequest;
        }
        else
        {
            requestFeature = new GaudiHttpRequestFeature();
            features.Set<IHttpRequestFeature>(requestFeature);
        }

        return CreateCore(features, recycled, hasBody, connectionFeature, tlsFeature, maxRequestBodySize);
    }

    private static IFeatureCollection CreateCore(
        GaudiFeatureCollection features,
        bool recycled,
        bool hasBody,
        IHttpConnectionFeature? connectionFeature,
        TlsHandshakeFeature? tlsFeature,
        long? maxRequestBodySize)
    {
        GaudiHttpResponseFeature responseFeature;
        if (recycled && features.Get<IHttpResponseFeature>() is GaudiHttpResponseFeature existingResponse)
        {
            existingResponse.Reset();
            responseFeature = existingResponse;
        }
        else
        {
            responseFeature = new GaudiHttpResponseFeature();
            features.Set<IHttpResponseFeature>(responseFeature);
        }

        if (recycled && features.Get<IHttpRequestBodyDetectionFeature>() is GaudiHttpRequestBodyDetectionFeature existingDetection)
        {
            existingDetection.Reset(hasBody);
        }
        else
        {
            features.Set<IHttpRequestBodyDetectionFeature>(new GaudiHttpRequestBodyDetectionFeature(hasBody));
        }

        GaudiHttpResponseBodyFeature responseBodyFeature;
        if (recycled && features.Get<IHttpResponseBodyFeature>() is GaudiHttpResponseBodyFeature existingBody)
        {
            existingBody.Reset();
            responseBodyFeature = existingBody;
        }
        else
        {
            responseBodyFeature = new GaudiHttpResponseBodyFeature();
            features.Set<IHttpResponseBodyFeature>(responseBodyFeature);
        }

        responseBodyFeature.SetResponseFeature(responseFeature);

        if (recycled && features.Get<IHttpResponseTrailersFeature>() is GaudiHttpResponseTrailersFeature existingTrailers)
        {
            existingTrailers.Reset();
        }
        else
        {
            features.Set<IHttpResponseTrailersFeature>(new GaudiHttpResponseTrailersFeature());
        }

        if (recycled && features.Get<IHttpRequestTrailersFeature>() is GaudiHttpRequestTrailersFeature existingRequestTrailers)
        {
            existingRequestTrailers.Reset();
        }
        else
        {
            features.Set<IHttpRequestTrailersFeature>(new GaudiHttpRequestTrailersFeature());
        }

        if (connectionFeature is not null)
        {
            features.Set(connectionFeature);
        }

        if (tlsFeature is not null)
        {
            features.Set<ITlsHandshakeFeature>(tlsFeature);
        }

        if (recycled && features.Get<IHttpRequestLifetimeFeature>() is GaudiHttpRequestLifetimeFeature existingLifetime)
        {
            existingLifetime.Reset();
        }
        else
        {
            features.Set<IHttpRequestLifetimeFeature>(new GaudiHttpRequestLifetimeFeature());
        }

        if (recycled && features.Get<IHttpRequestIdentifierFeature>() is GaudiHttpRequestIdentifierFeature existingIdentifier)
        {
            existingIdentifier.Reset();
        }
        else
        {
            features.Set<IHttpRequestIdentifierFeature>(new GaudiHttpRequestIdentifierFeature());
        }

        if (recycled && features.Get<IHttpMaxRequestBodySizeFeature>() is GaudiHttpMaxRequestBodySizeFeature existingMaxBody)
        {
            existingMaxBody.Reset(maxRequestBodySize);
        }
        else
        {
            features.Set<IHttpMaxRequestBodySizeFeature>(new GaudiHttpMaxRequestBodySizeFeature { MaxRequestBodySize = maxRequestBodySize });
        }

        if (recycled && features.Get<IHttpBodyControlFeature>() is GaudiHttpBodyControlFeature existingBodyControl)
        {
            existingBodyControl.Reset();
        }
        else
        {
            features.Set<IHttpBodyControlFeature>(new GaudiHttpBodyControlFeature());
        }

        return features;
    }

    internal static void Return(ConnectionObjectPool pool, IFeatureCollection features)
    {
        if (features is not GaudiFeatureCollection gaudiFeatures)
        {
            return;
        }

        if (features.Get<IHttpRequestLifetimeFeature>() is GaudiHttpRequestLifetimeFeature lifetime)
        {
            lifetime.Reset();
        }

        pool.Return(gaudiFeatures);
    }

    internal static ArrayBufferWriter<byte> RentBuffer()
    {
        if ((_bufferPool?.Count ?? 0) > 0)
        {
            var buf = _bufferPool!.Pop();
            buf.ResetWrittenCount();
            return buf;
        }

        return new ArrayBufferWriter<byte>();
    }

    internal static void ReturnBuffer(ArrayBufferWriter<byte> buffer)
    {
        buffer.ResetWrittenCount();
        _bufferPool ??= new Stack<ArrayBufferWriter<byte>>(MaxPoolSize);
        if (_bufferPool.Count < MaxPoolSize)
        {
            _bufferPool.Push(buffer);
        }
    }
}
