namespace Plataforma.Database.Entities;

/// <summary>
/// Records a player's persistent ownership of a game.
/// </summary>
public sealed class GameEntitlement
{
    /// <summary>
    /// Gets or sets the owning player identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the owned game identifier.
    /// </summary>
    public Guid GameId { get; set; }

    /// <summary>
    /// Gets or sets how the entitlement was granted.
    /// </summary>
    public string GrantType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the entitlement was granted in UTC.
    /// </summary>
    public DateTime GrantedAtUtc { get; set; }
}
