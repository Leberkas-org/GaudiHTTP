using Microsoft.AspNetCore.Http;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiInformationalResponseFeature
{
    private readonly Action<int, IHeaderDictionary> _sendCallback;
    private bool _finalResponseSent;

    public GaudiInformationalResponseFeature(Action<int, IHeaderDictionary> sendCallback)
    {
        _sendCallback = sendCallback;
    }

    public void MarkFinalResponseSent() => _finalResponseSent = true;

    public void SendInformational(int statusCode, IHeaderDictionary headers)
    {
        if (statusCode is < 100 or >= 200)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode,
                "Informational status code must be between 100 and 199.");
        }

        if (_finalResponseSent)
        {
            throw new InvalidOperationException(
                "Cannot send informational response after final response headers have been sent.");
        }

        _sendCallback(statusCode, headers);
    }

    internal void Reset()
    {
        _finalResponseSent = false;
    }
}
