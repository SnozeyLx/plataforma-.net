namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a one-way block between two platform accounts.
/// </summary>
public sealed class UserBlock
{
    public Guid BlockerUserId { get; set; }

    public Guid BlockedUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
