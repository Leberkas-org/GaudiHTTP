using System.Net.Security;
using System.Security.Authentication;
using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client;

public sealed class GaudiClientOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void BaseAddress_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.BaseAddress);
    }

    [Fact(Timeout = 5000)]
    public void BaseAddress_CanBeSet()
    {
        var options = new GaudiClientOptions();
        var uri = new Uri("https://example.com");

        options.BaseAddress = uri;

        Assert.Equal(uri, options.BaseAddress);
    }

    [Fact(Timeout = 5000)]
    public void Http1_DefaultInstanceIsNotNull()
    {
        var options = new GaudiClientOptions();

        Assert.NotNull(options.Http1);
    }

    [Fact(Timeout = 5000)]
    public void Http2_DefaultInstanceIsNotNull()
    {
        var options = new GaudiClientOptions();

        Assert.NotNull(options.Http2);
    }

    [Fact(Timeout = 5000)]
    public void Http3_DefaultInstanceIsNotNull()
    {
        var options = new GaudiClientOptions();

        Assert.NotNull(options.Http3);
    }

    [Fact(Timeout = 5000)]
    public void ConnectTimeout_DefaultIs15Seconds()
    {
        var options = new GaudiClientOptions();

        Assert.Equal(TimeSpan.FromSeconds(15), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConnectTimeout_CanBeSet()
    {
        var options = new GaudiClientOptions();
        var timeout = TimeSpan.FromSeconds(30);

        options.ConnectTimeout = timeout;

        Assert.Equal(timeout, options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void PooledConnectionIdleTimeout_DefaultIs90Seconds()
    {
        var options = new GaudiClientOptions();

        Assert.Equal(TimeSpan.FromSeconds(90), options.PooledConnectionIdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void PooledConnectionIdleTimeout_CanBeSet()
    {
        var options = new GaudiClientOptions();
        var timeout = TimeSpan.FromSeconds(120);

        options.PooledConnectionIdleTimeout = timeout;

        Assert.Equal(timeout, options.PooledConnectionIdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void PooledConnectionLifetime_DefaultIsInfinite()
    {
        var options = new GaudiClientOptions();

        Assert.Equal(Timeout.InfiniteTimeSpan, options.PooledConnectionLifetime);
    }

    [Fact(Timeout = 5000)]
    public void PooledConnectionLifetime_CanBeSet()
    {
        var options = new GaudiClientOptions();
        var lifetime = TimeSpan.FromMinutes(5);

        options.PooledConnectionLifetime = lifetime;

        Assert.Equal(lifetime, options.PooledConnectionLifetime);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentEndpoints_DefaultIs256()
    {
        var options = new GaudiClientOptions();

        Assert.Equal(256u, options.MaxConcurrentEndpoints);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentEndpoints_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            MaxConcurrentEndpoints = 512
        };

        Assert.Equal(512u, options.MaxConcurrentEndpoints);
    }

    [Fact(Timeout = 5000)]
    public void EnabledSslProtocols_DefaultIsNone()
    {
        var options = new GaudiClientOptions();

        Assert.Equal(SslProtocols.None, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void EnabledSslProtocols_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void ClientCertificates_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.ClientCertificates);
    }

    [Fact(Timeout = 5000)]
    public void DangerousAcceptAnyServerCertificate_DefaultIsFalse()
    {
        var options = new GaudiClientOptions();

        Assert.False(options.DangerousAcceptAnyServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void DangerousAcceptAnyServerCertificate_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            DangerousAcceptAnyServerCertificate = true
        };

        Assert.True(options.DangerousAcceptAnyServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void ServerCertificateValidationCallback_DefaultIsNotNull()
    {
        var options = new GaudiClientOptions();

        Assert.NotNull(options.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void ServerCertificateValidationCallback_CanBeSet()
    {
        var options = new GaudiClientOptions();
        RemoteCertificateValidationCallback customCallback = (_, _, _, _) => false;

        options.ServerCertificateValidationCallback = customCallback;

        Assert.Same(customCallback, options.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void SocketSendBufferSize_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void SocketSendBufferSize_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            SocketSendBufferSize = 65536
        };

        Assert.Equal(65536, options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void SocketReceiveBufferSize_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void SocketReceiveBufferSize_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            SocketReceiveBufferSize = 65536
        };

        Assert.Equal(65536, options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void UseProxy_DefaultIsTrue()
    {
        var options = new GaudiClientOptions();

        Assert.True(options.UseProxy);
    }

    [Fact(Timeout = 5000)]
    public void UseProxy_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            UseProxy = false
        };

        Assert.False(options.UseProxy);
    }

    [Fact(Timeout = 5000)]
    public void Proxy_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.Proxy);
    }

    [Fact(Timeout = 5000)]
    public void DefaultProxyCredentials_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.DefaultProxyCredentials);
    }

    [Fact(Timeout = 5000)]
    public void Credentials_DefaultIsNull()
    {
        var options = new GaudiClientOptions();

        Assert.Null(options.Credentials);
    }

    [Fact(Timeout = 5000)]
    public void PreAuthenticate_DefaultIsFalse()
    {
        var options = new GaudiClientOptions();

        Assert.False(options.PreAuthenticate);
    }

    [Fact(Timeout = 5000)]
    public void PreAuthenticate_CanBeSet()
    {
        var options = new GaudiClientOptions
        {
            PreAuthenticate = true
        };

        Assert.True(options.PreAuthenticate);
    }

    [Fact(Timeout = 5000)]
    public void MaxRequestBodyBufferSize_default_should_be_64_KiB()
    {
        var o = new GaudiClientOptions();

#pragma warning disable CS0618 // obsolete/inert but retained for API compatibility
        Assert.Equal(64 * 1024, o.Http2.MaxRequestBodyBufferSize);
        Assert.Equal(64 * 1024, o.Http3.MaxRequestBodyBufferSize);
#pragma warning restore CS0618
    }

    [Fact(Timeout = 5000)]
    public void
        EffectiveServerCertificateValidationCallback_WhenDangerousAcceptAnyServerCertificateFalse_ReturnsCustomCallback()
    {
        var options = new GaudiClientOptions();
        RemoteCertificateValidationCallback customCallback = (_, _, _, _) => false;
        options.ServerCertificateValidationCallback = customCallback;
        options.DangerousAcceptAnyServerCertificate = false;

        var effective = options.EffectiveServerCertificateValidationCallback;

        Assert.NotNull(effective);
        Assert.Same(customCallback, effective);
    }

    [Fact(Timeout = 5000)]
    public void
        EffectiveServerCertificateValidationCallback_WhenDangerousAcceptAnyServerCertificateTrue_ReturnsAlwaysTrue()
    {
        var options = new GaudiClientOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) => false,
            DangerousAcceptAnyServerCertificate = true
        };

        var effective = options.EffectiveServerCertificateValidationCallback;

        Assert.NotNull(effective);
        // The returned callback should always return true regardless of input
        var result = effective.Invoke(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.True(result);
    }

    [Fact(Timeout = 5000)]
    public void
        EffectiveServerCertificateValidationCallback_WhenDangerousAcceptAnyServerCertificateTrue_IgnoresServerCertificateValidationCallback()
    {
        var options = new GaudiClientOptions();
        var callbackCalled = false;
        options.ServerCertificateValidationCallback = (_, _, _, _) =>
        {
            callbackCalled = true;
            return false;
        };
        options.DangerousAcceptAnyServerCertificate = true;

        var effective = options.EffectiveServerCertificateValidationCallback;
        _ = effective?.Invoke(null!, null, null, SslPolicyErrors.None);

        Assert.False(callbackCalled);
    }

    [Fact(Timeout = 5000)]
    public void EffectiveServerCertificateValidationCallback_WithDefaultCallback_ReturnsTrueOnlyForValidCertificates()
    {
        var options = new GaudiClientOptions();
        // Use the default callback

        var effective = options.EffectiveServerCertificateValidationCallback;

        Assert.NotNull(effective);
        // Default callback returns true only when no policy errors
        var resultValid = effective.Invoke(null!, null, null, SslPolicyErrors.None);
        var resultInvalid = effective.Invoke(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch);

        Assert.True(resultValid);
        Assert.False(resultInvalid);
    }

    [Fact(Timeout = 5000)]
    public void EffectiveServerCertificateValidationCallback_TogglingDangerous_SwitchesCallback()
    {
        var options = new GaudiClientOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) => false
        };

        var effectiveWhenSafe = options.EffectiveServerCertificateValidationCallback;
        options.DangerousAcceptAnyServerCertificate = true;
        var effectiveWhenDangerous = options.EffectiveServerCertificateValidationCallback;

        var safeResult = effectiveWhenSafe?.Invoke(null!, null, null, SslPolicyErrors.None);
        var dangerousResult =
            effectiveWhenDangerous?.Invoke(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.False(safeResult);
        Assert.True(dangerousResult);
    }
}