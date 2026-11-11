namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a player's aggregate progression for one game.
/// </summary>
public sealed class GamePlayerProgress
{
    /// <summary>
    /// Gets or sets the player account identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the game identifier.
    /// </summary>
    public Guid GameId { get; set; }

    /// <summary>
    /// Gets or sets the player's current level.
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Gets or sets the player's accumulated experience points.
    /// </summary>
    public long Experience { get; set; }

    /// <summary>
    /// Gets or sets the number of completed matches.
    /// </summary>
    public long MatchesPlayed { get; set; }

    /// <summary>
    /// Gets or sets the accumulated play time in seconds.
    /// </summary>
    public long TotalPlaytimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the UTC time when progression was first created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time of the player's most recent session.
    /// </summary>
    public DateTime? LastPlayedAtUtc { get; set; }
}
