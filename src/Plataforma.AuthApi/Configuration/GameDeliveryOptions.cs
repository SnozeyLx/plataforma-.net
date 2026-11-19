namespace Plataforma.AuthApi.Configuration;

/// <summary>
/// Server-side configuration for protected game builds and sandbox ownership.
/// </summary>
public sealed class GameDeliveryOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "GameDelivery";

    /// <summary>
    /// Gets or sets whether Dash &amp; Crash is granted to every authenticated account for testing.
    /// </summary>
    public bool SandboxTestModeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the public base URL of the Bunny Pull Zone.
    /// </summary>
    public string CdnBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Bunny CDN Token Authentication key.
    /// </summary>
    public string CdnTokenAuthKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lifetime of issued CDN access links in seconds.
    /// </summary>
    public int TokenLifetimeSeconds { get; set; } = 600;

    /// <summary>
    /// Gets or sets the storage object key of the Windows launcher archive.
    /// </summary>
    public string LauncherWindowsObjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets build paths keyed by game slug.
    /// </summary>
    public Dictionary<string, GameBuildOptions> Builds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Describes the published object paths for one game's supported builds.
/// </summary>
public sealed class GameBuildOptions
{
    /// <summary>
    /// Gets or sets the storage object key of the Windows archive.
    /// </summary>
    public string WindowsObjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory key containing the WebGL build.
    /// </summary>
    public string WebGlDirectoryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entry point within the WebGL directory.
    /// </summary>
    public string WebGlEntryPoint { get; set; } = "index.html";
}
