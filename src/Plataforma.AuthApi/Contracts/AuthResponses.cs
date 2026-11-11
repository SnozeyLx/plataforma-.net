namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Public account details returned after registration.
/// </summary>
public sealed record RegisterResponse(Guid UserId, string Email);

/// <summary>
/// Access and refresh credentials for a platform session.
/// </summary>
public sealed record AuthTokensResponse(
    string TokenType,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>
/// Public details for the authenticated account.
/// </summary>
public sealed record CurrentUserResponse(Guid UserId, string Email);
