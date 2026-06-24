using GaudiHTTP.Protocol.Body;

namespace GaudiHTTP.Tests.Protocol.Body;

public sealed class BufferedBodyReaderSpec
{
    [Fact(Timeout = 5000)]
    public void Feed_should_complete_when_all_bytes_received()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(5);

        var consumed = reader.Feed("hello"u8);

        Assert.Equal(5, consumed);
        Assert.True(reader.IsCompleted);
        Assert.True(reader.IsBuffered);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_accumulate_across_multiple_calls()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(5);

        Assert.Equal(2, reader.Feed("he"u8));
        Assert.False(reader.IsCompleted);
        Assert.Equal(3, reader.Feed("llo!extra"u8));
        Assert.True(reader.IsCompleted);
    }

    [Fact(Timeout = 5000)]
    public void GetBody_should_return_accumulated_bytes()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(3);

        reader.Feed("ab"u8);
        reader.Feed("cdef"u8);

        Assert.Equal("abc"u8.ToArray(), reader.GetBody().ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_allow_reuse_for_next_request()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(3);
        reader.Feed("abc"u8);
        Assert.True(reader.IsCompleted);

        reader.Reset(2);
        Assert.False(reader.IsCompleted);
        reader.Feed("xy"u8);
        Assert.True(reader.IsCompleted);
        Assert.Equal("xy"u8.ToArray(), reader.GetBody().ToArray());
    }

    [Fact(Timeout = 5000)]
    public void Zero_length_body_should_complete_immediately()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(0);

        Assert.True(reader.IsCompleted);
        Assert.Equal(0, reader.Feed(ReadOnlySpan<byte>.Empty));
    }

    [Fact(Timeout = 5000)]
    public async Task AsStream_should_return_readable_stream_with_buffered_content()
    {
        using var reader = new BufferedBodyReader();
        reader.Reset(5);
        reader.Feed("hello"u8);

        var stream = reader.AsStream();
        var buffer = new byte[16];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

        Assert.Equal(5, read);
        Assert.Equal("hello"u8.ToArray(), buffer[..5]);
    }

}
