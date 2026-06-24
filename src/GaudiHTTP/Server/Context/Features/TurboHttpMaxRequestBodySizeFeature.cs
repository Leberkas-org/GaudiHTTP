using Microsoft.AspNetCore.Http.Features;

namespace GaudiHTTP.Server.Context.Features;

internal sealed class GaudiHttpMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
{
    public bool IsReadOnly { get; set; }
    public long? MaxRequestBodySize { get; set; }

    internal void Reset(long? maxSize)
    {
        IsReadOnly = false;
        MaxRequestBodySize = maxSize;
    }
}
