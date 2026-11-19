using System.ComponentModel.DataAnnotations;

namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Credentials used to register a platform account.
/// </summary>
public sealed record RegisterRequest(
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, MinLength(12), MaxLength(128)] string Password,
    [property: Required, StringLength(24, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_]+$")]
    string PlayerName);

/// <summary>
/// Credentials used to authenticate a platform account.
/// </summary>
public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Required, MaxLength(128)] string Password);

/// <summary>
/// A refresh token submitted to rotate or revoke a session.
/// </summary>
public sealed record RefreshTokenRequest(
    [property: Required, StringLength(256, MinimumLength = 32)] string RefreshToken);
