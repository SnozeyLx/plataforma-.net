using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Plataforma.AuthApi.Contracts;
using Plataforma.AuthApi.Services;

namespace Plataforma.AuthApi.Endpoints;

/// <summary>
/// Maps friend discovery, requests, and blocking endpoints.
/// </summary>
public static partial class FriendEndpoints
{
    private static readonly Regex PlayerNamePattern = new("^[a-zA-Z0-9_]{3,24}$");

    /// <summary>
    /// Adds the authenticated friend endpoints to the application.
    /// </summary>
    public static RouteGroupBuilder MapFriendEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/friends")
            .WithTags("Friends")
            .RequireAuthorization()
            .RequireRateLimiting("standard");

        group.MapGet("", GetFriendsAsync)
            .WithName("GetFriends")
            .WithSummary("List confirmed friends")
            .Produces<IReadOnlyList<FriendResponse>>();

        group.MapGet("/search", SearchAsync)
            .WithName("SearchPlayers")
            .WithSummary("Search players by public gamertag")
            .WithDescription("Searches a prefix of at least three characters and never searches by email.")
            .Produces<IReadOnlyList<PlayerSearchResponse>>();

        group.MapGet("/requests/incoming", GetIncomingRequestsAsync)
            .WithName("GetIncomingFriendRequests")
            .WithSummary("List incoming friend requests")
            .Produces<IReadOnlyList<FriendRequestResponse>>();

        group.MapGet("/requests/outgoing", GetOutgoingRequestsAsync)
            .WithName("GetOutgoingFriendRequests")
            .WithSummary("List outgoing friend requests")
            .Produces<IReadOnlyList<FriendRequestResponse>>();

        group.MapPost("/requests", SendRequestAsync)
            .WithName("SendFriendRequest")
            .WithSummary("Send a friend request")
            .Accepts<TargetPlayerRequest>("application/json")
            .Produces<FriendRequestResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/requests/{requestId:guid}", GetRequestAsync)
            .WithName("GetFriendRequest")
            .WithSummary("Get a friend request")
            .Produces<FriendRequestResponse>();

        group.MapPost("/requests/{requestId:guid}/accept", AcceptRequestAsync)
            .WithName("AcceptFriendRequest")
            .WithSummary("Accept an incoming friend request")
            .Produces<FriendRequestResponse>();

        group.MapPost("/requests/{requestId:guid}/decline", DeclineRequestAsync)
            .WithName("DeclineFriendRequest")
            .WithSummary("Decline an incoming friend request")
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/requests/{requestId:guid}", CancelRequestAsync)
            .WithName("CancelFriendRequest")
            .WithSummary("Cancel an outgoing friend request")
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/{friendUserId:guid}", RemoveFriendAsync)
            .WithName("RemoveFriend")
            .WithSummary("Remove a confirmed friend")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/blocks", GetBlockedPlayersAsync)
            .WithName("GetBlockedPlayers")
            .WithSummary("List blocked accounts")
            .Produces<IReadOnlyList<BlockedPlayerResponse>>();

        group.MapPut("/blocks/{targetUserId:guid}", BlockPlayerAsync)
            .WithName("BlockPlayer")
            .WithSummary("Block a player and remove any friendship")
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/blocks/{targetUserId:guid}", UnblockPlayerAsync)
            .WithName("UnblockPlayer")
            .WithSummary("Unblock a player")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<FriendResponse>>, UnauthorizedHttpResult>> GetFriendsAsync(
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await friendService.GetFriendsAsync(userId, cancellationToken));
    }

    private static async Task<Results<
        Ok<IReadOnlyList<PlayerSearchResponse>>,
        UnauthorizedHttpResult,
        ValidationProblem>> SearchAsync(
        string? q,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var prefix = q?.Trim();
        if (string.IsNullOrWhiteSpace(prefix) || !PlayerNamePattern.IsMatch(prefix))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["q"] = ["Use de 3 a 24 letras, números ou underscores para buscar."]
            });
        }

        return TypedResults.Ok(await friendService.SearchAsync(userId, prefix, cancellationToken));
    }

    private static async Task<Results<
        Ok<IReadOnlyList<FriendRequestResponse>>,
        UnauthorizedHttpResult>> GetIncomingRequestsAsync(
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await friendService.GetRequestsAsync(userId, outgoing: false, cancellationToken));
    }

    private static async Task<Results<
        Ok<IReadOnlyList<FriendRequestResponse>>,
        UnauthorizedHttpResult>> GetOutgoingRequestsAsync(
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await friendService.GetRequestsAsync(userId, outgoing: true, cancellationToken));
    }

    private static async Task<IResult> SendRequestAsync(
        TargetPlayerRequest? request,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (request is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A JSON request body is required."]
            });
        }

        var validationErrors = EndpointRequestValidation.Validate(request);
        if (request.TargetUserId == Guid.Empty)
        {
            validationErrors[nameof(request.TargetUserId)] = ["Choose a valid player."];
        }

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var result = await friendService.SendRequestAsync(userId, request.TargetUserId, cancellationToken);
        return result.Status switch
        {
            SendFriendRequestStatus.Created => TypedResults.Created(
                $"/api/friends/requests/{result.Request!.RequestId:D}",
                result.Request),
            SendFriendRequestStatus.TargetNotFound => TypedResults.NotFound(),
            SendFriendRequestStatus.CannotRequestSelf => TypedResults.Problem(
                title: "You cannot send a friend request to yourself.",
                statusCode: StatusCodes.Status400BadRequest),
            SendFriendRequestStatus.Blocked => TypedResults.Problem(
                title: "This friend request cannot be sent.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(
                title: "A friend request or friendship already exists.",
                statusCode: StatusCodes.Status409Conflict)
        };
    }

    private static async Task<Results<
        Ok<FriendRequestResponse>,
        UnauthorizedHttpResult,
        NotFound>> GetRequestAsync(
        Guid requestId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var request = await friendService.GetRequestAsync(userId, requestId, cancellationToken);
        return request is null ? TypedResults.NotFound() : TypedResults.Ok(request);
    }

    private static async Task<Results<
        Ok<FriendRequestResponse>,
        UnauthorizedHttpResult,
        NotFound>> AcceptRequestAsync(
        Guid requestId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var request = await friendService.AcceptRequestAsync(userId, requestId, cancellationToken);
        return request is null ? TypedResults.NotFound() : TypedResults.Ok(request);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> DeclineRequestAsync(
        Guid requestId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await friendService.DeclineRequestAsync(userId, requestId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> CancelRequestAsync(
        Guid requestId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await friendService.CancelRequestAsync(userId, requestId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> RemoveFriendAsync(
        Guid friendUserId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await friendService.RemoveFriendAsync(userId, friendUserId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<
        Ok<IReadOnlyList<BlockedPlayerResponse>>,
        UnauthorizedHttpResult>> GetBlockedPlayersAsync(
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await friendService.GetBlockedPlayersAsync(userId, cancellationToken));
    }

    private static async Task<Results<
        NoContent,
        UnauthorizedHttpResult,
        NotFound,
        Conflict<ProblemDetails>,
        BadRequest<ProblemDetails>>> BlockPlayerAsync(
        Guid targetUserId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await friendService.BlockPlayerAsync(userId, targetUserId, cancellationToken);
        return result.Status switch
        {
            BlockPlayerStatus.Blocked => TypedResults.NoContent(),
            BlockPlayerStatus.TargetNotFound => TypedResults.NotFound(),
            BlockPlayerStatus.CannotBlockSelf => TypedResults.BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "You cannot block yourself."
            }),
            _ => TypedResults.NotFound()
        };
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> UnblockPlayerAsync(
        Guid targetUserId,
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        return await friendService.UnblockPlayerAsync(userId, targetUserId, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
