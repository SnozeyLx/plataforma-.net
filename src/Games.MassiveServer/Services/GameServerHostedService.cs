using Games.MassiveServer.Configuration;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Services;

/// <summary>Hosts the UDP listener and relays visual player state within each match.</summary>
public sealed class GameServerHostedService : BackgroundService
{
    private const byte ReliableChannel = 0;
    private const byte ProtocolMarker = 0xD5;
    private const byte ClientHello = 1;
    private const byte ClientState = 2;
    private const byte ServerPlayerJoined = 10;
    private const byte ServerPlayerLeft = 11;
    private const byte ServerPlayerState = 12;
    private const byte ServerHandshakeAccepted = 13;
    private const int MaximumConnectionPayloadBytes = 128;
    private readonly EventBasedNetListener _listener = new();
    private readonly List<NetPeer> _connectedPeers = [];
    private readonly NetManager _netManager;
    private readonly IMatchmakingService _matchmakingService;
    private readonly ILogger<GameServerHostedService> _logger;
    private readonly GameServerOptions _options;

    /// <summary>Creates a UDP game server hosted service.</summary>
    public GameServerHostedService(
        IMatchmakingService matchmakingService,
        IOptions<GameServerOptions> options,
        ILogger<GameServerHostedService> logger)
    {
        _matchmakingService = matchmakingService;
        _logger = logger;
        _options = options.Value;
        _netManager = new NetManager(_listener)
        {
            DisconnectTimeout = 15_000,
            UnconnectedMessagesEnabled = false,
            NatPunchEnabled = false
        };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
    }

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_netManager.Start(_options.UdpPort))
        {
            throw new InvalidOperationException($"Could not bind the game server UDP port {_options.UdpPort}.");
        }

        _logger.LogInformation("Game server is listening on UDP port {UdpPort}.", _options.UdpPort);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _netManager.Stop();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _netManager.PollEvents();
            await Task.Delay(TimeSpan.FromMilliseconds(15), stoppingToken);
        }
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        var payload = request.Data;
        if (payload.AvailableBytes is <= 0 or > MaximumConnectionPayloadBytes)
        {
            _logger.LogDebug("Rejected a UDP connection with an invalid ticket payload size.");
            request.RejectForce();
            return;
        }

        string ticket;
        try
        {
            ticket = payload.GetString();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            _logger.LogDebug(exception, "Rejected a malformed UDP connection ticket.");
            request.RejectForce();
            return;
        }

        if (string.IsNullOrWhiteSpace(ticket)
            || !_matchmakingService.TryConsumeConnectionTicket(ticket, out var playerConnection))
        {
            _logger.LogDebug("Rejected a UDP connection without a valid matchmaking ticket.");
            request.RejectForce();
            return;
        }

        var peer = request.Accept();
        if (peer is null)
        {
            _matchmakingService.PlayerDisconnected(playerConnection);
            return;
        }

        peer.Tag = new PlayerSession(playerConnection);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if (peer.Tag is PlayerSession session)
        {
            _logger.LogInformation(
                "Player {UserId} established UDP transport for match {MatchId}.",
                session.Connection.UserId,
                session.Connection.MatchId);
        }
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer.Tag is not PlayerSession session)
        {
            return;
        }

        if (session.HasJoined)
        {
            var message = new NetDataWriter();
            message.Put(ProtocolMarker);
            message.Put(ServerPlayerLeft);
            message.Put(session.Connection.UserId.ToString("N"));
            SendToMatch(session.Connection.MatchId, message, DeliveryMethod.ReliableOrdered, peer);
        }

        _matchmakingService.PlayerDisconnected(session.Connection);
        _logger.LogInformation(
            "Player {UserId} disconnected from match {MatchId} ({Reason}).",
            session.Connection.UserId,
            session.Connection.MatchId,
            disconnectInfo.Reason);
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            if (reader.AvailableBytes is <= 0 or > MaximumConnectionPayloadBytes)
            {
                DisconnectMalformedPeer(peer, $"invalid payload size ({reader.AvailableBytes} bytes)");
                return;
            }

            if (reader.PeekByte() != ProtocolMarker)
            {
                HandleLegacyPing(peer, reader);
                return;
            }

            reader.GetByte();
            switch (reader.GetByte())
            {
                case ClientHello:
                    HandleHello(peer, reader);
                    break;
                case ClientState:
                    HandlePlayerState(peer, reader);
                    break;
                default:
                    DisconnectMalformedPeer(peer, "unknown gameplay message type");
                    break;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException
                or FormatException)
        {
            _logger.LogDebug(exception, "Disconnected a peer that sent a malformed packet.");
            peer.Disconnect();
        }
        finally
        {
            reader.Recycle();
        }
    }

    private void HandleLegacyPing(NetPeer peer, NetPacketReader reader)
    {
        if (reader.GetString() != "ping" || reader.AvailableBytes != 0)
        {
            DisconnectMalformedPeer(peer, "invalid legacy ping packet");
            return;
        }

        var response = new NetDataWriter();
        response.Put("pong");
        peer.Send(response, ReliableChannel, DeliveryMethod.ReliableOrdered);
    }

    private void HandleHello(NetPeer peer, NetPacketReader reader)
    {
        if (reader.AvailableBytes != 0)
        {
            DisconnectMalformedPeer(peer, "hello packet contained unexpected data");
            return;
        }

        if (peer.Tag is not PlayerSession session)
        {
            DisconnectMalformedPeer(peer, "hello packet had no valid connection ticket");
            return;
        }

        if (session.HasJoined)
        {
            DisconnectMalformedPeer(peer, "duplicate hello packet");
            return;
        }

        session.HasJoined = true;
        var acknowledgement = new NetDataWriter();
        acknowledgement.Put(ProtocolMarker);
        acknowledgement.Put(ServerHandshakeAccepted);
        peer.Send(acknowledgement, ReliableChannel, DeliveryMethod.ReliableOrdered);
        _logger.LogInformation(
            "Player {UserId} completed the gameplay handshake for match {MatchId}.",
            session.Connection.UserId,
            session.Connection.MatchId);

        _netManager.GetConnectedPeers(_connectedPeers);
        foreach (var existingPeer in _connectedPeers)
        {
            if (existingPeer == peer
                || existingPeer.Tag is not PlayerSession existing
                || !existing.HasJoined
                || existing.Connection.MatchId != session.Connection.MatchId)
            {
                continue;
            }

            SendPlayerJoined(peer, existing);
            if (existing.State is PlayerState state)
            {
                SendPlayerState(peer, existing.Connection.UserId, state);
            }

            SendPlayerJoined(existingPeer, session);
        }
    }

    private void HandlePlayerState(NetPeer peer, NetPacketReader reader)
    {
        if (peer.Tag is not PlayerSession session)
        {
            DisconnectMalformedPeer(peer, "state packet had no valid connection ticket");
            return;
        }

        if (!session.HasJoined)
        {
            DisconnectMalformedPeer(peer, "state packet arrived before the hello handshake");
            return;
        }

        if (reader.AvailableBytes != 25)
        {
            DisconnectMalformedPeer(peer, $"state packet had an invalid size ({reader.AvailableBytes} bytes)");
            return;
        }

        var state = new PlayerState(
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetFloat(),
            reader.GetByte());

        if (!state.IsValid)
        {
            _logger.LogDebug(
                "Rejected player state values: position=({X}, {Y}, {Z}), yaw={RotationY}, "
                + "RunX={RunX}, RunZ={RunZ}, flags={Flags}.",
                state.X,
                state.Y,
                state.Z,
                state.RotationY,
                state.RunX,
                state.RunZ,
                state.Flags);
            DisconnectMalformedPeer(peer, "state packet contained invalid position or animation values");
            return;
        }

        session.State = state;
        var now = DateTimeOffset.UtcNow;
        if (!session.HasReceivedState)
        {
            session.HasReceivedState = true;
            LogPlayerState(session, state, "first");
            session.NextStateLogAt = now.AddSeconds(1);
        }
        else if (now >= session.NextStateLogAt
            && session.LastLoggedState is PlayerState lastLoggedState
            && HasMeaningfulPositionChange(lastLoggedState, state))
        {
            LogPlayerState(session, state, "movement");
            session.NextStateLogAt = now.AddSeconds(1);
        }

        var message = CreatePlayerStateMessage(session.Connection.UserId, state);
        SendToMatch(session.Connection.MatchId, message, DeliveryMethod.Sequenced, peer);
    }

    private void LogPlayerState(PlayerSession session, PlayerState state, string updateType)
    {
        _logger.LogInformation(
            "Received {UpdateType} visual state from player {UserId} in match {MatchId}: "
            + "position=({X}, {Y}, {Z}), yaw={RotationY}, RunX={RunX}, RunZ={RunZ}, flags={Flags}.",
            updateType,
            session.Connection.UserId,
            session.Connection.MatchId,
            state.X,
            state.Y,
            state.Z,
            state.RotationY,
            state.RunX,
            state.RunZ,
            state.Flags);
        session.LastLoggedState = state;
    }

    private static bool HasMeaningfulPositionChange(PlayerState previous, PlayerState current)
    {
        var deltaX = current.X - previous.X;
        var deltaY = current.Y - previous.Y;
        var deltaZ = current.Z - previous.Z;
        return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ >= 0.01f;
    }

    private void SendPlayerJoined(NetPeer peer, PlayerSession session)
    {
        var message = new NetDataWriter();
        message.Put(ProtocolMarker);
        message.Put(ServerPlayerJoined);
        message.Put(session.Connection.UserId.ToString("N"));
        message.Put(session.DisplayName);
        peer.Send(message, ReliableChannel, DeliveryMethod.ReliableOrdered);
    }

    private void SendPlayerState(NetPeer peer, Guid userId, PlayerState state)
    {
        peer.Send(
            CreatePlayerStateMessage(userId, state),
            ReliableChannel,
            DeliveryMethod.Sequenced);
    }

    private static NetDataWriter CreatePlayerStateMessage(Guid userId, PlayerState state)
    {
        var message = new NetDataWriter();
        message.Put(ProtocolMarker);
        message.Put(ServerPlayerState);
        message.Put(userId.ToString("N"));
        message.Put(state.X);
        message.Put(state.Y);
        message.Put(state.Z);
        message.Put(state.RotationY);
        message.Put(state.RunX);
        message.Put(state.RunZ);
        message.Put(state.Flags);
        return message;
    }

    private void SendToMatch(
        Guid matchId,
        NetDataWriter message,
        DeliveryMethod deliveryMethod,
        NetPeer? excludedPeer = null)
    {
        _netManager.GetConnectedPeers(_connectedPeers);
        foreach (var peer in _connectedPeers)
        {
            if (peer == excludedPeer
                || peer.Tag is not PlayerSession session
                || !session.HasJoined
                || session.Connection.MatchId != matchId)
            {
                continue;
            }

            peer.Send(message, ReliableChannel, deliveryMethod);
        }
    }

    private void DisconnectMalformedPeer(NetPeer peer, string reason)
    {
        _logger.LogDebug("Disconnected a peer because {Reason}.", reason);
        peer.Disconnect();
    }

    private sealed class PlayerSession(PlayerConnection connection)
    {
        public PlayerConnection Connection { get; } = connection;

        public string DisplayName { get; } = $"Player-{connection.UserId.ToString("N")[..6]}";

        public bool HasJoined { get; set; }

        public bool HasReceivedState { get; set; }

        public DateTimeOffset NextStateLogAt { get; set; }

        public PlayerState? LastLoggedState { get; set; }

        public PlayerState? State { get; set; }
    }

    private readonly record struct PlayerState(
        float X,
        float Y,
        float Z,
        float RotationY,
        float RunX,
        float RunZ,
        byte Flags)
    {
        public bool IsValid =>
            float.IsFinite(X)
            && float.IsFinite(Y)
            && float.IsFinite(Z)
            && float.IsFinite(RotationY)
            && float.IsFinite(RunX)
            && float.IsFinite(RunZ)
            && Math.Abs(X) <= 1_000_000
            && Math.Abs(Y) <= 1_000_000
            && Math.Abs(Z) <= 1_000_000
            && Math.Abs(RotationY) <= 36_000
            && Math.Abs(RunX) <= 2.05
            && Math.Abs(RunZ) <= 2.05
            && (Flags & 0xE0) == 0;
    }
}
