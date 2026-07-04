namespace GaudiHTTP.Protocol.Body;

// Unified body read-completion messages, generic over the stream-id type so each protocol keeps
// its natural key with no casts: H1/H2 use int (H1 always 0), H3 uses long (QUIC stream ids).
internal readonly record struct BodyReadComplete<TStreamId>(TStreamId StreamId, int BytesRead);
internal readonly record struct BodyReadFailed<TStreamId>(TStreamId StreamId, Exception Reason);
internal readonly record struct BodyReadContinue<TStreamId>(TStreamId StreamId);
