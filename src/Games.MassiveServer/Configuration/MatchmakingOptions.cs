using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Configuration;

/// <summary>Configuration for the in-memory matchmaking queue.</summary>
public sealed class MatchmakingOptions
{
    /// <summary>The configuration section containing matchmaking settings.</summary>
    public const string SectionName = "Matchmaking";

    /// <summary>Gets or sets the game code served by this process.</summary>
    public string GameCode { get; set; } = "arena-demo";

    /// <summary>Gets or sets the minimum number of players required to start a match.</summary>
    public int MinPlayers { get; set; } = 2;

    /// <summary>Gets or sets the maximum number of players assigned to one match.</summary>
    public int MaxPlayers { get; set; } = 50;

    /// <summary>Gets or sets how long a player may wait in the queue.</summary>
    public int QueueTimeoutSeconds { get; set; } = 60;

    /// <summary>Gets or sets how long a connection ticket remains valid.</summary>
    public int ConnectionTicketLifetimeSeconds { get; set; } = 120;
}

/// <summary>Validates matchmaking settings.</summary>
public sealed class MatchmakingOptionsValidator : IValidateOptions<MatchmakingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MatchmakingOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.GameCode))
        {
            errors.Add("Matchmaking:GameCode is required.");
        }

        if (options.MinPlayers < 1 || options.MaxPlayers < options.MinPlayers || options.MaxPlayers > 50)
        {
            errors.Add("Matchmaking player limits must satisfy 1 <= MinPlayers <= MaxPlayers <= 50.");
        }

        if (options.QueueTimeoutSeconds is < 1 or > 600)
        {
            errors.Add("Matchmaking:QueueTimeoutSeconds must be between 1 and 600.");
        }

        if (options.ConnectionTicketLifetimeSeconds is < 10 or > 600)
        {
            errors.Add("Matchmaking:ConnectionTicketLifetimeSeconds must be between 10 and 600.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
