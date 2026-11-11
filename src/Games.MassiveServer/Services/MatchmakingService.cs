using System.Security.Cryptography;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Services;

/// <summary>Provides single-process matchmaking and validates allocated UDP connection tickets.</summary>
public sealed class MatchmakingService(
    IOptions<MatchmakingOptions> matchmakingOptions,
    IOptions<GameServerOptions> gameServerOptions,
    TimeProvider timeProvider) : IMatchmakingService
{
    private static readonly TimeSpan TicketRetention = TimeSpan.FromMinutes(10);
    private readonly object _sync = new();
    private readonly MatchmakingOptions _matchmakingOptions = matchmakingOptions.Value;
    private readonly GameServerOptions _gameServerOptions = gameServerOptions.Value;
    private readonly Dictionary<Guid, QueueTicket> _tickets = [];
    private readonly Dictionary<string, QueueTicket> _connectionTickets = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Match> _matches = [];
    private readonly List<Guid> _queue = [];

    /// <inheritdoc />
    public MatchmakingTicketResponse Enqueue(Guid userId)
    {
        lock (_sync)
        {
            var activeTicket = _tickets.Values.FirstOrDefault(ticket =>
                ticket.UserId == userId
                && ticket.Status is TicketStatus.Queued or TicketStatus.Ready or TicketStatus.Connected);
            if (activeTicket is not null)
            {
                return ToResponse(activeTicket);
            }

            var now = timeProvider.GetUtcNow();
            var ticket = new QueueTicket(userId, Guid.NewGuid(), now);
            _tickets.Add(ticket.TicketId, ticket);
            _queue.Add(ticket.TicketId);
            return ToResponse(ticket);
        }
    }

    /// <inheritdoc />
    public MatchmakingTicketResponse? GetTicket(Guid userId, Guid ticketId)
    {
        lock (_sync)
        {
            return _tickets.TryGetValue(ticketId, out var ticket) && ticket.UserId == userId
                ? ToResponse(ticket)
                : null;
        }
    }

    /// <inheritdoc />
    public bool Cancel(Guid userId, Guid ticketId)
    {
        lock (_sync)
        {
            if (!_tickets.TryGetValue(ticketId, out var ticket) || ticket.UserId != userId)
            {
                return false;
            }

            if (ticket.Status == TicketStatus.Queued)
            {
                _queue.Remove(ticketId);
                ticket.Status = TicketStatus.Cancelled;
                return true;
            }

            if (ticket.Status != TicketStatus.Ready || ticket.ConnectionTicket is null)
            {
                return false;
            }

            _connectionTickets.Remove(ticket.ConnectionTicket);
            if (ticket.MatchId is Guid matchId && _matches.TryGetValue(matchId, out var match))
            {
                match.UserIds.Remove(userId);
                match.Tickets.Remove(userId);
                if (match.UserIds.Count == 0)
                {
                    _matches.Remove(matchId);
                }
            }

            ticket.ConnectionTicket = null;
            ticket.Status = TicketStatus.Cancelled;
            return true;
        }
    }

    /// <inheritdoc />
    public void ProcessQueue()
    {
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            ExpireTickets(now);

            var waitingTickets = _queue
                .Select(ticketId => _tickets[ticketId])
                .Where(ticket => ticket.Status == TicketStatus.Queued)
                .ToArray();

            while (waitingTickets.Length >= _matchmakingOptions.MinPlayers)
            {
                var matchTickets = waitingTickets
                    .Take(_matchmakingOptions.MaxPlayers)
                    .ToArray();
                if (matchTickets.Length < _matchmakingOptions.MinPlayers)
                {
                    break;
                }

                var match = new Match(Guid.NewGuid());
                _matches.Add(match.MatchId, match);
                foreach (var ticket in matchTickets)
                {
                    ticket.Status = TicketStatus.Ready;
                    ticket.MatchId = match.MatchId;
                    ticket.ConnectionTicket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                    ticket.ConnectionExpiresAt = now.AddSeconds(
                        _matchmakingOptions.ConnectionTicketLifetimeSeconds);
                    match.UserIds.Add(ticket.UserId);
                    match.Tickets.Add(ticket.UserId, ticket);
                    _connectionTickets.Add(ticket.ConnectionTicket, ticket);
                    _queue.Remove(ticket.TicketId);
                }

                waitingTickets = waitingTickets.Skip(matchTickets.Length).ToArray();
            }

            foreach (var ticket in _queue
                .Select(ticketId => _tickets[ticketId])
                .Where(ticket => ticket.Status == TicketStatus.Queued)
                .ToArray())
            {
                if (ticket.CreatedAt.AddSeconds(_matchmakingOptions.QueueTimeoutSeconds) <= now)
                {
                    ticket.Status = TicketStatus.Expired;
                    _queue.Remove(ticket.TicketId);
                }
            }

            foreach (var ticketId in _tickets.Values
                .Where(ticket =>
                    (ticket.Status is TicketStatus.Completed or TicketStatus.Cancelled or TicketStatus.Expired)
                    && ticket.CreatedAt.Add(TicketRetention) <= now)
                .Select(ticket => ticket.TicketId)
                .ToArray())
            {
                _tickets.Remove(ticketId);
            }
        }
    }

    /// <inheritdoc />
    public bool TryConsumeConnectionTicket(string ticket, out PlayerConnection connection)
    {
        lock (_sync)
        {
            if (_connectionTickets.Remove(ticket, out var queueTicket)
                && queueTicket.Status == TicketStatus.Ready
                && queueTicket.ConnectionExpiresAt > timeProvider.GetUtcNow()
                && queueTicket.MatchId is Guid matchId
                && _matches.TryGetValue(matchId, out var match)
                && match.UserIds.Contains(queueTicket.UserId)
                && match.ConnectedUserIds.Add(queueTicket.UserId))
            {
                queueTicket.ConnectionTicket = null;
                queueTicket.Status = TicketStatus.Connected;
                connection = new PlayerConnection(queueTicket.UserId, matchId);
                return true;
            }

            connection = default!;
            return false;
        }
    }

    /// <inheritdoc />
    public void PlayerDisconnected(PlayerConnection connection)
    {
        lock (_sync)
        {
            if (_matches.TryGetValue(connection.MatchId, out var match))
            {
                match.ConnectedUserIds.Remove(connection.UserId);
                match.UserIds.Remove(connection.UserId);
                if (match.Tickets.TryGetValue(connection.UserId, out var ticket))
                {
                    ticket.Status = TicketStatus.Completed;
                }

                if (match.UserIds.Count == 0)
                {
                    _matches.Remove(connection.MatchId);
                }
            }
        }
    }

    private void ExpireTickets(DateTimeOffset now)
    {
        foreach (var ticket in _tickets.Values.Where(ticket =>
            ticket.Status == TicketStatus.Ready && ticket.ConnectionExpiresAt <= now))
        {
            if (ticket.ConnectionTicket is not null)
            {
                _connectionTickets.Remove(ticket.ConnectionTicket);
                ticket.ConnectionTicket = null;
            }

            if (ticket.MatchId is Guid matchId && _matches.TryGetValue(matchId, out var match))
            {
                match.UserIds.Remove(ticket.UserId);
            }

            ticket.Status = TicketStatus.Expired;
        }

        foreach (var matchId in _matches
            .Where(entry => entry.Value.UserIds.Count == 0)
            .Select(entry => entry.Key)
            .ToArray())
        {
            _matches.Remove(matchId);
        }
    }

    private MatchmakingTicketResponse ToResponse(QueueTicket ticket) =>
        new(
            ticket.TicketId,
            _matchmakingOptions.GameCode,
            ticket.Status.ToString().ToLowerInvariant(),
            ticket.CreatedAt,
            ticket.MatchId,
            ticket.Status == TicketStatus.Ready ? _gameServerOptions.PublicAddress : null,
            ticket.Status == TicketStatus.Ready ? _gameServerOptions.UdpPort : null,
            ticket.Status == TicketStatus.Ready ? ticket.ConnectionTicket : null);

    private sealed class QueueTicket(Guid userId, Guid ticketId, DateTimeOffset createdAt)
    {
        public Guid UserId { get; } = userId;

        public Guid TicketId { get; } = ticketId;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public TicketStatus Status { get; set; } = TicketStatus.Queued;

        public Guid? MatchId { get; set; }

        public string? ConnectionTicket { get; set; }

        public DateTimeOffset ConnectionExpiresAt { get; set; }
    }

    private sealed class Match(Guid matchId)
    {
        public Guid MatchId { get; } = matchId;

        public HashSet<Guid> UserIds { get; } = [];

        public HashSet<Guid> ConnectedUserIds { get; } = [];

        public Dictionary<Guid, QueueTicket> Tickets { get; } = [];
    }

    private enum TicketStatus
    {
        Queued,
        Ready,
        Connected,
        Completed,
        Cancelled,
        Expired
    }
}
