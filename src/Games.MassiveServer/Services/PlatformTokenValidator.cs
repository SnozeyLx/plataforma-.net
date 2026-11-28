using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Games.MassiveServer.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Games.MassiveServer.Services;

/// <summary>Validates platform bearer tokens locally when JWT keys are configured, or via the authentication API.</summary>
public sealed class PlatformTokenValidator : IPlatformTokenValidator
{
    private readonly HttpClient _httpClient;
    private readonly TokenValidationParameters? _localValidationParameters;

    /// <summary>Creates a platform token validator.</summary>
    public PlatformTokenValidator(HttpClient httpClient, IOptions<GameAuthOptions> options)
    {
        _httpClient = httpClient;
        var authOptions = options.Value;
        if (!string.IsNullOrWhiteSpace(authOptions.JwtSigningKeyBase64))
        {
            var signingKey = new SymmetricSecurityKey(Convert.FromBase64String(authOptions.JwtSigningKeyBase64));
            _localValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = authOptions.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = authOptions.JwtAudience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        }
    }

    /// <inheritdoc />
    public async Task<ValidatedPlayer?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (_localValidationParameters is not null)
        {
            return ValidateLocally(accessToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        if (profile is null || profile.UserId == Guid.Empty)
        {
            throw new InvalidDataException("The authentication API returned an invalid player profile.");
        }

        return new ValidatedPlayer(profile.UserId, profile.PlayerName);
    }

    /// <inheritdoc />
    public async Task<bool> HasEntitlementAsync(
        string accessToken,
        string gameSlug,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/games/{Uri.EscapeDataString(gameSlug)}/entitlement");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private ValidatedPlayer? ValidateLocally(string accessToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(accessToken, _localValidationParameters!, out _);
            var subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
            {
                return null;
            }

            var playerName = principal.FindFirstValue("player_name");
            return new ValidatedPlayer(
                userId,
                string.IsNullOrWhiteSpace(playerName) ? null : playerName);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record CurrentUserResponse(Guid UserId, string Email, string? PlayerName);
}
