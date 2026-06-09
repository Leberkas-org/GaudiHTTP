using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Server;

internal static class FeatureCollectionFactory
{
    [ThreadStatic] private static Stack<TurboFeatureCollection>? _tPool;
    [ThreadStatic] private static Stack<ArrayBufferWriter<byte>>? _bufferPool;

    private const int MaxPoolSize = 32;

    public static IFeatureCollection Create(
        TurboHttpRequestFeature requestFeature,
        bool hasBody,
        IServiceProvider? services = null,
        IHttpConnectionFeature? connectionFeature = null,
        TlsHandshakeFeature? tlsFeature = null,
        long? maxRequestBodySize = null)
    {
        var features = (_tPool?.Count ?? 0) > 0 ? _tPool!.Pop() : new TurboFeatureCollection();
        var recycled = features.Get<IHttpResponseFeature>() is not null;

        features.Set<IHttpRequestFeature>(requestFeature);

        TurboHttpResponseFeature responseFeature;
        if (recycled && features.Get<IHttpResponseFeature>() is TurboHttpResponseFeature existingResponse)
        {
            existingResponse.Reset();
            responseFeature = existingResponse;
        }
        else
        {
            responseFeature = new TurboHttpResponseFeature();
            features.Set<IHttpResponseFeature>(responseFeature);
        }

        if (recycled && features.Get<IHttpRequestBodyDetectionFeature>() is TurboHttpRequestBodyDetectionFeature existingDetection)
        {
            existingDetection.Reset(hasBody);
        }
        else
        {
            features.Set<IHttpRequestBodyDetectionFeature>(new TurboHttpRequestBodyDetectionFeature(hasBody));
        }

        TurboHttpResponseBodyFeature responseBodyFeature;
        if (recycled && features.Get<IHttpResponseBodyFeature>() is TurboHttpResponseBodyFeature existingBody)
        {
            existingBody.Reset();
            responseBodyFeature = existingBody;
        }
        else
        {
            responseBodyFeature = new TurboHttpResponseBodyFeature();
            features.Set<IHttpResponseBodyFeature>(responseBodyFeature);
        }

        responseBodyFeature.SetResponseFeature(responseFeature);

        if (recycled && features.Get<IHttpResponseTrailersFeature>() is TurboHttpResponseTrailersFeature existingTrailers)
        {
            existingTrailers.Reset();
        }
        else
        {
            features.Set<IHttpResponseTrailersFeature>(new TurboHttpResponseTrailersFeature());
        }

        if (connectionFeature is not null)
        {
            features.Set(connectionFeature);
        }

        if (tlsFeature is not null)
        {
            features.Set<ITlsHandshakeFeature>(tlsFeature);
        }

        if (recycled && features.Get<IHttpRequestLifetimeFeature>() is TurboHttpRequestLifetimeFeature existingLifetime)
        {
            existingLifetime.Reset();
        }
        else
        {
            features.Set<IHttpRequestLifetimeFeature>(new TurboHttpRequestLifetimeFeature());
        }

        if (recycled && features.Get<IHttpRequestIdentifierFeature>() is TurboHttpRequestIdentifierFeature existingIdentifier)
        {
            existingIdentifier.Reset();
        }
        else
        {
            features.Set<IHttpRequestIdentifierFeature>(new TurboHttpRequestIdentifierFeature());
        }

        if (recycled && features.Get<IHttpMaxRequestBodySizeFeature>() is TurboHttpMaxRequestBodySizeFeature existingMaxBody)
        {
            existingMaxBody.Reset(maxRequestBodySize);
        }
        else
        {
            features.Set<IHttpMaxRequestBodySizeFeature>(new TurboHttpMaxRequestBodySizeFeature { MaxRequestBodySize = maxRequestBodySize });
        }

        if (recycled && features.Get<IHttpBodyControlFeature>() is TurboHttpBodyControlFeature existingBodyControl)
        {
            existingBodyControl.Reset();
        }
        else
        {
            features.Set<IHttpBodyControlFeature>(new TurboHttpBodyControlFeature());
        }

        return features;
    }

    internal static void Return(IFeatureCollection features)
    {
        if (features is not TurboFeatureCollection turboFeatures)
        {
            return;
        }

        turboFeatures.RequestTimestamp = 0;
        turboFeatures.RequestActivity = null;

        if (features.Get<IHttpRequestLifetimeFeature>() is TurboHttpRequestLifetimeFeature lifetime)
        {
            lifetime.Reset();
        }

        _tPool ??= new Stack<TurboFeatureCollection>(MaxPoolSize);

        if (_tPool.Count < MaxPoolSize)
        {
            _tPool.Push(turboFeatures);
        }
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
