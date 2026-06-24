namespace GaudiHTTP.Benchmarks.Internal;

public abstract class TurboServerBaseClass : BenchmarkSuiteBase
{
    private static TurboBenchmarkServer? _sharedServer;
    private static readonly SemaphoreSlim ServerLock = new(1, 1);
    private static int _serverRefCount;

    protected static readonly byte[] HeavyPayload = GeneratePayload(1 * 1024 * 1024);

    protected int GaudiHttp11Port { get; private set; }
    protected int GaudiHttp20Port { get; private set; }
    protected int GaudiHttp30Port { get; private set; }

    protected int TurboPort => HttpVersion switch
    {
        "3.0" => GaudiHttp30Port,
        "2.0" => GaudiHttp20Port,
        _ => GaudiHttp11Port,
    };

    private string Scheme => HttpVersion == "3.0" ? "https" : "http";

    public Uri PlaintextUri => new(string.Concat(Scheme, "://127.0.0.1:", TurboPort.ToString(), "/plaintext"));
    public Uri JsonUri => new(string.Concat(Scheme, "://127.0.0.1:", TurboPort.ToString(), "/json"));
    public Uri FortunesUri => new(string.Concat(Scheme, "://127.0.0.1:", TurboPort.ToString(), "/fortunes"));
    public Uri UploadUri => new(string.Concat(Scheme, "://127.0.0.1:", TurboPort.ToString(), "/upload"));
    public Uri BaseAddress => new(string.Concat(Scheme, "://127.0.0.1:", TurboPort.ToString()));

    public static byte[] GeneratePayload(int sizeBytes)
    {
        var payload = new byte[sizeBytes];
        for (var i = 0; i < sizeBytes; i++)
        {
            payload[i] = (byte)(i % 256);
        }
        return payload;
    }

    public override async Task GlobalSetup()
    {
        await base.GlobalSetup();

        await ServerLock.WaitAsync();
        try
        {
            if (_sharedServer is null)
            {
                _sharedServer = new TurboBenchmarkServer();
                await _sharedServer.InitializeAsync();
            }

            _serverRefCount++;
            GaudiHttp11Port = _sharedServer.Http11Port;
            GaudiHttp20Port = _sharedServer.Http20Port;
            GaudiHttp30Port = _sharedServer.Http30Port;
        }
        finally
        {
            ServerLock.Release();
        }
    }

    public override async Task GlobalCleanup()
    {
        await ServerLock.WaitAsync();
        try
        {
            _serverRefCount--;
            if (_serverRefCount == 0 && _sharedServer is not null)
            {
                await _sharedServer.DisposeAsync();
                _sharedServer = null;
            }
        }
        finally
        {
            ServerLock.Release();
        }
    }
}
