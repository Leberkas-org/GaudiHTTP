namespace GaudiHTTP.Server.Context.Features;

internal interface IHttpStreamIdFeature
{
    long StreamId { get; }
}

internal sealed class GaudiStreamIdFeature(long streamId) : IHttpStreamIdFeature
{
    public long StreamId { get; } = streamId;
}
