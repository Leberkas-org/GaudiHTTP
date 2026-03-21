using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Settings Exchange  —  RFC 9114 §7.2.4
//
// Each side of an HTTP/3 connection MUST send a SETTINGS frame as the first
// frame on its control stream (§6.2.1). The peer applies the received settings.
//
// This class orchestrates the bidirectional settings exchange:
//   1. Client opens control stream → sends SETTINGS (via Http3ControlStream)
//   2. Server opens control stream → sends SETTINGS
//   3. Both sides apply the received settings
//
// SETTINGS_MAX_FIELD_SECTION_SIZE (0x06) is advisory — an endpoint SHOULD NOT
// produce a field section that exceeds the peer's indicated limit (§4.2.2).
//
// HTTP/2-specific settings (0x02–0x05) MUST NOT appear (§7.2.4.1).
// Receiving one is a connection error of type H3_SETTINGS_ERROR.

/// <summary>
/// Manages the HTTP/3 settings exchange for a connection (RFC 9114 §7.2.4).
/// Wraps <see cref="Http3ControlStream"/> and enforces applied settings.
/// </summary>
public sealed class Http3SettingsExchange
{
    private readonly Http3ControlStream _controlStream;
    private readonly Http3Settings _localSettings;

    /// <summary>
    /// Creates a new settings exchange with the specified local settings.
    /// </summary>
    /// <param name="localSettings">
    /// Client settings to send. If null, sends empty SETTINGS (all defaults).
    /// </param>
    public Http3SettingsExchange(Http3Settings? localSettings = null)
    {
        _controlStream = new Http3ControlStream();
        _localSettings = localSettings ?? new Http3Settings();
    }

    /// <summary>
    /// The local client settings that will be (or have been) sent.
    /// </summary>
    public Http3Settings LocalSettings => _localSettings;

    /// <summary>
    /// The remote server settings received, or <c>null</c> if not yet received.
    /// </summary>
    public Http3Settings? RemoteSettings => _controlStream.RemoteSettings;

    /// <summary>
    /// Whether the local control stream has been opened and SETTINGS sent.
    /// </summary>
    public bool LocalSettingsSent => _controlStream.LocalState == ControlStreamState.Active;

    /// <summary>
    /// Whether the remote SETTINGS have been received and applied.
    /// </summary>
    public bool RemoteSettingsReceived => _controlStream.RemoteState == ControlStreamState.Active;

    /// <summary>
    /// The effective MAX_FIELD_SECTION_SIZE limit advertised by the remote peer,
    /// or <c>null</c> if no limit was received (meaning unlimited).
    /// </summary>
    public long? RemoteMaxFieldSectionSize => RemoteSettings?.MaxFieldSectionSize;

    /// <summary>
    /// Opens the local control stream and sends SETTINGS as the first frame.
    /// Returns the bytes to write to the QUIC unidirectional stream.
    /// </summary>
    /// <returns>Serialized bytes: stream type prefix + SETTINGS frame.</returns>
    /// <exception cref="Http3ConnectionException">
    /// Thrown if the local control stream has already been opened.
    /// </exception>
    public byte[] SendSettings()
    {
        return _controlStream.OpenLocalStream(_localSettings);
    }

    /// <summary>
    /// Signals that a server-initiated control stream has been received.
    /// </summary>
    public void OnRemoteControlStreamOpened()
    {
        _controlStream.OnRemoteControlStreamOpened();
    }

    /// <summary>
    /// Processes a frame received on the remote control stream.
    /// The first frame MUST be SETTINGS.
    /// </summary>
    public void OnRemoteFrame(Http3Frame frame)
    {
        _controlStream.OnRemoteFrame(frame);
    }

    /// <summary>
    /// Validates that a field section does not exceed the peer's
    /// SETTINGS_MAX_FIELD_SECTION_SIZE (RFC 9114 §4.2.2).
    ///
    /// The field section size is the sum of the uncompressed name length,
    /// value length, and an overhead of 32 bytes per field line
    /// (same definition as HTTP/2, RFC 9113 §6.5.2).
    /// </summary>
    /// <param name="headers">The header list to validate.</param>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.ExcessiveLoad"/> if the field section
    /// exceeds the peer's advertised limit.
    /// </exception>
    public void ValidateFieldSectionSize(IReadOnlyList<(string Name, string Value)> headers)
    {
        if (RemoteMaxFieldSectionSize is not { } maxSize)
        {
            return; // No limit advertised
        }

        var size = CalculateFieldSectionSize(headers);

        if (size > maxSize)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.ExcessiveLoad,
                $"Field section size {size} exceeds peer's SETTINGS_MAX_FIELD_SECTION_SIZE {maxSize} (RFC 9114 §4.2.2).");
        }
    }

    /// <summary>
    /// Calculates the uncompressed field section size as defined by
    /// RFC 9110 §5.2 and referenced by RFC 9114 §4.2.2.
    /// Size = sum of (name_length + value_length + 32) for each field.
    /// </summary>
    public static long CalculateFieldSectionSize(IReadOnlyList<(string Name, string Value)> headers)
    {
        var size = 0L;
        for (var i = 0; i < headers.Count; i++)
        {
            var (name, value) = headers[i];
            size += name.Length + value.Length + 32;
        }

        return size;
    }

    /// <summary>
    /// Validates that a pre-calculated field section size does not exceed
    /// the peer's SETTINGS_MAX_FIELD_SECTION_SIZE.
    /// </summary>
    /// <param name="fieldSectionSize">The calculated field section size in bytes.</param>
    /// <exception cref="Http3ConnectionException">
    /// Thrown with <see cref="Http3ErrorCode.ExcessiveLoad"/> if the size exceeds the limit.
    /// </exception>
    public void ValidateFieldSectionSize(long fieldSectionSize)
    {
        if (RemoteMaxFieldSectionSize is not { } maxSize)
        {
            return;
        }

        if (fieldSectionSize > maxSize)
        {
            throw new Http3ConnectionException(
                Http3ErrorCode.ExcessiveLoad,
                $"Field section size {fieldSectionSize} exceeds peer's SETTINGS_MAX_FIELD_SECTION_SIZE {maxSize} (RFC 9114 §4.2.2).");
        }
    }

    /// <summary>
    /// Validates that a SETTINGS frame payload does not contain HTTP/2-specific
    /// settings (RFC 9114 §7.2.4.1). This is called automatically during
    /// deserialization by <see cref="Http3Settings.Deserialize"/>, but can
    /// also be used for pre-validation of raw payloads.
    /// </summary>
    /// <param name="parameters">The setting identifier-value pairs to validate.</param>
    /// <exception cref="Http3SettingsException">
    /// Thrown if any parameter uses a reserved HTTP/2 identifier.
    /// </exception>
    public static void RejectForbiddenH2Settings(IReadOnlyList<(long Identifier, long Value)> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var (id, _) = parameters[i];
            if (Http3SettingId.IsReservedH2Setting(id))
            {
                throw new Http3SettingsException(
                    $"Setting identifier 0x{id:x2} is a reserved HTTP/2 setting and MUST NOT appear in HTTP/3 (RFC 9114 §7.2.4.1).");
            }
        }
    }
}
