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
}
