using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Protocol.Syntax.Http2.Server;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.Decoder;

/// <summary>
/// RFC 7541 §4.2 / RFC 9113 §6.5.2: the server's HPACK decoder must enforce the
/// SETTINGS_HEADER_TABLE_SIZE the server advertised, not a hardcoded 4096. Previously
/// <see cref="Http2ServerDecoder"/> never called SetMaxAllowedTableSize, so a Dynamic Table Size
/// Update was wrongly rejected (configured &gt; 4096) or wrongly accepted (configured &lt; 4096).
/// </summary>
public sealed class Http2ServerHpackTableSizeSpec
{
    private static Http2ServerDecoderOptions Options(int headerTableSize) => new()
    {
        HeaderTableSize = headerTableSize,
        MaxConcurrentStreams = 100,
        MaxFieldSectionSize = 64 * 1024,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
    };

    private static readonly HpackEncoder Encoder = new(useHuffman: false);

    private static byte[] EncodeRequest()
    {
        using var owner = MemoryPool<byte>.Shared.Rent(4096);
        var span = owner.Memory.Span;
        var written = Encoder.Encode(new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        }, ref span, useHuffman: false);
        return owner.Memory[..written].ToArray();
    }

    // RFC 7541 §6.3: Dynamic Table Size Update — 001 pattern with a 5-bit prefix integer.
    private static byte[] EncodeDynamicTableSizeUpdate(int size)
    {
        const int prefixMax = 31; // 2^5 - 1
        var bytes = new List<byte>();
        if (size < prefixMax)
        {
            bytes.Add((byte)(0x20 | size));
            return bytes.ToArray();
        }

        bytes.Add((byte)(0x20 | prefixMax));
        var remaining = size - prefixMax;
        while (remaining >= 128)
        {
            bytes.Add((byte)((remaining % 128) | 0x80));
            remaining /= 128;
        }

        bytes.Add((byte)remaining);
        return bytes.ToArray();
    }

    private static StreamState BuildState(byte[] sizeUpdate, byte[] headerBlock)
    {
        var combined = new byte[sizeUpdate.Length + headerBlock.Length];
        sizeUpdate.CopyTo(combined, 0);
        headerBlock.CopyTo(combined, sizeUpdate.Length);

        var state = new StreamState();
        state.AppendHeader(combined);
        return state;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4.2")]
    public void DecodeHeaders_should_accept_table_size_update_up_to_advertised_header_table_size()
    {
        var decoder = new Http2ServerDecoder(Options(16 * 1024));
        // 8192 is above the old hardcoded 4096 but within the advertised 16384.
        var state = BuildState(EncodeDynamicTableSizeUpdate(8192), EncodeRequest());

        var feature = decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state);

        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC7541-4.2")]
    public void DecodeHeaders_should_reject_table_size_update_above_advertised_header_table_size()
    {
        var decoder = new Http2ServerDecoder(Options(2 * 1024));
        // 4000 is within the old hardcoded 4096 but above the advertised 2048 — must be rejected.
        var state = BuildState(EncodeDynamicTableSizeUpdate(4000), EncodeRequest());

        Assert.Throws<HpackException>(() =>
            decoder.DecodeHeadersToFeature(streamId: 1, endStream: true, state));
    }
}
