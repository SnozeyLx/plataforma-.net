namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Public profile details for the authenticated account.
/// </summary>
public sealed record PlayerProfileResponse(Guid UserId, string? PlayerName, int ProfileIconId);

/// <summary>
/// A public account returned by gamertag search.
/// </summary>
public sealed record PlayerSearchResponse(Guid UserId, string PlayerName);

/// <summary>
/// A confirmed friend in the authenticated account's friend list.
/// </summary>
public sealed record FriendResponse(
    Guid UserId,
    string PlayerName,
    DateTimeOffset FriendsSinceUtc);

/// <summary>
/// State of a friend request or accepted friendship.
/// </summary>
public enum FriendRequestStatus
{
    Pending,
    Accepted
}

/// <summary>
/// A friend request sent or received by the authenticated account.
/// </summary>
public sealed record FriendRequestResponse(
    Guid RequestId,
    Guid UserId,
    string PlayerName,
    FriendRequestStatus Status,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// An account blocked by the authenticated account.
/// </summary>
public sealed record BlockedPlayerResponse(
    Guid UserId,
    string PlayerName,
    DateTimeOffset BlockedAtUtc);
