using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Configuration;

/// <summary>Configuration for validating platform access tokens.</summary>
public sealed class GameAuthOptions
{
    /// <summary>The configuration section containing platform authentication settings.</summary>
    public const string SectionName = "GameAuth";

    /// <summary>Gets or sets the internal base URL of the platform authentication API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Gets or sets the timeout for platform token validation requests.</summary>
    public int TimeoutSeconds { get; set; } = 5;
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

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
