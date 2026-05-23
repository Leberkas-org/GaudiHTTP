using System.Text;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHTTP.Features.Sse;

/// <summary>
/// Exposes the SSE parser as a reusable Flow.
/// </summary>
public static class SseParserFlow
{
    public static Flow<ReadOnlyMemory<byte>, ServerSentEvent, NotUsed> Instance { get; }
        = Flow.FromGraph(new SseParserStage());
}

/// <summary>
/// Stateful GraphStage that transforms raw byte chunks into parsed SSE events.
/// Handles multi-line data, comments, BOM stripping, CRLF/LF/CR line endings, and split chunks.
/// </summary>
internal sealed class SseParserStage : GraphStage<FlowShape<ReadOnlyMemory<byte>, ServerSentEvent>>
{
    private readonly Inlet<ReadOnlyMemory<byte>> _in = new("SseParserStage.in");
    private readonly Outlet<ServerSentEvent> _out = new("SseParserStage.out");

    public override FlowShape<ReadOnlyMemory<byte>, ServerSentEvent> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new SseParserLogic(this);

    private sealed class SseParserLogic : GraphStageLogic
    {
        private readonly SseParserStage _stage;
        private readonly StringBuilder _lineBuffer = new();
        private readonly StringBuilder _dataAccumulator = new();
        private readonly Queue<ServerSentEvent> _pending = new();

        private string? _eventType;
        private string? _id;
        private TimeSpan? _retry;
        private bool _bomChecked;
        private bool _hasData;
        private bool _upstreamFinished;
        private bool _upstreamWaiting;

        public SseParserLogic(SseParserStage stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage._in,
                onPush: () =>
                {
                    _upstreamWaiting = false;
                    var chunk = Grab(stage._in);
                    var bytes = chunk.ToArray();

                    // Strip BOM if at start of stream (check bytes before decoding)
                    int startIndex = 0;
                    if (!_bomChecked)
                    {
                        _bomChecked = true;
                        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        {
                            startIndex = 3;
                        }
                    }

                    var text = Encoding.UTF8.GetString(bytes, startIndex, bytes.Length - startIndex);
                    ProcessText(text);
                    DrainPending(stage);
                },
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;

                    // Process any remaining buffered line as a field
                    if (_lineBuffer.Length > 0)
                    {
                        ProcessField(_lineBuffer.ToString());
                        _lineBuffer.Clear();
                    }

                    // Emit pending event if has data
                    if (_hasData)
                    {
                        var evt = new ServerSentEvent(
                            Data: _dataAccumulator.ToString(),
                            EventType: _eventType,
                            Id: _id,
                            Retry: _retry);
                        _pending.Enqueue(evt);
                    }

                    DrainPending(stage);
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    DrainPending(stage);
                });
        }

        public override void PreStart()
        {
            // Pull the first element from upstream to start the stream
            Pull(_stage._in);
            _upstreamWaiting = true;
        }

        private void DrainPending(SseParserStage stage)
        {
            // Keep pushing while downstream is ready and we have events
            while (IsAvailable(stage._out) && _pending.Count > 0)
            {
                var evt = _pending.Dequeue();
                Push(stage._out, evt);
            }

            // After draining, check if we should pull more or complete
            if (!IsAvailable(stage._out))
            {
                // Downstream is not ready, we'll wait for next pull
                return;
            }

            // Downstream is ready. Check if we should pull more or complete
            if (_upstreamFinished && _pending.Count == 0)
            {
                // Upstream finished and no pending events - complete
                CompleteStage();
            }
            else if (!_upstreamWaiting && !_upstreamFinished)
            {
                // Pull more from upstream
                Pull(stage._in);
                _upstreamWaiting = true;
            }
        }

        private void ProcessText(string text)
        {
            var i = 0;
            while (i < text.Length)
            {
                // Find next line ending
                var lineEnd = -1;
                var endLength = 0;

                // Search for next line ending from position i
                for (var j = i; j < text.Length; j++)
                {
                    if (j < text.Length - 1 && text[j] == '\r' && text[j + 1] == '\n')
                    {
                        lineEnd = j;
                        endLength = 2;
                        break;
                    }
                    else if (text[j] == '\r' || text[j] == '\n')
                    {
                        lineEnd = j;
                        endLength = 1;
                        break;
                    }
                }

                if (lineEnd >= 0)
                {
                    // Found a line ending
                    var lineContent = text.Substring(i, lineEnd - i);
                    _lineBuffer.Append(lineContent);
                    var completeLine = _lineBuffer.ToString();
                    _lineBuffer.Clear();

                    // Process the complete line
                    if (completeLine == string.Empty)
                    {
                        // Empty line = event boundary
                        if (_hasData)
                        {
                            var evt = new ServerSentEvent(
                                Data: _dataAccumulator.ToString(),
                                EventType: _eventType,
                                Id: _id,
                                Retry: _retry);
                            _pending.Enqueue(evt);
                        }

                        // Reset for next event
                        ResetEvent();
                    }
                    else if (!completeLine.StartsWith(":"))
                    {
                        // Not a comment
                        ProcessField(completeLine);
                    }

                    i = lineEnd + endLength;
                }
                else
                {
                    // No line ending found - buffer remaining text
                    var remaining = text.Substring(i);
                    _lineBuffer.Append(remaining);
                    break;
                }
            }
        }

        private void ProcessField(string line)
        {
            string fieldName;
            string fieldValue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                fieldName = line;
                fieldValue = string.Empty;
            }
            else
            {
                fieldName = line.Substring(0, colonIndex);
                var valueStart = colonIndex + 1;

                // Strip leading space after colon
                if (valueStart < line.Length && line[valueStart] == ' ')
                {
                    valueStart++;
                }

                fieldValue = valueStart < line.Length ? line.Substring(valueStart) : string.Empty;
            }

            switch (fieldName)
            {
                case "data":
                    if (_dataAccumulator.Length > 0)
                    {
                        _dataAccumulator.Append('\n');
                    }
                    _dataAccumulator.Append(fieldValue);
                    _hasData = true;
                    break;

                case "event":
                    _eventType = fieldValue;
                    break;

                case "id":
                    _id = fieldValue;
                    break;

                case "retry":
                    if (int.TryParse(fieldValue, out var retryMs))
                    {
                        _retry = TimeSpan.FromMilliseconds(retryMs);
                    }
                    break;
            }
        }

        private void ResetEvent()
        {
            _dataAccumulator.Clear();
            _eventType = null;
            _id = null;
            _retry = null;
            _hasData = false;
        }
    }
}
