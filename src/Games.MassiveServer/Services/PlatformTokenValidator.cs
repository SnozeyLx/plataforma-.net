using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Games.MassiveServer.Services;

/// <summary>Validates bearer tokens by calling the platform's authenticated profile endpoint.</summary>
public sealed class PlatformTokenValidator(HttpClient httpClient) : IPlatformTokenValidator
{
    /// <inheritdoc />
    public async Task<Guid?> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
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

        return profile.UserId;
    }

    private sealed record CurrentUserResponse(Guid UserId, string Email);
}
