namespace Games.MassiveServer.Hosting;

/// <summary>Provides matchmaking and UDP defaults for each dedicated game mode.</summary>
public static class GameServerPresets
{
    /// <summary>Defaults for massive matches, which may start with a single player.</summary>
    public static GameServerPreset Massive { get; } = new(
        "arena-massive",
        MinPlayers: 1,
        MaxPlayers: 50,
        UdpPort: 9050,
        MatchStartDelaySeconds: 0,
        PersistentWorld: true);

    /// <summary>Defaults for cooperative matches of one to four players.</summary>
    public static GameServerPreset Coop { get; } = new(
        "arena-coop",
        MinPlayers: 1,
        MaxPlayers: 4,
        UdpPort: 9051,
        MatchStartDelaySeconds: 2,
        PersistentWorld: false);
}

/// <summary>Describes the default matchmaking and transport settings for a game mode.</summary>
/// <param name="GameCode">The catalog code associated with the game mode.</param>
/// <param name="MinPlayers">The minimum players required to start a match.</param>
/// <param name="MaxPlayers">The maximum players allowed in a match.</param>
/// <param name="UdpPort">The local UDP port used by the game server.</param>
/// <param name="MatchStartDelaySeconds">The delay used to collect players after the minimum is reached.</param>
/// <param name="PersistentWorld">Whether players may join an active match until its capacity is reached.</param>
public sealed record GameServerPreset(
    string GameCode,
    int MinPlayers,
    int MaxPlayers,
    int UdpPort,
    int MatchStartDelaySeconds,
    bool PersistentWorld);
