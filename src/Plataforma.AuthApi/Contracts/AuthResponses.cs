namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Public account details returned after registration.
/// </summary>
public sealed record RegisterResponse(Guid UserId, string Email, string PlayerName);

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

/// <summary>
/// Generic response returned after a password reset request.
/// </summary>
public sealed record PasswordResetRequestedResponse(string Message);

/// <summary>
/// Confirmation returned after a password has been reset.
/// </summary>
public sealed record PasswordResetResponse(string Message);
