using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Plataforma.AuthApi.Contracts;
using Plataforma.AuthApi.Services;

namespace Plataforma.AuthApi.Endpoints;

/// <summary>
/// Maps persistent history endpoints for direct friend conversations.
/// </summary>
public static class FriendChatEndpoints
{
    /// <summary>
    /// Adds authenticated conversation history endpoints.
    /// </summary>
    public static RouteGroupBuilder MapFriendChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chat")
            .WithTags("Friend chat")
            .RequireAuthorization();

        group.MapGet(
                "/conversations/{friendUserId:guid}/messages",
                GetMessagesAsync)
            .WithName("GetFriendChatHistory")
            .WithSummary("Get a page of messages with a friend")
            .WithDescription("Only confirmed friends can read direct conversation history.")
            .Produces<ChatHistoryResponse>();

        return group;
    }

    private static async Task<Results<
        Ok<ChatHistoryResponse>,
        UnauthorizedHttpResult,
        NotFound,
        ValidationProblem>> GetMessagesAsync(
        Guid friendUserId,
        ClaimsPrincipal principal,
        IFriendChatService chatService,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (page is < 1 or > 10_000 || pageSize is < 1 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pagination"] = ["Page must be 1–10000 and pageSize must be 1–100."]
            });
        }

        var history = await chatService.GetHistoryAsync(
            userId,
            friendUserId,
            page,
            pageSize,
            cancellationToken);
        return history is null ? TypedResults.NotFound() : TypedResults.Ok(history);
    }
}
