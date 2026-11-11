using Microsoft.Extensions.Options;

namespace Plataforma.AuthApi.Configuration;

/// <summary>
/// Validates the cryptographic and lifetime settings for JWTs.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKeyBase64))
        {
            failures.Add("Jwt:SigningKeyBase64 is required.");
        }
        else
        {
            try
            {
                if (Convert.FromBase64String(options.SigningKeyBase64).Length < 32)
                {
                    failures.Add("Jwt:SigningKeyBase64 must encode at least 32 bytes.");
                }
            }
            catch (FormatException)
            {
                failures.Add("Jwt:SigningKeyBase64 must be a valid Base64 value.");
            }
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            failures.Add("Jwt:AccessTokenMinutes must be between 1 and 60.");
        }

        if (options.RefreshTokenDays is < 1 or > 90)
        {
            failures.Add("Jwt:RefreshTokenDays must be between 1 and 90.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
