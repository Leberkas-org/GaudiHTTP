namespace TurboHTTP.IntegrationTests.Shared;

internal interface ITestBackend : IAsyncDisposable
{
    int HttpPort { get; }
    int HttpsPort { get; }
    int QuicPort { get; }
    bool IsQuicAvailable { get; }
    bool IsHttp10TlsSupported { get; }
    Task StartAsync();
}
