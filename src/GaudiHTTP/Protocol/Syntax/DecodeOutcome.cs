namespace GaudiHTTP.Protocol.Syntax;

internal enum DecodeOutcome
{
    NeedMore,
    HeadersReady,
    Complete
}
