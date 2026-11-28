using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.AuthApi.Configuration;
using Plataforma.AuthApi.Contracts;
using Plataforma.Database;
using Plataforma.Database.Entities;

namespace Plataforma.AuthApi.Services;

/// <inheritdoc />
public sealed class GameDeliveryService(
    PlatformDbContext database,
    IOptions<GameDeliveryOptions> configuredOptions,
    TimeProvider timeProvider,
    ILogger<GameDeliveryService> logger) : IGameDeliveryService
{
    private const string SandboxGameSlug = "dash-and-crash";
    private const string SandboxGrantType = "Sandbox";
    private readonly GameDeliveryOptions options = configuredOptions.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GameLibraryItemResponse>> GetLibraryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (options.SandboxTestModeEnabled)
        {
            await EnsureSandboxEntitlementAsync(userId, cancellationToken);
        }

        var ownedGames = await (
            from entitlement in database.GameEntitlements.AsNoTracking()
            join game in database.Games.AsNoTracking()
                on entitlement.GameId equals game.Id
            where entitlement.UserId == userId && game.Status == GameStatus.Published
            orderby game.Name
            select new
            {
                game.Slug,
                game.Name,
                game.Description,
                entitlement.GrantedAtUtc
            }).ToListAsync(cancellationToken);

        var cdnReady = IsCdnDeliveryConfigured();
        return ownedGames.Select(game =>
        {
            var build = GetBuildOptions(game.Slug);
            return new GameLibraryItemResponse(
                game.Slug,
                game.Name,
                game.Description,
                true,
                new DateTimeOffset(DateTime.SpecifyKind(game.GrantedAtUtc, DateTimeKind.Utc)),
                cdnReady && IsBuildConfigured(build?.WindowsObjectPath),
                cdnReady && IsWebGlBuildConfigured(build),
                cdnReady && IsBuildConfigured(options.LauncherWindowsObjectPath));
        }).ToArray();
    }

    /// <inheritdoc />
    public async Task<GameEntitlementCheckStatus> CheckEntitlementAsync(
        Guid userId,
        string gameSlug,
        CancellationToken cancellationToken)
    {
        var game = await FindPublishedGameAsync(gameSlug, cancellationToken);
        if (game is null)
        {
            return GameEntitlementCheckStatus.GameNotFound;
        }

        return await EnsureAndCheckOwnershipAsync(userId, game.Value.Id, game.Value.Slug, cancellationToken)
            ? GameEntitlementCheckStatus.Owned
            : GameEntitlementCheckStatus.NotOwned;
    }

    /// <inheritdoc />
    public async Task<GameBuildAccessResult> GetBuildAccessAsync(
        Guid userId,
        string gameSlug,
        string platform,
        CancellationToken cancellationToken)
    {
        var game = await FindPublishedGameAsync(gameSlug, cancellationToken);
        if (game is null)
        {
            return new(GameBuildAccessStatus.GameNotFound);
        }

        if (!await EnsureAndCheckOwnershipAsync(userId, game.Value.Id, game.Value.Slug, cancellationToken))
        {
            return new(GameBuildAccessStatus.NotOwned);
        }

        var build = GetBuildOptions(game.Value.Slug);
        if (build is null)
        {
            return new(GameBuildAccessStatus.BuildUnavailable);
        }

        var isWebGl = string.Equals(platform, "webgl", StringComparison.OrdinalIgnoreCase);
        var objectPath = isWebGl ? build.WebGlDirectoryPath : build.WindowsObjectPath;
        if (!IsBuildConfigured(objectPath)
            || (isWebGl && !IsSafeObjectPath(build.WebGlEntryPoint)))
        {
            return new(GameBuildAccessStatus.BuildUnavailable);
        }

        var normalizedObjectPath = objectPath!.Trim('/');
        var fullPath = isWebGl
            ? $"{normalizedObjectPath}/{build.WebGlEntryPoint.Trim('/')}"
            : normalizedObjectPath;
        if (!TryCreateSignedUrl(
                fullPath,
                isWebGl ? $"/{normalizedObjectPath}/" : null,
                out var signedUrl,
                out var expiresAt))
        {
            return new(GameBuildAccessStatus.ConfigurationUnavailable);
        }

        return new(GameBuildAccessStatus.Available, new GameBuildAccessResponse(
            game.Value.Slug,
            isWebGl ? "webgl" : "windows",
            signedUrl,
            expiresAt));
    }

    private async Task<(Guid Id, string Slug)?> FindPublishedGameAsync(
        string gameSlug,
        CancellationToken cancellationToken)
    {
        var game = await database.Games.AsNoTracking()
            .Where(item => item.Slug == gameSlug && item.Status == GameStatus.Published)
            .Select(item => new { item.Id, item.Slug })
            .SingleOrDefaultAsync(cancellationToken);

        return game is null ? null : (game.Id, game.Slug);
    }

    private async Task<bool> EnsureAndCheckOwnershipAsync(
        Guid userId,
        Guid gameId,
        string gameSlug,
        CancellationToken cancellationToken)
    {
        if (options.SandboxTestModeEnabled
            && string.Equals(gameSlug, SandboxGameSlug, StringComparison.Ordinal))
        {
            await EnsureSandboxEntitlementAsync(userId, cancellationToken);
        }

        return await database.GameEntitlements.AsNoTracking()
            .AnyAsync(
                entitlement => entitlement.UserId == userId && entitlement.GameId == gameId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<LauncherDownloadAccessResult> GetLauncherDownloadAccessAsync(
        string platform,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var objectPath = platform switch
        {
            "windows" or "win-x64" => options.LauncherWindowsObjectPath,
            "macos-x64" or "osx-x64" => options.LauncherMacOsX64ObjectPath,
            "macos-arm64" or "osx-arm64" => options.LauncherMacOsArm64ObjectPath,
            "linux-x64" => options.LauncherLinuxX64ObjectPath,
            _ => null
        };
        if (objectPath is null)
        {
            return Task.FromResult(
                new LauncherDownloadAccessResult(LauncherDownloadAccessStatus.UnsupportedPlatform));
        }

        if (!IsBuildConfigured(objectPath))
        {
            return Task.FromResult(
                new LauncherDownloadAccessResult(LauncherDownloadAccessStatus.BuildUnavailable));
        }

        if (!TryCreateSignedUrl(
                objectPath.Trim('/'),
                tokenPath: null,
                out var signedUrl,
                out var expiresAt))
        {
            return Task.FromResult(
                new LauncherDownloadAccessResult(LauncherDownloadAccessStatus.ConfigurationUnavailable));
        }

        return Task.FromResult(new LauncherDownloadAccessResult(
            LauncherDownloadAccessStatus.Available,
            new LauncherDownloadAccessResponse(signedUrl, expiresAt)));
    }

    /// <inheritdoc />
    public Task<LauncherUpdateAssetAccessResult> GetLauncherUpdateAssetAccessAsync(
        string channel,
        string assetName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupportedLauncherChannel(channel) || !IsSafeLauncherUpdateAssetName(channel, assetName))
        {
            return Task.FromResult(
                new LauncherUpdateAssetAccessResult(LauncherUpdateAssetAccessStatus.InvalidAsset));
        }

        if (!IsBuildConfigured(options.LauncherUpdatesDirectoryPath))
        {
            return Task.FromResult(
                new LauncherUpdateAssetAccessResult(LauncherUpdateAssetAccessStatus.BuildUnavailable));
        }

        var objectPath =
            $"{options.LauncherUpdatesDirectoryPath.Trim('/')}/{channel}/{assetName}";
        if (!TryCreateSignedUrl(
                objectPath,
                tokenPath: null,
                out var signedUrl,
                out _,
                options.LauncherUpdateTokenLifetimeSeconds))
        {
            return Task.FromResult(
                new LauncherUpdateAssetAccessResult(
                    LauncherUpdateAssetAccessStatus.ConfigurationUnavailable));
        }

        return Task.FromResult(new LauncherUpdateAssetAccessResult(
            LauncherUpdateAssetAccessStatus.Available,
            signedUrl));
    }

    private async Task EnsureSandboxEntitlementAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var gameId = await database.Games.AsNoTracking()
            .Where(game => game.Slug == SandboxGameSlug && game.Status == GameStatus.Published)
            .Select(game => (Guid?)game.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"The published sandbox game '{SandboxGameSlug}' is missing from the catalog.");

        var key = new object[] { userId, gameId };
        if (await database.GameEntitlements.FindAsync(key, cancellationToken) is not null)
        {
            return;
        }

        database.GameEntitlements.Add(new GameEntitlement
        {
            UserId = userId,
            GameId = gameId,
            GrantType = SandboxGrantType,
            GrantedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            database.ChangeTracker.Clear();
            var concurrentlyGranted = await database.GameEntitlements.AsNoTracking()
                .AnyAsync(
                    entitlement => entitlement.UserId == userId && entitlement.GameId == gameId,
                    cancellationToken);
            if (!concurrentlyGranted)
            {
                throw;
            }

            logger.LogDebug(
                exception,
                "Sandbox game entitlement was concurrently granted for user {UserId}.",
                userId);
        }
    }

    private GameBuildOptions? GetBuildOptions(string gameSlug) =>
        options.Builds.TryGetValue(gameSlug, out var build) ? build : null;

    private bool IsWebGlBuildConfigured(GameBuildOptions? build) =>
        build is not null
        && IsBuildConfigured(build.WebGlDirectoryPath)
        && IsSafeObjectPath(build.WebGlEntryPoint);

    private bool IsBuildConfigured(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsSafeObjectPath(path);

    private bool IsCdnDeliveryConfigured() =>
        TryGetCdnBaseUri(out _)
        && !string.IsNullOrWhiteSpace(options.CdnTokenAuthKey)
        && options.TokenLifetimeSeconds is >= 60 and <= 3600;

    private static bool IsSupportedLauncherChannel(string channel) =>
        channel is "win-x64" or "osx-x64" or "osx-arm64" or "linux-x64";

    private static bool IsSafeLauncherUpdateAssetName(string channel, string assetName)
    {
        if (assetName == $"releases.{channel}.json"
            || assetName == $"assets.{channel}.json"
            || assetName == $"RELEASES-{channel}"
            || (channel == "win-x64" && assetName == "RELEASES"))
        {
            return true;
        }

        return assetName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            && assetName.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+')
            && !assetName.Contains("..", StringComparison.Ordinal);
    }

    private bool TryCreateSignedUrl(
        string objectPath,
        string? tokenPath,
        out string signedUrl,
        out DateTimeOffset expiresAt,
        int? tokenLifetimeSeconds = null)
    {
        signedUrl = string.Empty;
        expiresAt = default;
        var tokenLifetime = tokenLifetimeSeconds ?? options.TokenLifetimeSeconds;
        if (!TryGetCdnBaseUri(out var cdnBaseUri)
            || string.IsNullOrWhiteSpace(options.CdnTokenAuthKey)
            || tokenLifetime is < 60 or > 3600
            || !TryCreateCdnUri(cdnBaseUri, objectPath, out var resourceUri))
        {
            return false;
        }

        expiresAt = timeProvider.GetUtcNow().AddSeconds(tokenLifetime);
        signedUrl = SignUrl(options.CdnTokenAuthKey, resourceUri, expiresAt, tokenPath);
        return true;
    }

    private bool TryGetCdnBaseUri(out Uri baseUri)
    {
        baseUri = null!;
        if (!Uri.TryCreate(options.CdnBaseUrl, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.AbsolutePath != "/")
        {
            return false;
        }

        baseUri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }

    private static bool IsSafeObjectPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.Contains('\\')
        && !path.Contains('?')
        && !path.Contains('#')
        && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    private static bool TryCreateCdnUri(Uri baseUri, string objectPath, out Uri uri)
    {
        var escapedPath = string.Join(
            "/",
            objectPath.Split('/').Select(Uri.EscapeDataString));
        return Uri.TryCreate(baseUri, escapedPath, out uri!);
    }

    private static string SignUrl(
        string securityKey,
        Uri resourceUri,
        DateTimeOffset expiresAt,
        string? tokenPath)
    {
        var expires = expiresAt.ToUnixTimeSeconds().ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var tokenPathParameter = tokenPath is null
            ? string.Empty
            : $"token_path={tokenPath}";
        var signingData = tokenPathParameter;
        var message = $"{tokenPath ?? resourceUri.AbsolutePath}{expires}{signingData}";
        var keyBytes = Encoding.UTF8.GetBytes(securityKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var signature = HMACSHA256.HashData(keyBytes, messageBytes);
        var token = Convert.ToBase64String(signature)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        if (tokenPath is null)
        {
            return $"{resourceUri.GetLeftPart(UriPartial.Path)}?token=HS256-{token}&expires={expires}";
        }

        return $"{resourceUri.GetLeftPart(UriPartial.Authority)}/bcdn_token=HS256-{token}"
            + $"&token_path={Uri.EscapeDataString(tokenPath)}&expires={expires}"
            + resourceUri.AbsolutePath;
    }
}
