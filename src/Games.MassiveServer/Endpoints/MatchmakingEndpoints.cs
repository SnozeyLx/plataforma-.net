using System.Net.Http.Headers;
using Games.MassiveServer.Contracts;
using Games.MassiveServer.Services;
using Microsoft.AspNetCore.Http.HttpResults;

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
            .Produces<MatchmakingTicketResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{ticketId:guid}", GetTicketAsync)
            .WithName("GetMatchmakingTicket")
            .WithSummary("Get queue or match status")
            .Produces<MatchmakingTicketResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{ticketId:guid}", CancelTicketAsync)
            .WithName("CancelMatchmakingTicket")
            .WithSummary("Cancel a queue or unconsumed match ticket")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<Results<Accepted<MatchmakingTicketResponse>, UnauthorizedHttpResult>> EnqueueAsync(
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        CancellationToken cancellationToken)
    {
        var userId = await GetUserIdAsync(context, tokenValidator, cancellationToken);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var ticket = matchmakingService.Enqueue(userId.Value);
        return TypedResults.Accepted($"/api/matchmaking/tickets/{ticket.TicketId}", ticket);
    }

    private static async Task<Results<Ok<MatchmakingTicketResponse>, UnauthorizedHttpResult, NotFound>> GetTicketAsync(
        Guid ticketId,
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        CancellationToken cancellationToken)
    {
        var userId = await GetUserIdAsync(context, tokenValidator, cancellationToken);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var ticket = matchmakingService.GetTicket(userId.Value, ticketId);
        return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(ticket);
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound>> CancelTicketAsync(
        Guid ticketId,
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        IMatchmakingService matchmakingService,
        CancellationToken cancellationToken)
    {
        var userId = await GetUserIdAsync(context, tokenValidator, cancellationToken);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        return matchmakingService.Cancel(userId.Value, ticketId)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Guid?> GetUserIdAsync(
        HttpContext context,
        IPlatformTokenValidator tokenValidator,
        CancellationToken cancellationToken)
    {
        if (!AuthenticationHeaderValue.TryParse(
                context.Request.Headers.Authorization.ToString(),
                out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return null;
        }

        return await tokenValidator.ValidateAsync(authorization.Parameter, cancellationToken);
    }
}
