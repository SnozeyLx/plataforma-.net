namespace Plataforma.AuthApi.Configuration;

/// <summary>
/// Public settings used to build password reset links.
/// </summary>
public sealed class PasswordResetOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// Gets or sets the absolute HTTPS URL of the portal reset page.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;
}
