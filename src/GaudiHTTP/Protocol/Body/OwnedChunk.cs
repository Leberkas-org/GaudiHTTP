namespace TurboHTTP.Protocol.Body;

internal readonly struct OwnedChunk(byte[]? rental, int length)
{
    public byte[]? Rental { get; } = rental;
    public int Length { get; } = length;
    public ReadOnlyMemory<byte> Memory => Rental?.AsMemory(0, Length) ?? default;
}
