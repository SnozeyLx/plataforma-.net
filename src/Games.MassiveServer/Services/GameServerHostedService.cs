using System.Text;
using Games.MassiveServer.Configuration;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Services;

/// <summary>Hosts the UDP listener and accepts only allocated, one-time matchmaking tickets.</summary>
public sealed class GameServerHostedService : BackgroundService
{
    private const byte ReliableChannel = 0;
    private const int MaximumConnectionPayloadBytes = 128;
    private readonly EventBasedNetListener _listener = new();
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

        peer.Tag = playerConnection;
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if (peer.Tag is PlayerConnection connection)
        {
            _logger.LogInformation(
                "Player {UserId} connected to match {MatchId}.",
                connection.UserId,
                connection.MatchId);
        }
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer.Tag is PlayerConnection connection)
        {
            _matchmakingService.PlayerDisconnected(connection);
            _logger.LogInformation(
                "Player {UserId} disconnected from match {MatchId} ({Reason}).",
                connection.UserId,
                connection.MatchId,
                disconnectInfo.Reason);
        }
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            try
            {
                if (reader.AvailableBytes > 16 || reader.GetString() != "ping")
                {
                    peer.Disconnect();
                    return;
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or ArgumentOutOfRangeException
                    or IndexOutOfRangeException)
            {
                _logger.LogDebug(exception, "Disconnected a peer that sent a malformed packet.");
                peer.Disconnect();
                return;
            }

            var response = new NetDataWriter();
            response.Put("pong");
            peer.Send(response, ReliableChannel, DeliveryMethod.ReliableOrdered);
        }
        finally
        {
            reader.Recycle();
        }
    }
}
