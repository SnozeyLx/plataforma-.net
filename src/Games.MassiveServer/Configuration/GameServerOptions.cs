using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Configuration;

/// <summary>Configuration for the dedicated UDP game server.</summary>
public sealed class GameServerOptions
{
    /// <summary>The configuration section containing game server settings.</summary>
    public const string SectionName = "GameServer";

    /// <summary>Gets or sets the UDP port on which this server listens.</summary>
    public int UdpPort { get; set; } = 9050;

    /// <summary>Gets or sets the address advertised to clients in matchmaking responses.</summary>
    public string PublicAddress { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the UDP poll tick interval in milliseconds.</summary>
    public int TickIntervalMs { get; set; } = 5;

    /// <summary>Gets or sets how long a connected UDP peer has to complete the ClientHello handshake.</summary>
    public int HandshakeTimeoutSeconds { get; set; } = 5;

    /// <summary>Gets or sets the maximum UDP connection requests allowed per IP within the rate-limit window.</summary>
    public int MaxConnectionRequestsPerIpPerWindow { get; set; } = 10;

    /// <summary>Gets or sets the UDP connection request rate-limit window in seconds.</summary>
    public int ConnectionRequestWindowSeconds { get; set; } = 10;

    /// <summary>Gets or sets the maximum UDP gameplay packets accepted per peer per second.</summary>
    public int MaxPacketsPerSecondPerPeer { get; set; } = 90;

    /// <summary>Gets or sets the maximum custom gameplay message payload size in bytes.</summary>
    public int MaximumGameDataBytes { get; set; } = 1024;

    /// <summary>Gets or sets whether the legacy "ping"/"pong" packet is accepted.</summary>
    public bool EnableLegacyPing { get; set; } = true;

    /// <summary>Gets or sets whether clients must use the encrypted compact v2 (0xD6) UDP protocol.</summary>
    public bool RequireEncryptedUdp { get; set; }

    /// <summary>Gets or sets whether the WebSocket bridge endpoint (/ws/game) is enabled for browser/WebGL clients.</summary>
    public bool EnableWebSocketTransport { get; set; } = true;

    /// <summary>
    /// Gets or sets the optional public WebSocket URL (ws:// or wss://) advertised to WebGL clients in matchmaking responses.
    /// </summary>
    public string? PublicWebSocketUrl { get; set; }

    /// <summary>
    /// Gets or sets the interval in milliseconds to coalesce outbound state broadcasts (0 = immediate relay).
    /// </summary>
    public int StateRelayIntervalMs { get; set; }
}

/// <summary>Validates dedicated UDP game server settings.</summary>
public sealed class GameServerOptionsValidator : IValidateOptions<GameServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GameServerOptions options)
    {
        var errors = new List<string>();
        if (options.UdpPort is <= 0 or > 65535)
        {
            errors.Add("GameServer:UdpPort must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.PublicAddress))
        {
            errors.Add("GameServer:PublicAddress must be configured.");
        }

        if (options.TickIntervalMs is < 1 or > 50)
        {
            errors.Add("GameServer:TickIntervalMs must be between 1 and 50.");
        }

        if (options.HandshakeTimeoutSeconds is < 1 or > 60)
        {
            errors.Add("GameServer:HandshakeTimeoutSeconds must be between 1 and 60.");
        }

        if (options.MaxConnectionRequestsPerIpPerWindow is < 1 or > 500)
        {
            errors.Add("GameServer:MaxConnectionRequestsPerIpPerWindow must be between 1 and 500.");
        }

        if (options.ConnectionRequestWindowSeconds is < 1 or > 60)
        {
            errors.Add("GameServer:ConnectionRequestWindowSeconds must be between 1 and 60.");
        }

        if (options.MaxPacketsPerSecondPerPeer is < 10 or > 1000)
        {
            errors.Add("GameServer:MaxPacketsPerSecondPerPeer must be between 10 and 1000.");
        }

        if (options.MaximumGameDataBytes is < 64 or > 4096)
        {
            errors.Add("GameServer:MaximumGameDataBytes must be between 64 and 4096.");
        }

        if (options.StateRelayIntervalMs is < 0 or > 100)
        {
            errors.Add("GameServer:StateRelayIntervalMs must be between 0 and 100.");
        }

        if (!string.IsNullOrWhiteSpace(options.PublicWebSocketUrl))
        {
            if (!Uri.TryCreate(options.PublicWebSocketUrl.Trim(), UriKind.Absolute, out var wsUri)
                || (!string.Equals(wsUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(wsUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("GameServer:PublicWebSocketUrl must be an absolute ws:// or wss:// URL.");
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
