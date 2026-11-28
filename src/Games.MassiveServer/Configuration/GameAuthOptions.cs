using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Configuration;

/// <summary>Configuration for validating platform access tokens and game entitlements.</summary>
public sealed class GameAuthOptions
{
    /// <summary>The configuration section containing platform authentication settings.</summary>
    public const string SectionName = "GameAuth";

    /// <summary>Gets or sets the internal base URL of the platform authentication API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Gets or sets the timeout for platform token validation requests.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Gets or sets the optional JWT issuer for local in-process token validation.</summary>
    public string? JwtIssuer { get; set; }

    /// <summary>Gets or sets the optional JWT audience for local in-process token validation.</summary>
    public string? JwtAudience { get; set; }

    /// <summary>Gets or sets the optional Base64 HMAC signing key for local JWT validation.</summary>
    public string? JwtSigningKeyBase64 { get; set; }

    /// <summary>Gets or sets an optional published game slug required to enqueue for matchmaking.</summary>
    public string? RequiredGameSlug { get; set; }
}

/// <summary>Validates platform authentication settings.</summary>
public sealed class GameAuthOptionsValidator : IValidateOptions<GameAuthOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GameAuthOptions options)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            errors.Add("GameAuth:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (options.TimeoutSeconds is < 1 or > 30)
        {
            errors.Add("GameAuth:TimeoutSeconds must be between 1 and 30.");
        }

        if (!string.IsNullOrWhiteSpace(options.JwtSigningKeyBase64))
        {
            if (string.IsNullOrWhiteSpace(options.JwtIssuer) || string.IsNullOrWhiteSpace(options.JwtAudience))
            {
                errors.Add("GameAuth:JwtIssuer and GameAuth:JwtAudience are required when JwtSigningKeyBase64 is set.");
            }

            try
            {
                if (Convert.FromBase64String(options.JwtSigningKeyBase64).Length < 32)
                {
                    errors.Add("GameAuth:JwtSigningKeyBase64 must encode at least 32 bytes.");
                }
            }
            catch (FormatException)
            {
                errors.Add("GameAuth:JwtSigningKeyBase64 must be a valid Base64 string.");
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
