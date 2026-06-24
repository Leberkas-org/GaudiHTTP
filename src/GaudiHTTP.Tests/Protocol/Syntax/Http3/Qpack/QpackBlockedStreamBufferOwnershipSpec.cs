using GaudiHTTP.Protocol.Syntax.Http3.Qpack;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Qpack;

/// <summary>
/// A blocked header block must not alias the caller's buffer: the HEADERS frame that
/// carried it owns a pooled rental that is disposed (and reused) after frame handling,
/// so <see cref="QpackTableSync.TryDecodeOrBlock"/> has to take an owned copy.
/// </summary>
public sealed class QpackBlockedStreamBufferOwnershipSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-2.1.2")]
    public void Blocked_header_block_should_survive_caller_buffer_reuse()
    {
        var sync = new QpackTableSync(
            encoderMaxCapacity: 4 * 1024, decoderMaxCapacity: 4 * 1024,
            maxBlockedStreams: 10, configuredEncoderLimit: null);
        var encoder = sync.Encoder;

        var headers = new List<(string, string)> { ("x-owned", "must-survive") };
        var encoded = encoder.Encode(headers);

        // Simulate the pooled HEADERS frame buffer: the header block lives in a buffer
        // the caller reuses after the frame is handled.
        var callerBuffer = new byte[encoded.Length];
        encoded.CopyTo(callerBuffer);

        var result = sync.TryDecodeOrBlock(callerBuffer, streamId: 4);
        Assert.True(result.IsBlocked);

        // Caller disposes the frame; the pool hands the buffer to someone else who scribbles.
        Array.Fill(callerBuffer, (byte)0xFF);

        sync.ProcessEncoderInstructions(encoder.EncoderInstructions.Span);
        var resolved = sync.ResolveBlockedStreams();

        Assert.Single(resolved);
        Assert.Equal(4, resolved[0].StreamId);
        Assert.Single(resolved[0].Headers);
        Assert.Equal("x-owned", resolved[0].Headers[0].Name);
        Assert.Equal("must-survive", resolved[0].Headers[0].Value);
    }
}
