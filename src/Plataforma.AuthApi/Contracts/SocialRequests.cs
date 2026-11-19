using System.ComponentModel.DataAnnotations;

namespace Plataforma.AuthApi.Contracts;

/// <summary>
/// Sets the public gamertag for the authenticated account.
/// </summary>
public sealed record SetPlayerNameRequest
{
    [Required, StringLength(24, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_]+$")]
    public required string PlayerName { get; init; }
}

/// <summary>
/// Updates editable public profile preferences for the authenticated account.
/// </summary>
public sealed record UpdatePlayerProfileRequest
{
    [Required, StringLength(24, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_]+$")]
    public required string PlayerName { get; init; }

    [Range(1, 8)]
    public required int ProfileIconId { get; init; }
}

/// <summary>
/// Identifies the account that should receive a friend request or block.
/// </summary>
public sealed record TargetPlayerRequest
{
    [Required]
    public required Guid TargetUserId { get; init; }
}
