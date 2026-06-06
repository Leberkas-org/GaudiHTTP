using System.Globalization;
using System.Text;
using TurboHTTP.Protocol.LineBased;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Body;

internal sealed class ChunkedFramingDecoder : IFramingDecoder
{
    private enum Phase
    {
        ChunkSize,
        ChunkData,
        ChunkDataCrlf,
        Trailer,
        Complete
    }

    private const int MaxControlLineLength = 64 * 1024;
    private const int MaxTrailerSectionBytes = 32 * 1024;

    private Phase _phase;
    private int _currentChunkRemaining;
    private byte[] _stash = [];
    private int _stashLen;
    private long _totalBodyBytes;
    private long _maxBodySize;
    private int _maxChunkExtensionLength;
    private List<(string Name, string Value)>? _trailers;
    private int _trailerSectionBytes;

    public bool SupportsZeroCopy => false;
    public bool IsComplete => _phase == Phase.Complete;

    public IReadOnlyList<(string Name, string Value)> Trailers
        => _trailers ?? (IReadOnlyList<(string Name, string Value)>)[];

    public void Reset(long maxBodySize, int maxChunkExtensionLength)
    {
        _phase = Phase.ChunkSize;
        _currentChunkRemaining = 0;
        _stashLen = 0;
        _totalBodyBytes = 0;
        _maxBodySize = maxBodySize;
        _maxChunkExtensionLength = maxChunkExtensionLength;
        _trailers?.Clear();
        _trailerSectionBytes = 0;
    }

    public FramingDecodeResult Decode(ReadOnlySpan<byte> raw, out int rawConsumed)
    {
        rawConsumed = 0;
        if (_phase == Phase.Complete)
        {
            return new FramingDecodeResult(default, true);
        }

        ReadOnlySpan<byte> work;
        var stashOffset = _stashLen;
        if (_stashLen > 0)
        {
            EnsureStash(_stashLen + raw.Length);
            raw.CopyTo(_stash.AsSpan(_stashLen));
            work = _stash.AsSpan(0, _stashLen + raw.Length);
        }
        else
        {
            work = raw;
        }

        var pos = 0;
        ReadOnlySpan<byte> bodyOutput = default;
        var hasBody = false;
        var incompleteLine = false;

        while (pos < work.Length)
        {
            if (hasBody && _phase == Phase.ChunkData)
            {
                break;
            }

            switch (_phase)
            {
                case Phase.ChunkSize:
                {
                    var crlf = BufferSearch.FindCrlf(work, pos);
                    if (crlf < 0)
                    {
                        incompleteLine = true;
                        goto stash;
                    }

                    var line = work[pos..crlf];
                    var semi = line.IndexOf((byte)';');
                    if (semi >= 0 && line.Length - semi > _maxChunkExtensionLength)
                    {
                        throw new HttpProtocolException("Chunk extension exceeds configured maximum length.");
                    }

                    var sizeSpan = semi < 0 ? line : line[..semi];
                    if (!ulong.TryParse(Encoding.ASCII.GetString(sizeSpan),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize)
                        || chunkSize > int.MaxValue)
                    {
                        throw new HttpProtocolException("Invalid chunk size.");
                    }

                    _currentChunkRemaining = (int)chunkSize;
                    pos = crlf + 2;
                    _phase = _currentChunkRemaining == 0 ? Phase.Trailer : Phase.ChunkData;
                    break;
                }
                case Phase.ChunkData:
                {
                    var avail = work.Length - pos;
                    var take = Math.Min(_currentChunkRemaining, avail);
                    if (take > 0)
                    {
                        _totalBodyBytes += take;
                        if (_totalBodyBytes > _maxBodySize)
                        {
                            throw new HttpProtocolException(
                                $"Request body size {_totalBodyBytes} exceeds limit {_maxBodySize}.");
                        }

                        bodyOutput = work.Slice(pos, take);
                        hasBody = true;
                        _currentChunkRemaining -= take;
                        pos += take;

                        if (_currentChunkRemaining == 0)
                        {
                            _phase = Phase.ChunkDataCrlf;
                        }

                        break;
                    }

                    incompleteLine = true;
                    goto stash;
                }
                case Phase.ChunkDataCrlf:
                {
                    if (work.Length - pos < 2)
                    {
                        incompleteLine = true;
                        goto stash;
                    }

                    if (work[pos] != (byte)'\r' || work[pos + 1] != (byte)'\n')
                    {
                        throw new HttpProtocolException("Missing CRLF after chunk-data.");
                    }

                    pos += 2;
                    _phase = Phase.ChunkSize;
                    break;
                }
                case Phase.Trailer:
                {
                    var crlf = BufferSearch.FindCrlf(work, pos);
                    if (crlf < 0)
                    {
                        incompleteLine = true;
                        goto stash;
                    }

                    if (crlf == pos)
                    {
                        pos += 2;
                        _phase = Phase.Complete;
                        _stashLen = 0;
                        rawConsumed = pos - stashOffset;
                        if (rawConsumed < 0) rawConsumed = 0;
                        return new FramingDecodeResult(bodyOutput, true);
                    }

                    var trailerLine = work[pos..crlf];
                    _trailerSectionBytes += trailerLine.Length + 2;
                    if (_trailerSectionBytes > MaxTrailerSectionBytes)
                    {
                        throw new HttpProtocolException("Trailer section exceeds maximum size.");
                    }

                    if (HeaderFieldParser.TryParse(trailerLine, out var fieldName, out var fieldValue)
                        && TrailerFieldValidator.IsAllowedInTrailer(fieldName))
                    {
                        _trailers ??= [];
                        _trailers.Add((fieldName, fieldValue));
                    }

                    pos = crlf + 2;
                    break;
                }
            }
        }

        stash:
        var remaining = work.Length - pos;
        if (incompleteLine && _phase is Phase.ChunkSize or Phase.Trailer
                           && remaining > Math.Max(MaxControlLineLength, _maxChunkExtensionLength))
        {
            throw new HttpProtocolException("Chunk control line exceeds maximum length.");
        }

        if (incompleteLine && remaining > 0)
        {
            EnsureStash(remaining);
            work[pos..].CopyTo(_stash);
            _stashLen = remaining;
        }
        else
        {
            _stashLen = 0;
        }

        rawConsumed = Math.Max(0, pos - stashOffset);

        return new FramingDecodeResult(bodyOutput, false);
    }

    private void EnsureStash(int needed)
    {
        if (_stash.Length < needed)
        {
            Array.Resize(ref _stash, Math.Max(needed, _stash.Length * 2 + 16));
        }
    }

    public bool OnEof()
    {
        return _phase == Phase.Complete;
    }

    public int Drain(ReadOnlySpan<byte> raw)
    {
        if (_phase == Phase.Complete)
        {
            return 0;
        }

        Decode(raw, out var consumed);
        return consumed;
    }
}