namespace TurboHTTP.Benchmarks.Internal;

public abstract class TurboServerBaseClass : BenchmarkSuiteBase
{
    private static TurboBenchmarkServer? _sharedServer;
    private static readonly SemaphoreSlim _serverLock = new(1, 1);
    private static int _serverRefCount;

    protected static readonly byte[] HeavyPayload = GeneratePayload(1 * 1024 * 1024);

    protected int TurboHttp11Port { get; private set; }
    protected int TurboHttp20Port { get; private set; }
    protected int TurboHttp30Port { get; private set; }

    protected int TurboPort => HttpVersion switch
    {
        "3.0" => TurboHttp30Port,
        "2.0" => TurboHttp20Port,
        _ => TurboHttp11Port,
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

        await _serverLock.WaitAsync();
        try
        {
            if (_sharedServer is null)
            {
                _sharedServer = new TurboBenchmarkServer();
                await _sharedServer.InitializeAsync();
            }

            _serverRefCount++;
            TurboHttp11Port = _sharedServer.Http11Port;
            TurboHttp20Port = _sharedServer.Http20Port;
            TurboHttp30Port = _sharedServer.Http30Port;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    public override async Task GlobalCleanup()
    {
        await _serverLock.WaitAsync();
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
            _serverLock.Release();
        }
    }
}
