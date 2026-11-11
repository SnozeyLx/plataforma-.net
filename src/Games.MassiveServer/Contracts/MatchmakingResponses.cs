namespace Games.MassiveServer.Contracts;

/// <summary>Represents a player's matchmaking queue ticket and connection details.</summary>
public sealed record MatchmakingTicketResponse(
    Guid TicketId,
    string GameCode,
    string Status,
    DateTimeOffset CreatedAt,
    Guid? MatchId,
    string? ServerAddress,
    int? ServerPort,
    string? ConnectionTicket);
