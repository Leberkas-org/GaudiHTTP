namespace TurboHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Pure decision function for HTTP/2 adaptive receive-window growth.
/// Mirrors SocketsHttpHandler's BDP heuristic: grow when the connection's measured
/// bandwidth-delay product exceeds the current window scaled by a multiplier.
/// Holds no window state — the caller owns the window.
/// </summary>
internal sealed class WindowScaler(int maxWindow, double multiplier)
{
    public int MaxWindow => maxWindow;

    /// <summary>
    /// Returns the new window size (>= currentWindow), doubling up to the cap when the link is
    /// keeping the current window saturated. Returns currentWindow unchanged when RTT is unknown,
    /// the sample is degenerate, or growth is not warranted.
    /// </summary>
    public int ComputeNewWindow(int currentWindow, long deliveredBytes, TimeSpan elapsed, TimeSpan minRtt)
    {
        if (currentWindow >= maxWindow || minRtt <= TimeSpan.Zero || elapsed <= TimeSpan.Zero || deliveredBytes <= 0)
        {
            return currentWindow;
        }

        var bdpTerm = (double)deliveredBytes * minRtt.Ticks;
        var windowTerm = (double)currentWindow * elapsed.Ticks * multiplier;

        return bdpTerm > windowTerm ? Math.Min(maxWindow, currentWindow * 2) : currentWindow;
    }
}