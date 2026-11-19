namespace Plataforma.Database.Entities;

/// <summary>
/// Stores a text message exchanged by two platform friends.
/// </summary>
public sealed class FriendMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SenderUserId { get; set; }

    public Guid RecipientUserId { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
