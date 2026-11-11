namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a player's progress toward one game achievement.
/// </summary>
public sealed class UserAchievementProgress
{
    /// <summary>
    /// Gets or sets the player account identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the achievement identifier.
    /// </summary>
    public Guid AchievementId { get; set; }

    /// <summary>
    /// Gets or sets the current progress value.
    /// </summary>
    public long ProgressCurrent { get; set; }

    /// <summary>
    /// Gets or sets the UTC unlock time, or null while the achievement is incomplete.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time when progress was first recorded.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time when progress was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
