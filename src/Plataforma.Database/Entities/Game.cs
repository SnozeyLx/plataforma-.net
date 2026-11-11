namespace Plataforma.Database.Entities;

/// <summary>
/// Defines the publication state of a game in the platform catalog.
/// </summary>
public enum GameStatus
{
    /// <summary>
    /// The game is being configured and is not visible to players.
    /// </summary>
    Draft,

    /// <summary>
    /// The game is available in the catalog.
    /// </summary>
    Published,

    /// <summary>
    /// The game is no longer available but its historical data is retained.
    /// </summary>
    Archived
}

/// <summary>
/// Represents a game published on the platform.
/// </summary>
public sealed class Game
{
    /// <summary>
    /// Gets or sets the game identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique URL-friendly game identifier.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional game description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the optional catalog image URL.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the publication state.
    /// </summary>
    public GameStatus Status { get; set; } = GameStatus.Draft;

    /// <summary>
    /// Gets or sets the UTC creation time.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time of the last update.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
