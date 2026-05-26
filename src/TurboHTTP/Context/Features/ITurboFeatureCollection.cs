namespace TurboHTTP.Context.Features;

public interface ITurboFeatureCollection
{
    T? Get<T>() where T : class;
    void Set<T>(T? feature) where T : class;
}
