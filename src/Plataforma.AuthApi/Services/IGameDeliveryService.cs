using Plataforma.AuthApi.Contracts;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Provides authenticated game library and temporary build access.
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
    /// Creates a short-lived URL for the Windows launcher archive.
    /// </summary>
    Task<LauncherDownloadAccessResult> GetLauncherDownloadAccessAsync(
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
/// Lists the possible results of a launcher download access request.
/// </summary>
public enum LauncherDownloadAccessStatus
{
    /// <summary>
    /// A signed URL was created.
    /// </summary>
    Available,

    /// <summary>
    /// The launcher archive is not configured.
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
