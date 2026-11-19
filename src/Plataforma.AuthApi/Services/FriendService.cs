using Microsoft.EntityFrameworkCore;
using Plataforma.AuthApi.Contracts;
using Plataforma.Database;
using Plataforma.Database.Entities;
using Plataforma.Database.Identity;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Implements profile discovery and friendship operations using the platform database.
/// </summary>
public sealed class FriendService(
    PlatformDbContext dbContext,
    TimeProvider timeProvider) : IFriendService
{
    private const int SearchResultLimit = 20;

    public async Task<PlayerProfileResponse?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PlayerProfileResponse(user.Id, user.PlayerName, user.ProfileIconId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> IsPlayerNameTakenAsync(
        string normalizedPlayerName,
        Guid? exceptUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.NormalizedPlayerName == normalizedPlayerName &&
                    (exceptUserId == null || user.Id != exceptUserId),
                cancellationToken);
    }

    public async Task<SetPlayerNameResult> SetPlayerNameAsync(
        Guid userId,
        string playerName,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            account => account.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return new SetPlayerNameResult(SetPlayerNameStatus.AccountNotFound, null);
        }

        var normalizedPlayerName = NormalizePlayerName(playerName);
        if (await IsPlayerNameTakenAsync(normalizedPlayerName, userId, cancellationToken))
        {
            return new SetPlayerNameResult(SetPlayerNameStatus.Taken, null);
        }

        user.PlayerName = playerName;
        user.NormalizedPlayerName = normalizedPlayerName;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsPlayerNameTakenAsync(normalizedPlayerName, userId, cancellationToken))
            {
                return new SetPlayerNameResult(SetPlayerNameStatus.Taken, null);
            }

            throw;
        }

        return new SetPlayerNameResult(
            SetPlayerNameStatus.Updated,
            new PlayerProfileResponse(user.Id, user.PlayerName, user.ProfileIconId));
    }

    public async Task<PlayerProfileResponse?> UpdateProfileAsync(
        Guid userId,
        string playerName,
        int profileIconId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            account => account.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return null;
        }

        var normalizedPlayerName = NormalizePlayerName(playerName);
        if (await IsPlayerNameTakenAsync(normalizedPlayerName, userId, cancellationToken))
        {
            return null;
        }

        user.PlayerName = playerName;
        user.NormalizedPlayerName = normalizedPlayerName;
        user.ProfileIconId = profileIconId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsPlayerNameTakenAsync(normalizedPlayerName, userId, cancellationToken))
            {
                return null;
            }

            throw;
        }

        return new PlayerProfileResponse(user.Id, user.PlayerName, user.ProfileIconId);
    }

    public async Task<IReadOnlyList<PlayerSearchResponse>> SearchAsync(
        Guid userId,
        string prefix,
        CancellationToken cancellationToken)
    {
        var normalizedPrefix = NormalizePlayerName(prefix);

        return await dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.Id != userId &&
                user.NormalizedPlayerName != null &&
                user.NormalizedPlayerName.StartsWith(normalizedPrefix) &&
                !dbContext.UserBlocks.Any(block =>
                    (block.BlockerUserId == userId && block.BlockedUserId == user.Id) ||
                    (block.BlockerUserId == user.Id && block.BlockedUserId == userId)) &&
                !dbContext.FriendConnections.Any(connection =>
                    (connection.LowerUserId == userId && connection.UpperUserId == user.Id) ||
                    (connection.LowerUserId == user.Id && connection.UpperUserId == userId)))
            .OrderBy(user => user.NormalizedPlayerName)
            .Take(SearchResultLimit)
            .Select(user => new PlayerSearchResponse(user.Id, user.PlayerName!))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FriendResponse>> GetFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connections = await dbContext.FriendConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == FriendConnectionStatus.Accepted &&
                (connection.LowerUserId == userId || connection.UpperUserId == userId))
            .Select(connection => new
            {
                UserId = connection.LowerUserId == userId
                    ? connection.UpperUserId
                    : connection.LowerUserId,
                FriendsSinceUtc = connection.RespondedAtUtc ?? connection.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        if (connections.Count == 0)
        {
            return [];
        }

        var friendsById = await dbContext.Users
            .AsNoTracking()
            .Where(user => connections.Select(connection => connection.UserId).Contains(user.Id))
            .Select(user => new { user.Id, user.PlayerName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return connections
            .Where(connection => friendsById.ContainsKey(connection.UserId))
            .Select(connection => new FriendResponse(
                connection.UserId,
                friendsById[connection.UserId].PlayerName ?? string.Empty,
                AsUtcOffset(connection.FriendsSinceUtc)))
            .OrderBy(friend => friend.PlayerName)
            .ToArray();
    }

    public Task<IReadOnlyList<FriendRequestResponse>> GetRequestsAsync(
        Guid userId,
        bool outgoing,
        CancellationToken cancellationToken)
    {
        return GetRequestListAsync(userId, outgoing, cancellationToken);
    }

    public async Task<FriendRequestResponse?> GetRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var connection = await dbContext.FriendConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                request => request.Id == requestId &&
                    (request.RequesterUserId == userId || request.RecipientUserId == userId),
                cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var otherUserId = connection.RequesterUserId == userId
            ? connection.RecipientUserId
            : connection.RequesterUserId;
        var playerName = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == otherUserId)
            .Select(user => user.PlayerName)
            .SingleOrDefaultAsync(cancellationToken);
        if (playerName is null)
        {
            return null;
        }

        return ToRequestResponse(connection, otherUserId, playerName);
    }

    public async Task<SendFriendRequestResult> SendRequestAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (userId == targetUserId)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.CannotRequestSelf, null);
        }

        var target = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == targetUserId && user.PlayerName != null)
            .Select(user => new { user.Id, user.PlayerName })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.TargetNotFound, null);
        }

        if (await AreEitherBlockedAsync(userId, targetUserId, cancellationToken))
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.Blocked, null);
        }

        var (lowerUserId, upperUserId) = OrderIds(userId, targetUserId);
        var existingConnection = await dbContext.FriendConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                connection =>
                    connection.LowerUserId == lowerUserId &&
                    connection.UpperUserId == upperUserId,
                cancellationToken);
        if (existingConnection is not null)
        {
            return new SendFriendRequestResult(SendFriendRequestStatus.AlreadyConnected, null);
        }

        var request = new FriendConnection
        {
            LowerUserId = lowerUserId,
            UpperUserId = upperUserId,
            RequesterUserId = userId,
            RecipientUserId = targetUserId,
            Status = FriendConnectionStatus.Pending,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.FriendConnections.Add(request);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(request).State = EntityState.Detached;
            var concurrentConnectionExists = await dbContext.FriendConnections
                .AsNoTracking()
                .AnyAsync(
                    connection =>
                        connection.LowerUserId == lowerUserId &&
                        connection.UpperUserId == upperUserId,
                    cancellationToken);
            if (!concurrentConnectionExists)
            {
                throw;
            }

            return new SendFriendRequestResult(SendFriendRequestStatus.AlreadyConnected, null);
        }

        return new SendFriendRequestResult(
            SendFriendRequestStatus.Created,
            ToRequestResponse(request, target.Id, target.PlayerName!));
    }

    public async Task<FriendRequestResponse?> AcceptRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.FriendConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                connection =>
                    connection.Id == requestId &&
                    connection.RecipientUserId == userId &&
                    connection.Status == FriendConnectionStatus.Pending,
                cancellationToken);
        if (request is null)
        {
            return null;
        }

        if (await AreEitherBlockedAsync(request.RequesterUserId, userId, cancellationToken))
        {
            return null;
        }

        var respondedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var updatedCount = await dbContext.FriendConnections
            .Where(connection =>
                connection.Id == requestId &&
                connection.RecipientUserId == userId &&
                connection.Status == FriendConnectionStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(connection => connection.Status, FriendConnectionStatus.Accepted)
                    .SetProperty(connection => connection.RespondedAtUtc, respondedAtUtc),
                cancellationToken);
        if (updatedCount == 0)
        {
            return null;
        }

        request.Status = FriendConnectionStatus.Accepted;
        request.RespondedAtUtc = respondedAtUtc;
        var playerName = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == request.RequesterUserId)
            .Select(user => user.PlayerName)
            .SingleOrDefaultAsync(cancellationToken);

        return playerName is null
            ? null
            : ToRequestResponse(request, request.RequesterUserId, playerName);
    }

    public async Task<bool> DeclineRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var deletedCount = await dbContext.FriendConnections
            .Where(connection =>
                connection.Id == requestId &&
                connection.RecipientUserId == userId &&
                connection.Status == FriendConnectionStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);
        return deletedCount > 0;
    }

    public async Task<bool> CancelRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var deletedCount = await dbContext.FriendConnections
            .Where(connection =>
                connection.Id == requestId &&
                connection.RequesterUserId == userId &&
                connection.Status == FriendConnectionStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);
        return deletedCount > 0;
    }

    public async Task<bool> RemoveFriendAsync(
        Guid userId,
        Guid friendUserId,
        CancellationToken cancellationToken)
    {
        if (userId == friendUserId)
        {
            return false;
        }

        var (lowerUserId, upperUserId) = OrderIds(userId, friendUserId);
        var deletedCount = await dbContext.FriendConnections
            .Where(connection =>
                connection.LowerUserId == lowerUserId &&
                connection.UpperUserId == upperUserId &&
                connection.Status == FriendConnectionStatus.Accepted)
            .ExecuteDeleteAsync(cancellationToken);
        return deletedCount > 0;
    }

    public async Task<IReadOnlyList<BlockedPlayerResponse>> GetBlockedPlayersAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var blocks = await dbContext.UserBlocks
            .AsNoTracking()
            .Where(block => block.BlockerUserId == userId)
            .Join(
                dbContext.Users.AsNoTracking(),
                block => block.BlockedUserId,
                user => user.Id,
                (block, user) => new
                {
                    user.Id,
                    user.PlayerName,
                    block.CreatedAtUtc
                })
            .ToListAsync(cancellationToken);

        return blocks
            .Select(block => new BlockedPlayerResponse(
                block.Id,
                block.PlayerName ?? string.Empty,
                AsUtcOffset(block.CreatedAtUtc)))
            .OrderBy(block => block.PlayerName)
            .ToArray();
    }

    public async Task<BlockPlayerResult> BlockPlayerAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (userId == targetUserId)
        {
            return new BlockPlayerResult(BlockPlayerStatus.CannotBlockSelf);
        }

        var targetExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == targetUserId, cancellationToken);
        if (!targetExists)
        {
            return new BlockPlayerResult(BlockPlayerStatus.TargetNotFound);
        }

        var alreadyBlocked = await dbContext.UserBlocks
            .AnyAsync(
                block => block.BlockerUserId == userId && block.BlockedUserId == targetUserId,
                cancellationToken);
        if (alreadyBlocked)
        {
            return new BlockPlayerResult(BlockPlayerStatus.Blocked);
        }

        dbContext.UserBlocks.Add(new UserBlock
        {
            BlockerUserId = userId,
            BlockedUserId = targetUserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        var (lowerUserId, upperUserId) = OrderIds(userId, targetUserId);
        var connection = await dbContext.FriendConnections
            .SingleOrDefaultAsync(
                entry =>
                    entry.LowerUserId == lowerUserId &&
                    entry.UpperUserId == upperUserId,
                cancellationToken);
        if (connection is not null)
        {
            dbContext.FriendConnections.Remove(connection);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicateBlockExists = await dbContext.UserBlocks
                .AsNoTracking()
                .AnyAsync(
                    block => block.BlockerUserId == userId && block.BlockedUserId == targetUserId,
                    cancellationToken);
            if (!duplicateBlockExists)
            {
                throw;
            }
        }

        return new BlockPlayerResult(BlockPlayerStatus.Blocked);
    }

    public async Task<bool> UnblockPlayerAsync(
        Guid userId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var block = await dbContext.UserBlocks
            .SingleOrDefaultAsync(
                entry => entry.BlockerUserId == userId && entry.BlockedUserId == targetUserId,
                cancellationToken);
        if (block is null)
        {
            return false;
        }

        dbContext.UserBlocks.Remove(block);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<FriendRequestResponse>> GetRequestListAsync(
        Guid userId,
        bool outgoing,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FriendConnections
            .AsNoTracking()
            .Where(connection => connection.Status == FriendConnectionStatus.Pending);
        var requests = outgoing
            ? await query
                .Where(connection => connection.RequesterUserId == userId)
                .Select(connection => new
                {
                    connection.Id,
                    UserId = connection.RecipientUserId,
                    connection.Status,
                    connection.CreatedAtUtc
                })
                .ToListAsync(cancellationToken)
            : await query
                .Where(connection => connection.RecipientUserId == userId)
                .Select(connection => new
                {
                    connection.Id,
                    UserId = connection.RequesterUserId,
                    connection.Status,
                    connection.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return [];
        }

        var userIds = requests.Select(request => request.UserId).Distinct().ToArray();
        var playerNames = await dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.PlayerName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return requests
            .Where(request => playerNames.ContainsKey(request.UserId))
            .Select(request => new FriendRequestResponse(
                request.Id,
                request.UserId,
                playerNames[request.UserId].PlayerName ?? string.Empty,
                ToApiStatus(request.Status),
                AsUtcOffset(request.CreatedAtUtc)))
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToArray();
    }

    private Task<bool> AreEitherBlockedAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(
                block =>
                    (block.BlockerUserId == firstUserId && block.BlockedUserId == secondUserId) ||
                    (block.BlockerUserId == secondUserId && block.BlockedUserId == firstUserId),
                cancellationToken);
    }

    private static FriendRequestResponse ToRequestResponse(
        FriendConnection connection,
        Guid otherUserId,
        string playerName)
    {
        return new FriendRequestResponse(
            connection.Id,
            otherUserId,
            playerName,
            ToApiStatus(connection.Status),
            AsUtcOffset(connection.CreatedAtUtc));
    }

    private static FriendRequestStatus ToApiStatus(FriendConnectionStatus status) =>
        status == FriendConnectionStatus.Accepted
            ? FriendRequestStatus.Accepted
            : FriendRequestStatus.Pending;

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static string NormalizePlayerName(string playerName) =>
        playerName.ToUpperInvariant();

    private static (Guid Lower, Guid Upper) OrderIds(Guid first, Guid second) =>
        first.CompareTo(second) < 0 ? (first, second) : (second, first);

}
