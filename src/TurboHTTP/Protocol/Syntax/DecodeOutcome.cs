namespace TurboHTTP.Protocol.Syntax;

internal enum DecodeOutcome
{
    NeedMore,
    HeadersReady,
    Complete,
}
