using Plataforma.AuthApi.Contracts;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Provides game library, build, and launcher delivery access.
/// </summary>
public interface IGameDeliveryService
{
    /// <summary>
    /// Gets the current player's owned games, granting the sandbox game when enabled.
    /// </summary>
    Task<IReadOnlyList<GameLibraryItemResponse>> GetLibraryAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a short-lived build URL after verifying the player's entitlement.
    /// </summary>
    Task<GameBuildAccessResult> GetBuildAccessAsync(
        Guid userId,
        string gameSlug,
        string platform,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies whether the player owns a published game without generating a CDN URL.
    /// </summary>
    Task<GameEntitlementCheckStatus> CheckEntitlementAsync(
        Guid userId,
        string gameSlug,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a short-lived URL for a supported launcher installer.
    /// </summary>
    Task<LauncherDownloadAccessResult> GetLauncherDownloadAccessAsync(
        string platform,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a short-lived URL for an update asset in a supported release channel.
    /// </summary>
    Task<LauncherUpdateAssetAccessResult> GetLauncherUpdateAssetAccessAsync(
        string channel,
        string assetName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes the outcome of a build access request.
/// </summary>
public sealed record GameBuildAccessResult(
    GameBuildAccessStatus Status,
    GameBuildAccessResponse? Access = null);

/// <summary>
/// Describes the outcome of a launcher download access request.
/// </summary>
public sealed record LauncherDownloadAccessResult(
    LauncherDownloadAccessStatus Status,
    LauncherDownloadAccessResponse? Access = null);

/// <summary>
/// Describes the outcome of a launcher update asset access request.
/// </summary>
public sealed record LauncherUpdateAssetAccessResult(
    LauncherUpdateAssetAccessStatus Status,
    string? Url = null);

/// <summary>
/// Lists the possible results of a launcher download access request.
/// </summary>
public enum LauncherDownloadAccessStatus
{
    /// <summary>
    /// A signed URL was created.
    /// </summary>
    Available,

    /// <summary>
    /// The launcher installer is not configured.
    /// </summary>
    BuildUnavailable,

    /// <summary>
    /// CDN delivery configuration is incomplete or invalid.
    /// </summary>
    ConfigurationUnavailable,

    /// <summary>
    /// The requested launcher platform is not supported.
    /// </summary>
    UnsupportedPlatform
}

/// <summary>
/// Lists the possible results of a launcher update asset access request.
/// </summary>
public enum LauncherUpdateAssetAccessStatus
{
    /// <summary>
    /// A signed URL was created.
    /// </summary>
    Available,

    /// <summary>
    /// The release channel or asset name is not allowed.
    /// </summary>
    InvalidAsset,

    /// <summary>
    /// The launcher update asset directory is not configured.
    /// </summary>
    BuildUnavailable,

    /// <summary>
    /// CDN delivery configuration is incomplete or invalid.
    /// </summary>
    ConfigurationUnavailable
}

/// <summary>
/// Lists the possible results of a build access request.
/// </summary>
public enum GameBuildAccessStatus
{
    /// <summary>
    /// A signed URL was created.
    /// </summary>
    Available,

    /// <summary>
    /// The game is not present in the catalog.
    /// </summary>
    GameNotFound,

    /// <summary>
    /// The player does not own the game.
    /// </summary>
    NotOwned,

    /// <summary>
    /// The requested build is not configured.
    /// </summary>
    BuildUnavailable,

    /// <summary>
    /// CDN delivery configuration is incomplete or invalid.
    /// </summary>
    ConfigurationUnavailable
}

/// <summary>
/// Lists the possible results of an entitlement verification request.
/// </summary>
public enum GameEntitlementCheckStatus
{
    /// <summary>
    /// The player owns the published game.
    /// </summary>
    Owned,

    /// <summary>
    /// The game is not present or not published in the catalog.
    /// </summary>
    GameNotFound,

    /// <summary>
    /// The player does not own the game.
    /// </summary>
    NotOwned
}
