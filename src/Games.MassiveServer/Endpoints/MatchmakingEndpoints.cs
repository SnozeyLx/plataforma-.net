using System.Net.Http.Headers;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Contracts;
using Games.MassiveServer.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Endpoints;

/// <summary>Maps authenticated matchmaking queue endpoints.</summary>
public static class MatchmakingEndpoints
{
    /// <summary>Adds matchmaking endpoints to the application.</summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <returns>The route group containing matchmaking endpoints.</returns>
    public static RouteGroupBuilder MapMatchmakingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/matchmaking/tickets")
            .WithTags("Matchmaking");

        group.MapPost("/", EnqueueAsync)
            .WithName("EnqueueForMatchmaking")
            .WithSummary("Join the game queue")
            .WithDescription("Validates the platform access token and returns a queue ticket.")
            .RequireRateLimiting("matchmaking-write")
            .Produces<MatchmakingTicketResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{ticketId:guid}", GetTicketAsync)
            .WithName("GetMatchmakingTicket")
            .WithSummary("Get queue or match status")
            .RequireRateLimiting("matchmaking-read")
            .Produces<MatchmakingTicketResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{ticketId:guid}", CancelTicketAsync)
            .WithName("CancelMatchmakingTicket")
            .WithSummary("Cancel a queue or unconsumed match ticket")
            .RequireRateLimiting("matchmaking-write")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Results<Accepted<MatchmakingTicketResponse>, UnauthorizedHttpResult, ForbidHttpResult>> EnqueueAsync(
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        IOptions<GameAuthOptions> gameAuthOptions,
        CancellationToken cancellationToken)
    {
        var bearerToken = ExtractBearerToken(context);
        if (bearerToken is null)
        {
            return TypedResults.Unauthorized();
        }

        var player = await tokenValidator.ValidateAsync(bearerToken, cancellationToken);
        if (player is null)
        {
            return TypedResults.Unauthorized();
        }

        var requiredSlug = gameAuthOptions.Value.RequiredGameSlug;
        if (!string.IsNullOrWhiteSpace(requiredSlug)
            && !await tokenValidator.HasEntitlementAsync(bearerToken, requiredSlug.Trim(), cancellationToken))
        {
            return TypedResults.Forbid();
        }

        var ticket = matchmakingService.Enqueue(player.UserId, player.PlayerName);
        return TypedResults.Accepted($"/api/matchmaking/tickets/{ticket.TicketId}", ticket);
    }

    private static async Task<Results<Ok<MatchmakingTicketResponse>, UnauthorizedHttpResult, NotFound>> GetTicketAsync(
        Guid ticketId,
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        CancellationToken cancellationToken)
    {
        var player = await GetPlayerAsync(context, tokenValidator, cancellationToken);
        if (player is null)
        {
            return TypedResults.Unauthorized();
        }

        var ticket = matchmakingService.GetTicket(player.UserId, ticketId);
        return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> CancelTicketAsync(
        Guid ticketId,
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        CancellationToken cancellationToken)
    {
        var player = await GetPlayerAsync(context, tokenValidator, cancellationToken);
        if (player is null)
        {
            return TypedResults.Unauthorized();
        }

        return matchmakingService.Cancel(player.UserId, ticketId)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<ValidatedPlayer?> GetPlayerAsync(
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        CancellationToken cancellationToken)
    {
        var bearerToken = ExtractBearerToken(context);
        return bearerToken is null
            ? null
            : await tokenValidator.ValidateAsync(bearerToken, cancellationToken);
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        if (!AuthenticationHeaderValue.TryParse(
                context.Request.Headers.Authorization.ToString(),
                out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return null;
        }

        return authorization.Parameter;
    }
}
