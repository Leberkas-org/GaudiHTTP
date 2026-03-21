using System;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Control Stream State Machine  —  RFC 9114 §6.2.1
//
// Each side of an HTTP/3 connection MUST initiate a single control stream.
// The first frame on the control stream MUST be a SETTINGS frame.
// Receiving a second control stream from a peer is a connection error
// of type H3_STREAM_CREATION_ERROR. Closure of the control stream is
// a connection error of type H3_CLOSED_CRITICAL_STREAM.

/// <summary>
/// Tracks the lifecycle state of a single control stream direction
/// (either the local outbound or the remote inbound control stream).
/// </summary>
public enum ControlStreamState
{
    /// <summary>No control stream has been opened/received yet.</summary>
    NotOpened,

    /// <summary>Control stream opened but SETTINGS not yet sent/received.</summary>
    AwaitingSettings,

    /// <summary>SETTINGS frame has been sent/received — stream is active.</summary>
    Active,

    /// <summary>Control stream has been closed (connection error).</summary>
    Closed,
}

/// <summary>
/// HTTP/3 control stream state machine per RFC 9114 §6.2.1.
/// Tracks both the local (client-initiated) and remote (server-initiated)
/// control streams and enforces all MUST-level requirements.
/// </summary>
public sealed class Http3ControlStream
{
    /// <summary>Current state of the local (client-initiated) control stream.</summary>
    public ControlStreamState LocalState { get; private set; } = ControlStreamState.NotOpened;

    /// <summary>Current state of the remote (server-initiated) control stream.</summary>
    public ControlStreamState RemoteState { get; private set; } = ControlStreamState.NotOpened;

    /// <summary>
    /// The SETTINGS received from the server on the remote control stream,
    /// or <c>null</c> if not yet received.
    /// </summary>
    public Http3Settings? RemoteSettings { get; private set; }

    /// <summary>
    /// Opens the local (client) control stream and sends a SETTINGS frame.
    /// Returns the bytes to write to the unidirectional stream (stream type + SETTINGS frame).
    /// </summary>
    /// <param name="localSettings">The client's SETTINGS to send. If null, sends empty SETTINGS.</param>
    /// <returns>Serialized bytes: stream type prefix + SETTINGS frame.</returns>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.StreamCreationError"/> if a local
    /// control stream has already been opened.
    /// </exception>
    public byte[] OpenLocalStream(Http3Settings? localSettings = null)
    {
        if (LocalState != ControlStreamState.NotOpened)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.StreamCreationError,
                "Client MUST NOT open more than one control stream per connection (RFC 9114 §6.2.1).");
        }

        LocalState = ControlStreamState.AwaitingSettings;

        var settings = localSettings ?? new Http3Settings();
        var settingsFrame = settings.ToFrame();

        // Stream type prefix (0x00 for control) + SETTINGS frame
        var streamTypeSize = RFC9000.QuicVarInt.EncodedLength((long)Http3StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var buf = new byte[streamTypeSize + frameSize];
        var span = buf.AsSpan();

        var written = RFC9000.QuicVarInt.Encode((long)Http3StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        LocalState = ControlStreamState.Active;
        return buf;
    }

    /// <summary>
    /// Signals that a server-initiated unidirectional stream has been identified
    /// as a control stream (stream type = 0x00). Call this when the stream type
    /// byte has been read and identified as <see cref="Http3StreamType.Control"/>.
    /// </summary>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.StreamCreationError"/> if a remote
    /// control stream has already been received (RFC 9114 §6.2.1).
    /// </exception>
    public void OnRemoteControlStreamOpened()
    {
        if (RemoteState != ControlStreamState.NotOpened)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.StreamCreationError,
                "Receiving a second control stream from the server is a connection error (RFC 9114 §6.2.1).");
        }

        RemoteState = ControlStreamState.AwaitingSettings;
    }

    /// <summary>
    /// Processes a frame received on the remote control stream.
    /// The first frame MUST be SETTINGS; subsequent frames are validated
    /// for control-stream legality.
    /// </summary>
    /// <param name="frame">The decoded frame from the remote control stream.</param>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.MissingSettings"/> if the first frame
    /// is not SETTINGS (RFC 9114 §6.2.1).
    /// </exception>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.FrameUnexpected"/> if a second SETTINGS
    /// frame is received (RFC 9114 §7.2.4).
    /// </exception>
    public void OnRemoteFrame(Http3Frame frame)
    {
        if (RemoteState == ControlStreamState.NotOpened)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.ClosedCriticalStream,
                "Received frame on control stream before it was opened.");
        }

        if (RemoteState == ControlStreamState.Closed)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.ClosedCriticalStream,
                "Received frame on closed control stream.");
        }

        if (RemoteState == ControlStreamState.AwaitingSettings)
        {
            if (frame is not Http3SettingsFrame settingsFrame)
            {
                throw new Http3ConnectionException(
                    Http3ErrorCode.MissingSettings,
                    $"First frame on control stream MUST be SETTINGS, got {frame.Type} (RFC 9114 §6.2.1).");
            }

            // Parse the settings from the frame parameters
            var settings = new Http3Settings();
            foreach (var (id, val) in settingsFrame.Parameters)
            {
                settings.Set(id, val);
            }

            RemoteSettings = settings;
            RemoteState = ControlStreamState.Active;
            return;
        }

        // Active state — reject duplicate SETTINGS and DATA/HEADERS (request-stream only frames)
        if (frame is Http3SettingsFrame)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.FrameUnexpected,
                "A second SETTINGS frame on the control stream is a connection error (RFC 9114 §7.2.4).");
        }

        if (frame is Http3DataFrame)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.FrameUnexpected,
                "DATA frames are not permitted on the control stream (RFC 9114 §7.2.1).");
        }

        if (frame is Http3HeadersFrame)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.FrameUnexpected,
                "HEADERS frames are not permitted on the control stream (RFC 9114 §7.2.2).");
        }

        // GOAWAY, CANCEL_PUSH, MAX_PUSH_ID are valid on the control stream — accept them.
    }

    /// <summary>
    /// Signals that the remote control stream has been closed by the server.
    /// This is always a connection error of type H3_CLOSED_CRITICAL_STREAM (RFC 9114 §6.2.1).
    /// </summary>
    /// <exception cref="Http3ConnectionException">
    /// Always thrown with <see cref="Http3ErrorCode.ClosedCriticalStream"/>.
    /// </exception>
    public void OnRemoteControlStreamClosed()
    {
        RemoteState = ControlStreamState.Closed;
        throw new Http3ConnectionException(
            Http3ErrorCode.ClosedCriticalStream,
            "Closure of the control stream MUST be treated as a connection error of type H3_CLOSED_CRITICAL_STREAM (RFC 9114 §6.2.1).");
    }

    /// <summary>
    /// Signals that the local control stream has been closed (e.g. by transport).
    /// This is always a connection error of type H3_CLOSED_CRITICAL_STREAM (RFC 9114 §6.2.1).
    /// </summary>
    /// <exception cref="Http3ConnectionException">
    /// Always thrown with <see cref="Http3ErrorCode.ClosedCriticalStream"/>.
    /// </exception>
    public void OnLocalControlStreamClosed()
    {
        LocalState = ControlStreamState.Closed;
        throw new Http3ConnectionException(
            Http3ErrorCode.ClosedCriticalStream,
            "Closure of the control stream MUST be treated as a connection error of type H3_CLOSED_CRITICAL_STREAM (RFC 9114 §6.2.1).");
    }
}