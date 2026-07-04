namespace GaudiHTTP.Pooling;

/// <summary>Implemented by objects that are reset to a pristine state before pool reuse and that
/// self-return to the pool on <see cref="IDisposable.Dispose"/>.</summary>
internal interface IResetable : IDisposable
{
    void Reset();
    void OnRented() { }
}
