using Plataforma.AuthApi.Contracts;
using Plataforma.Database.Identity;

namespace Plataforma.AuthApi.Services;

/// <summary>
/// Issues, rotates, and revokes platform session tokens.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates an access token and a persisted refresh token for a user.
    /// </summary>
    /// <param name="user">The authenticated account.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The access and refresh token pair.</returns>
    Task<AuthTokensResponse> CreateTokenPairAsync(
        ApplicationUser user,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rotates an active refresh token and returns a replacement token pair.
    /// </summary>
    /// <param name="refreshToken">The opaque refresh token supplied by the client.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A replacement token pair, or <see langword="null"/> for an invalid token.</returns>
    Task<AuthTokensResponse?> RotateRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the refresh token and all active tokens in its rotation family.
    /// </summary>
    /// <param name="refreshToken">The opaque refresh token supplied by the client.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task that completes when revocation has been processed.</returns>
    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);
}
