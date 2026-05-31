namespace TurboHTTP.Protocol.Server;

internal sealed class DataRateState
{
    public long TotalBytes { get; set; }
    public long LastCheckBytes { get; set; }
    public long LastCheckTimestamp { get; set; } = Environment.TickCount64;
    public long GracePeriodStartTimestamp { get; set; } = Environment.TickCount64;
    public bool InGracePeriod { get; set; }

    public void Reset()
    {
        TotalBytes = 0;
        LastCheckBytes = 0;
        LastCheckTimestamp = Environment.TickCount64;
        GracePeriodStartTimestamp = Environment.TickCount64;
        InGracePeriod = false;
    }
}
