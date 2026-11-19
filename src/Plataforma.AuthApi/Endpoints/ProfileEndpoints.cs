using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Plataforma.AuthApi.Contracts;
using Plataforma.AuthApi.Services;

namespace Plataforma.AuthApi.Endpoints;

/// <summary>
/// Maps public profile endpoints.
/// </summary>
public static class ProfileEndpoints
{
    /// <summary>
    /// Adds the authenticated profile endpoints to the application.
    /// </summary>
    public static RouteGroupBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/profile")
            .WithTags("Player profile")
            .RequireAuthorization();

        group.MapGet("", GetProfileAsync)
            .WithName("GetPlayerProfile")
            .WithSummary("Get the current player's public profile")
            .Produces<PlayerProfileResponse>();

        group.MapPut("/player-name", SetPlayerNameAsync)
            .WithName("SetPlayerName")
            .WithSummary("Set or update the current player's gamertag")
            .WithDescription("Gamertags are unique and searchable. Email addresses are never used for player discovery.")
            .Accepts<SetPlayerNameRequest>("application/json")
            .Produces<PlayerProfileResponse>();

        group.MapPut("", UpdateProfileAsync)
            .WithName("UpdatePlayerProfile")
            .WithSummary("Update the current player's profile")
            .WithDescription("Updates the gamertag and selected template profile icon.")
            .Accepts<UpdatePlayerProfileRequest>("application/json")
            .Produces<PlayerProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static async Task<Results<Ok<PlayerProfileResponse>, UnauthorizedHttpResult, NotFound>> GetProfileAsync(
        ClaimsPrincipal principal,
        IFriendService friendService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var profile = await friendService.GetProfileAsync(userId, cancellationToken);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<Results<
        Ok<PlayerProfileResponse>,
        UnauthorizedHttpResult,
        NotFound,
        Conflict<ProblemDetails>,
        ValidationProblem>> SetPlayerNameAsync(
        SetPlayerNameRequest? request,
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
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var result = await friendService.SetPlayerNameAsync(
            userId,
            request.PlayerName.Trim(),
            cancellationToken);

        return result.Status switch
        {
            SetPlayerNameStatus.Updated => TypedResults.Ok(result.Profile!),
            SetPlayerNameStatus.Taken => TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This gamertag is already in use."
            }),
            _ => TypedResults.NotFound()
        };
    }

    private static async Task<Results<
        Ok<PlayerProfileResponse>,
        UnauthorizedHttpResult,
        NotFound,
        Conflict<ProblemDetails>,
        ValidationProblem>> UpdateProfileAsync(
        UpdatePlayerProfileRequest? request,
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
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var profile = await friendService.UpdateProfileAsync(
            userId,
            request.PlayerName.Trim(),
            request.ProfileIconId,
            cancellationToken);

        if (profile is null)
        {
            if (!await friendService.IsPlayerNameTakenAsync(
                FriendService.NormalizePlayerName(request.PlayerName.Trim()),
                userId,
                cancellationToken))
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This gamertag is already in use."
            });
        }

        return TypedResults.Ok(profile);
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
