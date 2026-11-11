namespace Plataforma.Database.Entities;

/// <summary>
/// Defines the outcome of a completed game session.
/// </summary>
public enum GameSessionOutcome
{
    /// <summary>
    /// The player won the session.
    /// </summary>
    Win,

    /// <summary>
    /// The player lost the session.
    /// </summary>
    Loss,

    /// <summary>
    /// The session ended in a draw.
    /// </summary>
    Draw,

    /// <summary>
    /// The session ended without a competitive result.
    /// </summary>
    Abandoned
}

/// <summary>
/// Stores the trusted result of a player's session in a game.
/// </summary>
public sealed class GameSession
{
    /// <summary>
    /// Gets or sets the session identifier allocated by the platform.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the player account identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the game identifier.
    /// </summary>
    public Guid GameId { get; set; }

    /// <summary>
    /// Gets or sets the UTC session start time.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC session end time, or null while the session is active.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optional game-specific score.
    /// </summary>
    public long? Score { get; set; }

    /// <summary>
    /// Gets or sets the outcome reported by the trusted game server.
    /// </summary>
    public GameSessionOutcome? Outcome { get; set; }

    /// <summary>
    /// Gets or sets the experience awarded for this session.
    /// </summary>
    public long ExperienceAwarded { get; set; }
}
