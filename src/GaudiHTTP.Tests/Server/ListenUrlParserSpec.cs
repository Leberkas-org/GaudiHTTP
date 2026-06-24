using System.Net;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class ListenUrlParserSpec
{
    [Fact(Timeout = 5000)]
    public void Listen_with_http_localhost_url_should_parse_loopback_address()
    {
        var options = new TurboServerOptions();
        options.Listen("http://localhost:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Loopback, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5100, options.ListenOptions[0].Port);
        Assert.False(options.ListenOptions[0].IsHttps);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_https_localhost_url_should_parse_loopback_and_enable_https()
    {
        var options = new TurboServerOptions();
        options.Listen("https://localhost:5101");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Loopback, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5101, options.ListenOptions[0].Port);
        Assert.True(options.ListenOptions[0].IsHttps);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_http_plus_url_should_parse_any_address()
    {
        var options = new TurboServerOptions();
        options.Listen("http://+:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Any, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5100, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_http_star_url_should_parse_any_address()
    {
        var options = new TurboServerOptions();
        options.Listen("http://*:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Any, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5100, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_explicit_0_0_0_0_should_parse_any_address()
    {
        var options = new TurboServerOptions();
        options.Listen("http://0.0.0.0:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Any, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5100, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_invalid_url_should_throw_ArgumentException()
    {
        var options = new TurboServerOptions();

        var ex = Assert.Throws<ArgumentException>(() => options.Listen("not-a-url"));
        Assert.Contains("Invalid endpoint URL", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_unsupported_scheme_should_throw_NotSupportedException()
    {
        var options = new TurboServerOptions();

        var ex = Assert.Throws<NotSupportedException>(() => options.Listen("ftp://localhost:5100"));
        Assert.Contains("Unsupported URL scheme", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_configure_callback_should_apply_configuration()
    {
        var options = new TurboServerOptions();
        options.Listen("http://localhost:5100", listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
        });

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http2, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_configure_callback_should_apply_endpoint_defaults_before_user_callback()
    {
        var options = new TurboServerOptions();
        options.ConfigureEndpointDefaults(listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
        });

        options.Listen("http://localhost:5100", listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
        });

        Assert.Single(options.ListenOptions);
        // User callback should override defaults
        Assert.Equal(HttpProtocols.Http2, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void Listen_without_configure_callback_should_apply_endpoint_defaults()
    {
        var options = new TurboServerOptions();
        options.ConfigureEndpointDefaults(listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
        });

        options.Listen("http://localhost:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http1, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void ListenLocalhost_should_apply_endpoint_defaults()
    {
        var options = new TurboServerOptions();
        options.ConfigureEndpointDefaults(listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
        });

        options.ListenLocalhost(5100);

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http1, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void ListenAnyIP_should_apply_endpoint_defaults()
    {
        var options = new TurboServerOptions();
        options.ConfigureEndpointDefaults(listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
        });

        options.ListenAnyIP(5100);

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http1, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_address_should_apply_endpoint_defaults()
    {
        var options = new TurboServerOptions();
        options.ConfigureEndpointDefaults(listen =>
        {
            listen.Protocols = HttpProtocols.Http1;
        });

        options.Listen(IPAddress.Loopback, 5100);

        Assert.Single(options.ListenOptions);
        Assert.Equal(HttpProtocols.Http1, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_ipv6_should_parse_correctly()
    {
        var options = new TurboServerOptions();
        options.Listen("http://[::1]:5100");

        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.IPv6Loopback, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5100, options.ListenOptions[0].Port);
    }
}
