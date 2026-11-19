using Plataforma.AuthApi.Contracts;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Reads and sends messages between confirmed friends.
/// </summary>
public interface IFriendChatService
{
    Task<bool> CanChatAsync(
        Guid userId,
        Guid friendUserId,
        CancellationToken cancellationToken);

    Task<ChatHistoryResponse?> GetHistoryAsync(
        Guid userId,
        Guid friendUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SendChatMessageResult> SendMessageAsync(
        Guid userId,
        Guid friendUserId,
        string? content,
        CancellationToken cancellationToken);
}

public enum SendChatMessageStatus
{
    Sent,
    NotFriends,
    InvalidContent
}

public sealed record SendChatMessageResult(
    SendChatMessageStatus Status,
    ChatMessageResponse? Message);
