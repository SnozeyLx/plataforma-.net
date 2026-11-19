using Microsoft.EntityFrameworkCore;
using Plataforma.AuthApi.Contracts;
using Plataforma.Database;
using Plataforma.Database.Entities;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Persists direct chat messages and enforces the friendship boundary.
/// </summary>
public sealed class FriendChatService(
    PlatformDbContext dbContext,
    TimeProvider timeProvider) : IFriendChatService
{
    private const int MaximumMessageLength = 2000;

    public async Task<bool> CanChatAsync(
        Guid userId,
        Guid friendUserId,
        CancellationToken cancellationToken)
    {
        if (userId == friendUserId)
        {
            return false;
        }

        var (lowerUserId, upperUserId) = OrderIds(userId, friendUserId);
        var isFriend = await dbContext.FriendConnections
            .AsNoTracking()
            .AnyAsync(
                connection =>
                    connection.LowerUserId == lowerUserId &&
                    connection.UpperUserId == upperUserId &&
                    connection.Status == FriendConnectionStatus.Accepted,
                cancellationToken);
        if (!isFriend)
        {
            return false;
        }

        return !await dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(
                block =>
                    (block.BlockerUserId == userId && block.BlockedUserId == friendUserId) ||
                    (block.BlockerUserId == friendUserId && block.BlockedUserId == userId),
                cancellationToken);
    }

    public async Task<ChatHistoryResponse?> GetHistoryAsync(
        Guid userId,
        Guid friendUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await CanChatAsync(userId, friendUserId, cancellationToken))
        {
            return null;
        }

        var messages = await dbContext.FriendMessages
            .AsNoTracking()
            .Where(message =>
                (message.SenderUserId == userId && message.RecipientUserId == friendUserId) ||
                (message.SenderUserId == friendUserId && message.RecipientUserId == userId))
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = messages.Count > pageSize;
        var pageMessages = messages
            .Take(pageSize)
            .Reverse()
            .Select(ToResponse)
            .ToArray();

        return new ChatHistoryResponse(pageMessages, page, pageSize, hasMore);
    }

    public async Task<SendChatMessageResult> SendMessageAsync(
        Guid userId,
        Guid friendUserId,
        string? content,
        CancellationToken cancellationToken)
    {
        var messageContent = content?.Trim();
        if (string.IsNullOrEmpty(messageContent) || messageContent.Length > MaximumMessageLength)
        {
            return new SendChatMessageResult(SendChatMessageStatus.InvalidContent, null);
        }

        if (!await CanChatAsync(userId, friendUserId, cancellationToken))
        {
            return new SendChatMessageResult(SendChatMessageStatus.NotFriends, null);
        }

        var message = new FriendMessage
        {
            SenderUserId = userId,
            RecipientUserId = friendUserId,
            Content = messageContent,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.FriendMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SendChatMessageResult(SendChatMessageStatus.Sent, ToResponse(message));
    }

    private static ChatMessageResponse ToResponse(FriendMessage message) =>
        new(
            message.Id,
            message.SenderUserId,
            message.RecipientUserId,
            message.Content,
            new DateTimeOffset(DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc)));

    private static (Guid Lower, Guid Upper) OrderIds(Guid first, Guid second) =>
        first.CompareTo(second) < 0 ? (first, second) : (second, first);
}
