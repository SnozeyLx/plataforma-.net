using MimeKit;

namespace Plataforma.AuthApi.Configuration;

/// <summary>
/// SMTP settings used to deliver account emails.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Email";

    /// <summary>
    /// Gets or sets the SMTP host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SMTP port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SMTP password or provider-specific SMTP key.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the verified sender address.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sender display name.
    /// </summary>
    public string FromName { get; set; } = "Plataforma";

    /// <summary>
    /// Gets whether all required SMTP settings are available.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && Port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && MailboxAddress.TryParse(FromAddress, out _);
}
