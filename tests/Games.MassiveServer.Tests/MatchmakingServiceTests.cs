using Games.MassiveServer.Configuration;
using Games.MassiveServer.Hosting;
using Games.MassiveServer.Services;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Tests;

[TestClass]
public sealed class MatchmakingServiceTests
{
    [TestMethod]
    public void ProcessQueue_EnoughPlayers_AssignsMatchAndConsumesTicketOnce()
    {
        var (service, _) = CreateService(minPlayers: 2, maxPlayers: 2);
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        var firstTicket = service.Enqueue(firstUserId);
        var duplicateTicket = service.Enqueue(firstUserId);
        var secondTicket = service.Enqueue(secondUserId);

        service.ProcessQueue();

        var firstMatchTicket = service.GetTicket(firstUserId, firstTicket.TicketId);
        var secondMatchTicket = service.GetTicket(secondUserId, secondTicket.TicketId);
        Assert.IsNotNull(firstMatchTicket);
        Assert.IsNotNull(secondMatchTicket);
        Assert.AreEqual(firstTicket.TicketId, duplicateTicket.TicketId);
        Assert.AreEqual("ready", firstMatchTicket.Status);
        Assert.AreEqual(firstMatchTicket.MatchId, secondMatchTicket.MatchId);
        Assert.IsNotNull(firstMatchTicket.ConnectionTicket);

        Assert.IsTrue(service.TryConsumeConnectionTicket(
            firstMatchTicket.ConnectionTicket,
            out var playerConnection));
        Assert.AreEqual(firstUserId, playerConnection.UserId);
        Assert.IsFalse(service.TryConsumeConnectionTicket(
            firstMatchTicket.ConnectionTicket,
            out _));
        Assert.AreEqual("connected", service.GetTicket(firstUserId, firstTicket.TicketId)?.Status);

        service.PlayerDisconnected(playerConnection);
        Assert.AreNotEqual(firstTicket.TicketId, service.Enqueue(firstUserId).TicketId);
    }

    [TestMethod]
    public void ProcessQueue_MassivePreset_StartsWithOnePlayer()
    {
        var (service, _) = CreateService(
            minPlayers: GameServerPresets.Massive.MinPlayers,
            maxPlayers: GameServerPresets.Massive.MaxPlayers);
        var userId = Guid.NewGuid();
        var ticket = service.Enqueue(userId);

        service.ProcessQueue();

        Assert.AreEqual("ready", service.GetTicket(userId, ticket.TicketId)?.Status);
    }

    [TestMethod]
    public void Enqueue_PersistentWorld_AssignsPlayersToSameMatchUntilFull()
    {
        var (service, _) = CreateService(
            minPlayers: 1,
            maxPlayers: 2,
            persistentWorld: true);
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var thirdUserId = Guid.NewGuid();

        var firstTicket = service.Enqueue(firstUserId);
        var secondTicket = service.Enqueue(secondUserId);
        var thirdTicket = service.Enqueue(thirdUserId);

        var firstReady = service.GetTicket(firstUserId, firstTicket.TicketId);
        var secondReady = service.GetTicket(secondUserId, secondTicket.TicketId);
        var thirdReady = service.GetTicket(thirdUserId, thirdTicket.TicketId);
        Assert.AreEqual("ready", firstReady?.Status);
        Assert.AreEqual(firstReady?.MatchId, secondReady?.MatchId);
        Assert.AreNotEqual(firstReady?.MatchId, thirdReady?.MatchId);
    }

    [TestMethod]
    public void ProcessQueue_MoreThanFiftyPlayers_CreatesSeparateMatchesWithinCapacity()
    {
        var (service, _) = CreateService(
            minPlayers: GameServerPresets.Massive.MinPlayers,
            maxPlayers: GameServerPresets.Massive.MaxPlayers);
        var players = Enumerable.Range(0, GameServerPresets.Massive.MaxPlayers + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var tickets = players
            .Select(userId => (UserId: userId, Ticket: service.Enqueue(userId)))
            .ToArray();

        service.ProcessQueue();

        var readyTickets = tickets
            .Select(player => service.GetTicket(player.UserId, player.Ticket.TicketId))
            .ToArray();
        Assert.IsTrue(readyTickets.All(ticket => ticket?.Status == "ready"));
        var playersPerMatch = readyTickets
            .GroupBy(ticket => ticket?.MatchId)
            .Select(match => match.Count())
            .OrderByDescending(playerCount => playerCount)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { GameServerPresets.Massive.MaxPlayers, 1 },
            playersPerMatch);
    }

    [TestMethod]
    public void ProcessQueue_CoopPreset_GroupsUpToFourPlayersDuringStartDelay()
    {
        var (service, timeProvider) = CreateService(
            minPlayers: GameServerPresets.Coop.MinPlayers,
            maxPlayers: GameServerPresets.Coop.MaxPlayers,
            matchStartDelaySeconds: GameServerPresets.Coop.MatchStartDelaySeconds);
        var players = Enumerable.Range(0, GameServerPresets.Coop.MaxPlayers)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var tickets = players
            .Select(userId => (UserId: userId, Ticket: service.Enqueue(userId)))
            .ToArray();

        service.ProcessQueue();
        Assert.IsTrue(tickets.All(player =>
            service.GetTicket(player.UserId, player.Ticket.TicketId)?.Status == "queued"));

        timeProvider.Advance(TimeSpan.FromSeconds(GameServerPresets.Coop.MatchStartDelaySeconds));
        service.Enqueue(Guid.NewGuid());
        service.ProcessQueue();

        var readyTickets = tickets
            .Select(player => service.GetTicket(player.UserId, player.Ticket.TicketId))
            .ToArray();
        Assert.IsTrue(readyTickets.All(ticket => ticket?.Status == "ready"));
        Assert.AreEqual(1, readyTickets.Select(ticket => ticket?.MatchId).Distinct().Count());
    }

    [TestMethod]
    public void ProcessQueue_CoopPreset_HonorsMinimumOfTwoWhenConfigured()
    {
        var (service, _) = CreateService(minPlayers: 2, maxPlayers: GameServerPresets.Coop.MaxPlayers);
        var firstUserId = Guid.NewGuid();
        var firstTicket = service.Enqueue(firstUserId);

        service.ProcessQueue();

        Assert.AreEqual("queued", service.GetTicket(firstUserId, firstTicket.TicketId)?.Status);
    }

    [TestMethod]
    public void ProcessQueue_QueueTimesOut_ExpiresTicket()
    {
        var (service, timeProvider) = CreateService(
            minPlayers: 2,
            maxPlayers: 2,
            queueTimeoutSeconds: 10);
        var userId = Guid.NewGuid();
        var ticket = service.Enqueue(userId);

        timeProvider.Advance(TimeSpan.FromSeconds(11));
        service.ProcessQueue();

        Assert.AreEqual("expired", service.GetTicket(userId, ticket.TicketId)?.Status);
    }

    [TestMethod]
    public void ProcessQueue_ExceedsMatchCapacity_LeavesRemainderQueued()
    {
        var (service, _) = CreateService(minPlayers: 2, maxPlayers: 2);
        var userIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var tickets = userIds
            .Select(userId => (UserId: userId, Ticket: service.Enqueue(userId)))
            .ToArray();

        service.ProcessQueue();

        var statuses = tickets
            .Select(player => service.GetTicket(player.UserId, player.Ticket.TicketId)?.Status)
            .ToArray();
        Assert.AreEqual(2, statuses.Count(status => status == "ready"));
        Assert.AreEqual(1, statuses.Count(status => status == "queued"));
    }

    [TestMethod]
    public void Cancel_TicketOwnedByDifferentPlayer_ReturnsFalse()
    {
        var (service, _) = CreateService(minPlayers: 2, maxPlayers: 2);
        var userId = Guid.NewGuid();
        var ticket = service.Enqueue(userId);

        Assert.IsFalse(service.Cancel(Guid.NewGuid(), ticket.TicketId));
        Assert.AreEqual("queued", service.GetTicket(userId, ticket.TicketId)?.Status);
    }

    private static (MatchmakingService Service, AdjustableTimeProvider TimeProvider) CreateService(
        int minPlayers,
        int maxPlayers,
        int queueTimeoutSeconds = 60,
        int matchStartDelaySeconds = 0,
        bool persistentWorld = false)
    {
        var timeProvider = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var matchmakingOptions = Options.Create(new MatchmakingOptions
        {
            MinPlayers = minPlayers,
            MaxPlayers = maxPlayers,
            QueueTimeoutSeconds = queueTimeoutSeconds,
            MatchStartDelaySeconds = matchStartDelaySeconds,
            PersistentWorld = persistentWorld
        });
        var gameServerOptions = Options.Create(new GameServerOptions
        {
            UdpPort = 9050,
            PublicAddress = "127.0.0.1"
        });

        return (
            new MatchmakingService(matchmakingOptions, gameServerOptions, timeProvider),
            timeProvider);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
