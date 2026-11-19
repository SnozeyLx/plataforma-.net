namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a pending friend request or an accepted friendship between two accounts.
/// </summary>
public sealed class FriendConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LowerUserId { get; set; }

    public Guid UpperUserId { get; set; }

    public Guid RequesterUserId { get; set; }

    public Guid RecipientUserId { get; set; }

    public FriendConnectionStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RespondedAtUtc { get; set; }
}

/// <summary>
/// Indicates whether a friend connection is pending or accepted.
/// </summary>
public enum FriendConnectionStatus
{
    Pending,
    Accepted
}
