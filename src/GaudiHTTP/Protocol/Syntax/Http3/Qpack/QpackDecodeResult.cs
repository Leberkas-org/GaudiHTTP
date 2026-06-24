namespace GaudiHTTP.Protocol.Syntax.Http3.Qpack;

internal readonly record struct QpackDecodeResult
{
    private QpackDecodeResult(bool isBlocked, int requiredInsertCount, IReadOnlyList<(string Name, string Value)>? headers)
    {
        IsBlocked = isBlocked;
        RequiredInsertCount = requiredInsertCount;
        Headers = headers;
    }

    public bool IsBlocked { get; }
    public int RequiredInsertCount { get; }
    public IReadOnlyList<(string Name, string Value)>? Headers { get; }

    public static QpackDecodeResult Success(IReadOnlyList<(string Name, string Value)> headers)
        => new(false, 0, headers);

    public static QpackDecodeResult Blocked(int requiredInsertCount)
        => new(true, requiredInsertCount, null);
}
