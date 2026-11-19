namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Describes a game in the authenticated player's library.
/// </summary>
public sealed record GameLibraryItemResponse(
    string Slug,
    string Title,
    string? Description,
    bool IsOwned,
    DateTimeOffset? GrantedAtUtc,
    bool WindowsBuildAvailable,
    bool WebGlBuildAvailable,
    bool LauncherDownloadAvailable);

/// <summary>
/// Contains a short-lived URL for an owned game's published build.
/// </summary>
public sealed record GameBuildAccessResponse(
    string GameSlug,
    string Platform,
    string Url,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Contains a short-lived URL for downloading the platform launcher.
/// </summary>
public sealed record LauncherDownloadAccessResponse(
    string Url,
    DateTimeOffset ExpiresAtUtc);
