using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class HttpsDefaultsSpec
{
    [Fact(Timeout = 5000)]
    public void ConfigureHttpsDefaults_should_store_callback()
    {
        var options = new TurboServerOptions();

        options.ConfigureHttpsDefaults(https =>
        {
            https.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });

        Assert.NotNull(options.HttpsDefaultsCallback);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureHttpsDefaults_callback_should_apply_to_options()
    {
        var options = new TurboServerOptions();

        options.ConfigureHttpsDefaults(https =>
        {
            https.HandshakeTimeout = TimeSpan.FromSeconds(42);
        });

        var httpsOptions = new TurboHttpsOptions();
        options.HttpsDefaultsCallback!.Invoke(httpsOptions);

        Assert.Equal(TimeSpan.FromSeconds(42), httpsOptions.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureHttpsDefaults_should_be_null_by_default()
    {
        var options = new TurboServerOptions();

        Assert.Null(options.HttpsDefaultsCallback);
    }
}
