namespace Games.MassiveServer.Services;

/// <summary>Validates platform access tokens locally or through the central authentication API.</summary>
public interface IPlatformTokenValidator
{
    /// <summary>Validates an access token and returns the authenticated player identity.</summary>
    Task<ValidatedPlayer?> ValidateAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>Verifies whether the authenticated player owns the specified published game.</summary>
    Task<bool> HasEntitlementAsync(
        string accessToken,
        string gameSlug,
        CancellationToken cancellationToken);
}

/// <summary>Represents the validated identity extracted from a platform access token.</summary>
public sealed record ValidatedPlayer(Guid UserId, string? PlayerName = null);
