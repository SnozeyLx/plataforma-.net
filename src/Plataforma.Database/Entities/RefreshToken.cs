using Plataforma.Database.Identity;

namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a hashed, single-use refresh token and its rotation family.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>
    /// Gets or sets the refresh token record identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the owning user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 hash of the opaque refresh token.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier shared by all tokens in a rotation family.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation time.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiration time.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC revocation time, if the token has been revoked.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the hash of the replacement token, when rotated.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// Gets or sets the optimistic concurrency stamp.
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the owning user.
    /// </summary>
    public ApplicationUser User { get; set; } = null!;
}
