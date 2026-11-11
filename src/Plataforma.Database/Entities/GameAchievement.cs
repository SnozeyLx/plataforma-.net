namespace Plataforma.Database.Entities;

/// <summary>
/// Defines the lifecycle state of an achievement.
/// </summary>
public enum AchievementStatus
{
    /// <summary>
    /// The achievement is available to players.
    /// </summary>
    Active,

    /// <summary>
    /// The achievement is retained for historical records but no longer awarded.
    /// </summary>
    Archived
}

/// <summary>
/// Defines an achievement that belongs to a single game.
/// </summary>
public sealed class GameAchievement
{
    /// <summary>
    /// Gets or sets the achievement identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the owning game identifier.
    /// </summary>
    public Guid GameId { get; set; }

    /// <summary>
    /// Gets or sets the unique code within the game.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the achievement display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional achievement description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the number of platform achievement points awarded.
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// Gets or sets the target value for simple numeric progress tracking.
    /// </summary>
    public long TargetProgress { get; set; } = 1;

    /// <summary>
    /// Gets or sets the optional game-specific criteria as JSON.
    /// </summary>
    public string? CriteriaJson { get; set; }

    /// <summary>
    /// Gets or sets the achievement lifecycle state.
    /// </summary>
    public AchievementStatus Status { get; set; } = AchievementStatus.Active;

    /// <summary>
    /// Gets or sets the UTC creation time.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
