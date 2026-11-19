using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Plataforma.AuthApi.Services;

namespace Plataforma.AuthApi.Hubs;

/// <summary>
/// Provides real-time direct messaging for confirmed friends.
/// </summary>
[Authorize]
public sealed class FriendChatHub(IFriendChatService chatService) : Hub
{
    public async Task JoinConversation(Guid friendUserId)
    {
        if (!TryGetUserId(out var userId) ||
            !await chatService.CanChatAsync(userId, friendUserId, Context.ConnectionAborted))
        {
            throw new HubException("This conversation is not available.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(userId, friendUserId),
            Context.ConnectionAborted);
    }

    public Task LeaveConversation(Guid friendUserId)
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Authentication is required.");
        }

        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GetConversationGroup(userId, friendUserId),
            Context.ConnectionAborted);
    }

    public async Task SendMessage(Guid friendUserId, string? content)
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Authentication is required.");
        }

        var result = await chatService.SendMessageAsync(
            userId,
            friendUserId,
            content,
            Context.ConnectionAborted);
        if (result.Status != SendChatMessageStatus.Sent || result.Message is null)
        {
            throw new HubException(
                result.Status == SendChatMessageStatus.InvalidContent
                    ? "Messages must contain 1 to 2000 characters."
                    : "This conversation is not available.");
        }

        await Clients.Group(GetConversationGroup(userId, friendUserId))
            .SendAsync("MessageReceived", result.Message, Context.ConnectionAborted);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(
            Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out userId);

    private static string GetConversationGroup(Guid firstUserId, Guid secondUserId)
    {
        var (lowerUserId, upperUserId) = firstUserId.CompareTo(secondUserId) < 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
        return $"friend-chat:{lowerUserId:N}:{upperUserId:N}";
    }
}
