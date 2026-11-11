namespace Games.MassiveServer.Services;

/// <summary>Validates platform access tokens through the central authentication API.</summary>
public interface IPlatformTokenValidator
{
    /// <summary>Validates an access token and returns its authenticated player ID.</summary>
    /// <param name="accessToken">The bearer token supplied by the game client.</param>
    /// <param name="cancellationToken">A token that cancels the HTTP request.</param>
    /// <returns>The player ID, or <see langword="null"/> when the token is invalid.</returns>
    Task<Guid?> ValidateAsync(string accessToken, CancellationToken cancellationToken);
}
