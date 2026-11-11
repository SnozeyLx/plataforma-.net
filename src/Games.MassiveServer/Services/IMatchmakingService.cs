using Games.MassiveServer.Contracts;

namespace Games.MassiveServer.Services;

/// <summary>Manages the in-memory matchmaking queue and short-lived connection tickets.</summary>
public interface IMatchmakingService
{
    /// <summary>Creates or returns the active queue ticket for a player.</summary>
    MatchmakingTicketResponse Enqueue(Guid userId);

    /// <summary>Returns a player's queue ticket when it belongs to that player.</summary>
    MatchmakingTicketResponse? GetTicket(Guid userId, Guid ticketId);

    /// <summary>Cancels a queued or not-yet-consumed ticket owned by the player.</summary>
    bool Cancel(Guid userId, Guid ticketId);

    /// <summary>Forms matches and expires queue and connection tickets.</summary>
    void ProcessQueue();

    /// <summary>Consumes a one-time ticket for an assigned player connection.</summary>
    bool TryConsumeConnectionTicket(string ticket, out PlayerConnection connection);

    /// <summary>Marks a player as disconnected from the given match.</summary>
    void PlayerDisconnected(PlayerConnection connection);
}

/// <summary>Identifies the authorized player and match associated with a UDP peer.</summary>
public sealed record PlayerConnection(Guid UserId, Guid MatchId);
