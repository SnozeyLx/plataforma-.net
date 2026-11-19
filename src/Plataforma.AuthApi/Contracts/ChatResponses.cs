namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// A text message in a direct conversation between friends.
/// </summary>
public sealed record ChatMessageResponse(
    Guid MessageId,
    Guid SenderUserId,
    Guid RecipientUserId,
    string Content,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// A page of messages from a direct conversation.
/// </summary>
public sealed record ChatHistoryResponse(
    IReadOnlyList<ChatMessageResponse> Messages,
    int Page,
    int PageSize,
    bool HasMore);
