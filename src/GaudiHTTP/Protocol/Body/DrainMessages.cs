namespace GaudiHTTP.Protocol.Body;

// Unified body read-completion messages, generic over the stream-id type so each protocol keeps
// its natural key with no casts: H1/H2 use int (H1 always 0), H3 uses long (QUIC stream ids).
//
// Generation stamps the pump incarnation the read was STARTED under (bumped on every pump
// Cleanup/reconnect). A completion whose generation no longer matches the pump's current
// generation is a stale event from a torn-down connection: after reconnect the stream-ids reset
// and reuse (H2->1, H3->0), so a stale completion Tell'd before reconnect could otherwise land on
// a NEW request that re-registered the SAME id and mis-advance its body. The pump drops such
// completions (releasing the orphaned slot from the old incarnation) instead of mis-routing them.
// Defaults to 0 so the single-stream SerialBodyPump and non-reconnecting server pumps (whose
// generation never leaves 0) keep constructing these messages unchanged.
internal readonly record struct BodyReadComplete<TStreamId>(TStreamId StreamId, int BytesRead, int Generation = 0);
internal readonly record struct BodyReadFailed<TStreamId>(TStreamId StreamId, Exception Reason, int Generation = 0);
