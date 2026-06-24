namespace TurboHTTP.Pooling;

/// <summary>Implemented by objects that are reset to a pristine state before pool reuse.</summary>
internal interface IResettable
{
    void Reset();
}
