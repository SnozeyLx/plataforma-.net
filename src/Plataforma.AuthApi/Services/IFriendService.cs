using Plataforma.AuthApi.Contracts;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Manages public player profiles, friend relationships, and account blocks.
/// </summary>
public interface IFriendService
{
    Task<PlayerProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsPlayerNameTakenAsync(
        string normalizedPlayerName,
        Guid? exceptUserId,
        CancellationToken cancellationToken);

    Task<SetPlayerNameResult> SetPlayerNameAsync(
        Guid userId,
        string playerName,
        CancellationToken cancellationToken);

    Task<PlayerProfileResponse?> UpdateProfileAsync(
        Guid userId,
        string playerName,
        int profileIconId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerSearchResponse>> SearchAsync(
        Guid userId,
        string prefix,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FriendResponse>> GetFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FriendRequestResponse>> GetRequestsAsync(
        Guid userId,
        bool outgoing,
        CancellationToken cancellationToken);

    Task<FriendRequestResponse?> GetRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<SendFriendRequestResult> SendRequestAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<FriendRequestResponse?> AcceptRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<bool> DeclineRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<bool> CancelRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken);

    Task<bool> RemoveFriendAsync(
        Guid userId,
        Guid friendUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockedPlayerResponse>> GetBlockedPlayersAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<BlockPlayerResult> BlockPlayerAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken);

    Task<bool> UnblockPlayerAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken);
}

public enum SetPlayerNameStatus
{
    Updated,
    Taken,
    AccountNotFound
}

public sealed record SetPlayerNameResult(SetPlayerNameStatus Status, PlayerProfileResponse? Profile);

public enum SendFriendRequestStatus
{
    Created,
    TargetNotFound,
    CannotRequestSelf,
    Blocked,
    AlreadyConnected
}

public sealed record SendFriendRequestResult(
    SendFriendRequestStatus Status,
    FriendRequestResponse? Request);

public enum BlockPlayerStatus
{
    Blocked,
    TargetNotFound,
    CannotBlockSelf
}

public sealed record BlockPlayerResult(BlockPlayerStatus Status);
