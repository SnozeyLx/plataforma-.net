using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Plataforma.AuthApi.Contracts;
using Plataforma.AuthApi.Services;

namespace Plataforma.AuthApi.Endpoints;

/// <summary>
/// Maps game library, game build, and launcher delivery endpoints.
/// </summary>
public static class GameDeliveryEndpoints
{
    /// <summary>
    /// Adds the authenticated game delivery endpoints to the application.
    /// </summary>
    public static RouteGroupBuilder MapGameDeliveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Game delivery")
            .RequireAuthorization();

        group.MapGet("/library", GetLibraryAsync)
            .WithName("GetPlayerGameLibrary")
            .WithSummary("Get the current player's owned games")
            .WithDescription("Returns persistent entitlements and indicates which builds are configured.")
            .Produces<IReadOnlyList<GameLibraryItemResponse>>();

        group.MapGet("/games/{slug}/access", GetBuildAccessAsync)
            .WithName("GetGameBuildAccess")
            .WithSummary("Get temporary access to an owned game build")
            .WithDescription("Checks the player's entitlement and returns a short-lived Bunny CDN URL.")
            .Produces<GameBuildAccessResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        group.MapGet("/launcher/windows/access", GetLauncherDownloadAccessAsync)
            .WithName("GetWindowsLauncherDownloadAccess")
            .WithSummary("Get temporary access to the Windows platform launcher")
            .WithDescription("Publicly returns a short-lived Bunny CDN URL for the launcher archive; game builds remain entitlement-protected.")
            .Produces<LauncherDownloadAccessResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        return group;
    }

    private static async Task<Results<
        Ok<IReadOnlyList<GameLibraryItemResponse>>,
        UnauthorizedHttpResult>> GetLibraryAsync(
        ClaimsPrincipal principal,
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var library = await gameDeliveryService.GetLibraryAsync(userId, cancellationToken);
        return TypedResults.Ok(library);
    }

    private static async Task<Results<
        Ok<GameBuildAccessResponse>,
        UnauthorizedHttpResult,
        ForbidHttpResult,
        NotFound,
        ProblemHttpResult,
        ValidationProblem>> GetBuildAccessAsync(
        string slug,
        string? platform,
        ClaimsPrincipal principal,
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        if (platform is not ("windows" or "webgl"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["platform"] = ["Platform must be 'windows' or 'webgl'."]
            });
        }

        var result = await gameDeliveryService.GetBuildAccessAsync(
            userId,
            slug,
            platform,
            cancellationToken);

        return result.Status switch
        {
            GameBuildAccessStatus.Available => TypedResults.Ok(result.Access!),
            GameBuildAccessStatus.GameNotFound => TypedResults.NotFound(),
            GameBuildAccessStatus.NotOwned => TypedResults.Forbid(),
            GameBuildAccessStatus.BuildUnavailable => TypedResults.Problem(
                title: "The requested game build is not published.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.Problem(
                title: "Game delivery is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<Results<
        Ok<LauncherDownloadAccessResponse>,
        ProblemHttpResult>> GetLauncherDownloadAccessAsync(
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        var result = await gameDeliveryService.GetLauncherDownloadAccessAsync(cancellationToken);
        return result.Status switch
        {
            LauncherDownloadAccessStatus.Available => TypedResults.Ok(result.Access!),
            LauncherDownloadAccessStatus.BuildUnavailable => TypedResults.Problem(
                title: "The platform launcher is not published.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.Problem(
                title: "Launcher delivery is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
