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

        group.MapGet("/games/{slug}/entitlement", CheckEntitlementAsync)
            .WithName("CheckGameEntitlement")
            .WithSummary("Verify whether the player owns a published game")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/launcher/{platform}/access", GetLauncherDownloadAccessAsync)
            .WithName("GetLauncherDownloadAccess")
            .WithSummary("Get temporary access to a platform launcher")
            .WithDescription("Publicly returns a short-lived Bunny CDN URL for a supported launcher installer; game builds remain entitlement-protected.")
            .Produces<LauncherDownloadAccessResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        group.MapGet("/launcher/updates/{channel}/{assetName}", GetLauncherUpdateAssetAsync)
            .WithName("GetLauncherUpdateAsset")
            .WithSummary("Redirect to a launcher update asset")
            .WithDescription("Validates the release channel and asset name, then redirects to a short-lived Bunny CDN URL.")
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status404NotFound)
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
        Ok<GameEntitlementResponse>,
        UnauthorizedHttpResult,
        ForbidHttpResult,
        NotFound>> CheckEntitlementAsync(
        string slug,
        ClaimsPrincipal principal,
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var status = await gameDeliveryService.CheckEntitlementAsync(userId, slug, cancellationToken);
        return status switch
        {
            GameEntitlementCheckStatus.Owned => TypedResults.Ok(new GameEntitlementResponse(slug, true)),
            GameEntitlementCheckStatus.GameNotFound => TypedResults.NotFound(),
            _ => TypedResults.Forbid()
        };
    }

    private sealed record GameEntitlementResponse(string GameSlug, bool Owned);

    private static async Task<Results<
        Ok<LauncherDownloadAccessResponse>,
        ValidationProblem,
        ProblemHttpResult>> GetLauncherDownloadAccessAsync(
        string platform,
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        var result = await gameDeliveryService.GetLauncherDownloadAccessAsync(
            platform,
            cancellationToken);
        return result.Status switch
        {
            LauncherDownloadAccessStatus.Available => TypedResults.Ok(result.Access!),
            LauncherDownloadAccessStatus.UnsupportedPlatform => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["platform"] = ["The requested launcher platform is not supported."]
                }),
            LauncherDownloadAccessStatus.BuildUnavailable => TypedResults.Problem(
                title: "The platform launcher is not published.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.Problem(
                title: "Launcher delivery is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<Results<
        RedirectHttpResult,
        NotFound,
        ProblemHttpResult>> GetLauncherUpdateAssetAsync(
        string channel,
        string assetName,
        IGameDeliveryService gameDeliveryService,
        CancellationToken cancellationToken)
    {
        var result = await gameDeliveryService.GetLauncherUpdateAssetAccessAsync(
            channel,
            assetName,
            cancellationToken);

        return result.Status switch
        {
            LauncherUpdateAssetAccessStatus.Available => TypedResults.Redirect(result.Url!),
            LauncherUpdateAssetAccessStatus.InvalidAsset => TypedResults.NotFound(),
            LauncherUpdateAssetAccessStatus.BuildUnavailable => TypedResults.Problem(
                title: "Launcher update assets are not published.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => TypedResults.Problem(
                title: "Launcher update delivery is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
