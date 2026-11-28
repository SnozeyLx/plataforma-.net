using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Games.MassiveServer.Configuration;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Services;

/// <summary>Hosts the UDP listener, accepts bridged WebSocket peers, enforces transport protections, and relays state within each match.</summary>
public sealed class GameServerHostedService : BackgroundService
{
    private const byte ReliableChannel = 0;
    private const byte ProtocolMarker = 0xD5;
    private const byte ProtocolV2Marker = UdpPacketCipher.ProtocolV2Marker;
    private const byte ClientHello = 1;
    private const byte ClientState = 2;
    private const byte ClientGameData = 3;
    private const byte ServerPlayerJoined = 10;
    private const byte ServerPlayerLeft = 11;
    private const byte ServerPlayerState = 12;
    private const byte ServerHandshakeAccepted = 13;
    private const byte ServerGameData = 14;
    private const byte GameDataScopeBroadcast = 0;
    private const byte GameDataScopeUnicast = 1;
    private const int MaximumConnectionPayloadBytes = 128;
    private const int PlayerStatePayloadBytes = 25;
    private const int MaxTrackedConnectionIps = 10_000;
    private const long MaintenanceIntervalMs = 250;

    private readonly EventBasedNetListener _listener = new();
    private readonly NetDataWriter _v1Writer = new();
    private readonly NetDataWriter _v2PlaintextWriter = new();
    private readonly NetDataReader _decryptedReader = new();
    private readonly NetDataReader _wsReader = new();
    private readonly Dictionary<Guid, MatchRoom> _rooms = [];
    private readonly List<PlayerSession> _pendingHandshakeSessions = [];
    private readonly Dictionary<WebSocketPeerChannel, PlayerSession> _wsSessions = [];
    private readonly ConcurrentQueue<InboundWsEvent> _inboundWsEvents = new();
    private readonly object _ipRateSync = new();
    private readonly Dictionary<IPAddress, IpRateWindow> _connectionAttemptsByIp = [];
    private readonly byte[] _gameDataScratch;
    private readonly byte[] _decryptScratch;
    private readonly byte[] _encryptScratch;
    private readonly byte[] _handshakeScratch = new byte[UdpPacketCipher.HandshakePayloadSizeBytes];
    private readonly int _maxPlaintextPayloadBytes;
    private readonly int _maxReceivePayloadBytes;
    private readonly NetManager _netManager;
    private readonly IMatchmakingService _matchmakingService;
    private readonly ILogger<GameServerHostedService> _logger;
    private readonly GameServerOptions _options;

    /// <summary>Creates a dual-transport UDP/WebSocket game server hosted service.</summary>
    public GameServerHostedService(
        IMatchmakingService matchmakingService,
        IOptions<GameServerOptions> options,
        ILogger<GameServerHostedService> logger)
    {
        _matchmakingService = matchmakingService;
        _logger = logger;
        _options = options.Value;
        _gameDataScratch = new byte[_options.MaximumGameDataBytes];
        _maxPlaintextPayloadBytes = Math.Max(
            MaximumConnectionPayloadBytes,
            64 + _options.MaximumGameDataBytes);
        _maxReceivePayloadBytes = _maxPlaintextPayloadBytes + UdpPacketCipher.HeaderSizeBytes;
        _decryptScratch = new byte[_maxPlaintextPayloadBytes];
        _encryptScratch = new byte[_maxReceivePayloadBytes + 64];

        _netManager = new NetManager(_listener)
        {
            DisconnectTimeout = 15_000,
            UnconnectedMessagesEnabled = false,
            BroadcastReceiveEnabled = false,
            NatPunchEnabled = false
        };

        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
    }

    /// <summary>Gets the maximum allowed connection handshake payload size in bytes.</summary>
    public int MaxConnectionPayloadSize => MaximumConnectionPayloadBytes;

    /// <summary>Gets the maximum allowed inbound gameplay frame size in bytes.</summary>
    public int MaxReceivePayloadSize => _maxReceivePayloadBytes;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_netManager.Start(_options.UdpPort))
        {
            throw new InvalidOperationException($"Could not bind the game server UDP port {_options.UdpPort}.");
        }

        _logger.LogInformation(
            "Game server is listening on UDP port {UdpPort} (tick {TickIntervalMs}ms, relayInterval {StateRelayIntervalMs}ms, encryptedOnly={RequireEncryptedUdp}, wsBridge={EnableWebSocketTransport}).",
            _options.UdpPort,
            _options.TickIntervalMs,
            _options.StateRelayIntervalMs,
            _options.RequireEncryptedUdp,
            _options.EnableWebSocketTransport);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _netManager.Stop();
        DisposeAllSessions();
    }

    /// <summary>Validates a WebSocket connect frame and registers the peer for single-thread relay processing.</summary>
    public bool TryAcceptWebSocketConnection(
        IPAddress remoteAddress,
        ReadOnlySpan<byte> connectPayload,
        out WebSocketPeerChannel channel)
    {
        channel = null!;
        var nowMs = Environment.TickCount64;
        if (!AllowConnectionAttempt(remoteAddress, nowMs))
        {
            return false;
        }

        if (connectPayload.Length is <= 0 or > MaximumConnectionPayloadBytes)
        {
            return false;
        }

        PlayerConnection playerConnection;
        var isEncryptedV2 = false;

        if (connectPayload[0] == ProtocolV2Marker
            && connectPayload.Length == UdpPacketCipher.HandshakePayloadSizeBytes)
        {
            isEncryptedV2 = true;
            var ticketId = new Guid(connectPayload.Slice(1, 16));
            var timestampUnixMs = BinaryPrimitives.ReadInt64LittleEndian(connectPayload.Slice(17, 8));
            var hmac = connectPayload.Slice(25, 32);
            if (!_matchmakingService.TryConsumeSecureConnectionTicket(
                    ticketId,
                    timestampUnixMs,
                    hmac,
                    out playerConnection))
            {
                return false;
            }
        }
        else
        {
            if (!TryExtractConnectionTicket(connectPayload, out var ticket)
                || !_matchmakingService.TryConsumeConnectionTicket(ticket, out playerConnection))
            {
                return false;
            }
        }

        channel = new WebSocketPeerChannel();
        _inboundWsEvents.Enqueue(InboundWsEvent.Connect(channel, playerConnection, isEncryptedV2));
        return true;
    }

    /// <summary>Enqueues an inbound binary WebSocket packet to be processed on the relay loop.</summary>
    public void EnqueueWebSocketPacket(WebSocketPeerChannel channel, byte[] payload) =>
        _inboundWsEvents.Enqueue(InboundWsEvent.Packet(channel, payload));

    /// <summary>Enqueues a WebSocket disconnect notification to clean up the player's room and ticket state.</summary>
    public void EnqueueWebSocketDisconnect(WebSocketPeerChannel channel) =>
        _inboundWsEvents.Enqueue(InboundWsEvent.Disconnect(channel));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenanceMs = Environment.TickCount64 + MaintenanceIntervalMs;
        var nextStateRelayMs = Environment.TickCount64 + _options.StateRelayIntervalMs;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.TickIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _netManager.PollEvents();

                var nowMs = Environment.TickCount64;
                DrainWebSocketEvents(nowMs);

                if (_options.StateRelayIntervalMs > 0 && nowMs >= nextStateRelayMs)
                {
                    FlushDirtyStates();
                    nextStateRelayMs = nowMs + _options.StateRelayIntervalMs;
                }

                if (nowMs >= nextMaintenanceMs)
                {
                    RunMaintenance(nowMs);
                    nextMaintenanceMs = nowMs + MaintenanceIntervalMs;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void DrainWebSocketEvents(long nowMs)
    {
        while (_inboundWsEvents.TryDequeue(out var wsEvent))
        {
            switch (wsEvent.Kind)
            {
                case WsEventKind.Connect:
                {
                    var session = new PlayerSession(
                        wsEvent.Channel,
                        wsEvent.Connection!,
                        nowMs,
                        wsEvent.IsEncryptedV2);
                    _wsSessions[wsEvent.Channel] = session;
                    _pendingHandshakeSessions.Add(session);
                    _logger.LogInformation(
                        "Player {UserId} established WebSocket bridge for match {MatchId} (v2={IsEncryptedV2}).",
                        session.Connection.UserId,
                        session.Connection.MatchId,
                        session.IsEncryptedV2);
                    break;
                }
                case WsEventKind.Packet:
                {
                    if (_wsSessions.TryGetValue(wsEvent.Channel, out var session))
                    {
                        ProcessInboundWebSocketPacket(session, wsEvent.Payload!, nowMs);
                    }
                    break;
                }
                case WsEventKind.Disconnect:
                {
                    if (_wsSessions.Remove(wsEvent.Channel, out var session))
                    {
                        CleanupDisconnectedSession(session, "WebSocketDisconnected");
                    }
                    wsEvent.Channel.Dispose();
                    break;
                }
            }
        }
    }

    private void ProcessInboundWebSocketPacket(PlayerSession session, byte[] payload, long nowMs)
    {
        try
        {
            if (!session.AllowPacket(nowMs, _options.MaxPacketsPerSecondPerPeer))
            {
                DisconnectMalformedSession(session, "packet rate limit exceeded");
                return;
            }

            if (payload.Length <= 0 || payload.Length > _maxReceivePayloadBytes)
            {
                DisconnectMalformedSession(session, $"invalid payload size ({payload.Length} bytes)");
                return;
            }

            if (session.IsEncryptedV2)
            {
                HandleEncryptedV2Bytes(session, payload, DeliveryMethod.ReliableOrdered, nowMs);
                return;
            }

            if (payload.Length > _maxPlaintextPayloadBytes)
            {
                DisconnectMalformedSession(session, $"invalid v1 payload size ({payload.Length} bytes)");
                return;
            }

            _wsReader.SetSource(payload);
            if (_wsReader.PeekByte() != ProtocolMarker)
            {
                if (!_options.EnableLegacyPing)
                {
                    DisconnectMalformedSession(session, "legacy ping is disabled");
                    return;
                }

                HandleLegacyPing(session, _wsReader);
                return;
            }

            _wsReader.GetByte();
            DispatchClientMessage(session, _wsReader, DeliveryMethod.ReliableOrdered, nowMs);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException
                or FormatException)
        {
            _logger.LogDebug(exception, "Disconnected a WebSocket peer that sent a malformed packet.");
            session.Disconnect();
        }
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        var nowMs = Environment.TickCount64;
        if (!AllowConnectionAttempt(request.RemoteEndPoint.Address, nowMs))
        {
            _logger.LogDebug(
                "Rejected a UDP connection from {RemoteIp} due to connection rate limit.",
                request.RemoteEndPoint.Address);
            request.RejectForce();
            return;
        }

        var payload = request.Data;
        if (payload.AvailableBytes is <= 0 or > MaximumConnectionPayloadBytes)
        {
            _logger.LogDebug("Rejected a UDP connection with an invalid ticket payload size.");
            request.RejectForce();
            return;
        }

        PlayerConnection playerConnection;
        var isEncryptedV2 = false;

        if (payload.PeekByte() == ProtocolV2Marker && payload.AvailableBytes == UdpPacketCipher.HandshakePayloadSizeBytes)
        {
            isEncryptedV2 = true;
            if (!TryReadAndConsumeV2Connection(payload, out playerConnection))
            {
                _logger.LogDebug("Rejected a v2 UDP connection without a valid HMAC ticket.");
                request.RejectForce();
                return;
            }
        }
        else
        {
            if (_options.RequireEncryptedUdp)
            {
                _logger.LogDebug("Rejected an unencrypted v1 UDP connection because RequireEncryptedUdp is enabled.");
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
                || !_matchmakingService.TryConsumeConnectionTicket(ticket, out playerConnection))
            {
                _logger.LogDebug("Rejected a UDP connection without a valid matchmaking ticket.");
                request.RejectForce();
                return;
            }
        }

        var peer = request.Accept();
        if (peer is null)
        {
            _matchmakingService.PlayerDisconnected(playerConnection);
            return;
        }

        var session = new PlayerSession(peer, playerConnection, nowMs, isEncryptedV2);
        peer.Tag = session;
        _pendingHandshakeSessions.Add(session);
    }

    private static bool TryExtractConnectionTicket(ReadOnlySpan<byte> connectPayload, out string ticket)
    {
        ticket = string.Empty;
        if (connectPayload.Length >= 2)
        {
            var encodedLength = BinaryPrimitives.ReadUInt16LittleEndian(connectPayload[..2]);
            if (encodedLength > 0 && encodedLength <= MaximumConnectionPayloadBytes && connectPayload.Length == 2 + encodedLength)
            {
                ticket = Encoding.UTF8.GetString(connectPayload[2..]);
                return !string.IsNullOrWhiteSpace(ticket);
            }
        }

        if (connectPayload.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        ticket = Encoding.UTF8.GetString(connectPayload);
        return !string.IsNullOrWhiteSpace(ticket);
    }

    private bool TryReadAndConsumeV2Connection(NetDataReader payload, out PlayerConnection connection)
    {
        payload.GetBytes(_handshakeScratch, UdpPacketCipher.HandshakePayloadSizeBytes);

        var ticketId = new Guid(_handshakeScratch.AsSpan(1, 16));
        var timestampUnixMs = BinaryPrimitives.ReadInt64LittleEndian(_handshakeScratch.AsSpan(17, 8));
        var hmac = _handshakeScratch.AsSpan(25, 32);

        return _matchmakingService.TryConsumeSecureConnectionTicket(
            ticketId,
            timestampUnixMs,
            hmac,
            out connection);
    }

    private void OnPeerConnected(NetPeer peer)
    {
        if (peer.Tag is PlayerSession session)
        {
            _logger.LogInformation(
                "Player {UserId} established UDP transport for match {MatchId} (v2={IsEncryptedV2}).",
                session.Connection.UserId,
                session.Connection.MatchId,
                session.IsEncryptedV2);
        }
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer.Tag is not PlayerSession session)
        {
            return;
        }

        CleanupDisconnectedSession(session, disconnectInfo.Reason.ToString());
    }

    private void CleanupDisconnectedSession(PlayerSession session, string reason)
    {
        _pendingHandshakeSessions.Remove(session);

        if (session.HasJoined && _rooms.TryGetValue(session.Connection.MatchId, out var room))
        {
            room.Remove(session);
            if (room.IsEmpty)
            {
                _rooms.Remove(session.Connection.MatchId);
            }
            else
            {
                BroadcastPlayerLeft(room, session);
            }
        }

        session.Dispose();
        _matchmakingService.PlayerDisconnected(session.Connection);
        _logger.LogInformation(
            "Player {UserId} disconnected from match {MatchId} ({Reason}).",
            session.Connection.UserId,
            session.Connection.MatchId,
            reason);
    }

    private void OnNetworkReceive(
        NetPeer peer,
        NetPacketReader reader,
        byte channelNumber,
        DeliveryMethod deliveryMethod)
    {
        try
        {
            if (peer.Tag is not PlayerSession session)
            {
                peer.Disconnect();
                return;
            }

            var nowMs = Environment.TickCount64;
            if (!session.AllowPacket(nowMs, _options.MaxPacketsPerSecondPerPeer))
            {
                DisconnectMalformedSession(session, "packet rate limit exceeded");
                return;
            }

            if (reader.AvailableBytes <= 0 || reader.AvailableBytes > _maxReceivePayloadBytes)
            {
                DisconnectMalformedSession(session, $"invalid payload size ({reader.AvailableBytes} bytes)");
                return;
            }

            if (session.IsEncryptedV2)
            {
                var frameLength = reader.AvailableBytes;
                if (frameLength <= UdpPacketCipher.HeaderSizeBytes || frameLength > _encryptScratch.Length)
                {
                    DisconnectMalformedSession(session, "invalid encrypted frame size");
                    return;
                }

                reader.GetBytes(_encryptScratch, frameLength);
                HandleEncryptedV2Bytes(session, _encryptScratch.AsSpan(0, frameLength), deliveryMethod, nowMs);
                return;
            }

            if (reader.AvailableBytes > _maxPlaintextPayloadBytes)
            {
                DisconnectMalformedSession(session, $"invalid v1 payload size ({reader.AvailableBytes} bytes)");
                return;
            }

            if (reader.PeekByte() != ProtocolMarker)
            {
                if (!_options.EnableLegacyPing)
                {
                    DisconnectMalformedSession(session, "legacy ping is disabled");
                    return;
                }

                HandleLegacyPing(session, reader);
                return;
            }

            reader.GetByte();
            DispatchClientMessage(session, reader, deliveryMethod, nowMs);
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

    private void HandleEncryptedV2Bytes(
        PlayerSession session,
        ReadOnlySpan<byte> frame,
        DeliveryMethod deliveryMethod,
        long nowMs)
    {
        if (session.AesGcm is null)
        {
            DisconnectMalformedSession(session, "missing AES-GCM session key");
            return;
        }

        if (frame.Length <= UdpPacketCipher.HeaderSizeBytes || frame.Length > _encryptScratch.Length)
        {
            DisconnectMalformedSession(session, "invalid encrypted frame size");
            return;
        }

        if (!UdpPacketCipher.TryDecrypt(
                session.AesGcm,
                UdpPacketCipher.DirectionClientToServer,
                frame,
                _decryptScratch,
                out var sequence,
                out var plaintextLength))
        {
            DisconnectMalformedSession(session, "failed to authenticate/decrypt frame");
            return;
        }

        if (!session.TryAcceptIncomingSequence(sequence))
        {
            DisconnectMalformedSession(session, "replayed or out-of-window sequence");
            return;
        }

        if (plaintextLength < 2 || _decryptScratch[0] != ProtocolV2Marker)
        {
            DisconnectMalformedSession(session, "invalid decrypted v2 protocol marker");
            return;
        }

        _decryptedReader.SetSource(_decryptScratch, 1, plaintextLength);
        DispatchClientMessage(session, _decryptedReader, deliveryMethod, nowMs);
    }

    private void DispatchClientMessage(
        PlayerSession session,
        NetDataReader reader,
        DeliveryMethod deliveryMethod,
        long nowMs)
    {
        switch (reader.GetByte())
        {
            case ClientHello:
                HandleHello(session, reader);
                break;
            case ClientState:
                HandlePlayerState(session, reader, nowMs);
                break;
            case ClientGameData:
                HandleGameData(session, reader, deliveryMethod);
                break;
            default:
                DisconnectMalformedSession(session, "unknown gameplay message type");
                break;
        }
    }

    private void HandleLegacyPing(PlayerSession session, NetDataReader reader)
    {
        if (reader.AvailableBytes > MaximumConnectionPayloadBytes
            || reader.GetString() != "ping"
            || reader.AvailableBytes != 0)
        {
            DisconnectMalformedSession(session, "invalid legacy ping packet");
            return;
        }

        _v1Writer.Reset();
        _v1Writer.Put("pong");
        session.Send(_v1Writer, DeliveryMethod.ReliableOrdered);
    }

    private void HandleHello(PlayerSession session, NetDataReader reader)
    {
        if (reader.AvailableBytes != 0)
        {
            DisconnectMalformedSession(session, "hello packet contained unexpected data");
            return;
        }

        if (session.HasJoined)
        {
            DisconnectMalformedSession(session, "duplicate hello packet");
            return;
        }

        if (!_rooms.TryGetValue(session.Connection.MatchId, out var room))
        {
            room = new MatchRoom();
            _rooms.Add(session.Connection.MatchId, room);
        }

        if (!room.TryAdd(session))
        {
            DisconnectMalformedSession(session, "match room slot capacity reached");
            return;
        }

        session.HasJoined = true;
        _pendingHandshakeSessions.Remove(session);

        SendHandshakeAccepted(session);

        _logger.LogInformation(
            "Player {UserId} ({DisplayName}) completed the gameplay handshake for match {MatchId} in slot {SlotId}.",
            session.Connection.UserId,
            session.DisplayName,
            session.Connection.MatchId,
            session.SlotId);

        foreach (var existing in room.JoinedSessions)
        {
            if (existing == session)
            {
                continue;
            }

            SendPlayerJoined(session, existing);
            if (existing.State is PlayerState existingState)
            {
                SendPlayerState(session, existing, existingState);
            }

            SendPlayerJoined(existing, session);
        }
    }

    private void HandlePlayerState(
        PlayerSession session,
        NetDataReader reader,
        long nowMs)
    {
        if (!session.HasJoined)
        {
            DisconnectMalformedSession(session, "state packet arrived before the hello handshake");
            return;
        }

        if (reader.AvailableBytes != PlayerStatePayloadBytes)
        {
            DisconnectMalformedSession(session, $"state packet had an invalid size ({reader.AvailableBytes} bytes)");
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
            DisconnectMalformedSession(session, "state packet contained invalid position or animation values");
            return;
        }

        session.State = state;
        if (!session.HasReceivedState)
        {
            session.HasReceivedState = true;
            LogPlayerState(session, state, "first");
            session.NextStateLogTickMs = nowMs + 1000;
        }
        else if (nowMs >= session.NextStateLogTickMs
            && session.LastLoggedState is PlayerState lastLoggedState
            && HasMeaningfulPositionChange(lastLoggedState, state))
        {
            LogPlayerState(session, state, "movement");
            session.NextStateLogTickMs = nowMs + 1000;
        }

        if (!_rooms.TryGetValue(session.Connection.MatchId, out var room) || room.JoinedSessions.Count <= 1)
        {
            return;
        }

        if (_options.StateRelayIntervalMs > 0)
        {
            session.HasDirtyState = true;
            room.HasDirtyStates = true;
            return;
        }

        BroadcastPlayerState(room, session, state);
    }

    private void HandleGameData(
        PlayerSession session,
        NetDataReader reader,
        DeliveryMethod deliveryMethod)
    {
        if (!session.HasJoined)
        {
            DisconnectMalformedSession(session, "game data packet arrived before the hello handshake");
            return;
        }

        if (reader.AvailableBytes < 2)
        {
            DisconnectMalformedSession(session, "game data packet was too small");
            return;
        }

        if (!_rooms.TryGetValue(session.Connection.MatchId, out var room))
        {
            return;
        }

        var scope = reader.GetByte();
        PlayerSession? unicastRecipient = null;
        if (scope == GameDataScopeUnicast)
        {
            if (session.IsEncryptedV2)
            {
                if (reader.AvailableBytes < sizeof(ushort) + 1)
                {
                    DisconnectMalformedSession(session, "v2 unicast game data packet was too small");
                    return;
                }

                var targetSlotId = reader.GetUShort();
                if (!room.TryGetSessionBySlot(targetSlotId, out var recipient))
                {
                    return;
                }

                unicastRecipient = recipient;
            }
            else
            {
                var targetIdText = reader.GetString(40);
                if (!Guid.TryParse(targetIdText, out var parsedTargetUserId))
                {
                    DisconnectMalformedSession(session, "game data packet had an invalid target user id");
                    return;
                }

                if (!room.TryGetSession(parsedTargetUserId, out var recipient))
                {
                    return;
                }

                unicastRecipient = recipient;
            }
        }
        else if (scope != GameDataScopeBroadcast)
        {
            DisconnectMalformedSession(session, "game data packet had an invalid routing scope");
            return;
        }

        var payloadLength = reader.AvailableBytes;
        if (payloadLength <= 0 || payloadLength > _options.MaximumGameDataBytes)
        {
            DisconnectMalformedSession(session, $"game data payload size ({payloadLength} bytes) is out of range");
            return;
        }

        reader.GetBytes(_gameDataScratch, payloadLength);
        var relayDelivery = NormalizeRelayDeliveryMethod(deliveryMethod);

        if (unicastRecipient is not null)
        {
            if (unicastRecipient != session)
            {
                SendGameData(unicastRecipient, session, payloadLength, relayDelivery);
            }

            return;
        }

        BroadcastGameData(room, session, payloadLength, relayDelivery);
    }

    private void FlushDirtyStates()
    {
        foreach (var room in _rooms.Values)
        {
            if (!room.HasDirtyStates)
            {
                continue;
            }

            room.HasDirtyStates = false;
            if (room.JoinedSessions.Count <= 1)
            {
                foreach (var session in room.JoinedSessions)
                {
                    session.HasDirtyState = false;
                }
                continue;
            }

            foreach (var sender in room.JoinedSessions)
            {
                if (!sender.HasDirtyState || sender.State is not PlayerState state)
                {
                    continue;
                }

                sender.HasDirtyState = false;
                BroadcastPlayerState(room, sender, state);
            }
        }
    }

    private void SendHandshakeAccepted(PlayerSession target)
    {
        if (target.IsEncryptedV2)
        {
            _v2PlaintextWriter.Reset();
            _v2PlaintextWriter.Put(ProtocolV2Marker);
            _v2PlaintextWriter.Put(ServerHandshakeAccepted);
            _v2PlaintextWriter.Put(target.SlotId);
            SendEncryptedV2(target, _v2PlaintextWriter, DeliveryMethod.ReliableOrdered);
            return;
        }

        _v1Writer.Reset();
        _v1Writer.Put(ProtocolMarker);
        _v1Writer.Put(ServerHandshakeAccepted);
        target.Send(_v1Writer, DeliveryMethod.ReliableOrdered);
    }

    private void SendPlayerJoined(PlayerSession target, PlayerSession subject)
    {
        if (target.IsEncryptedV2)
        {
            _v2PlaintextWriter.Reset();
            _v2PlaintextWriter.Put(ProtocolV2Marker);
            _v2PlaintextWriter.Put(ServerPlayerJoined);
            _v2PlaintextWriter.Put(subject.SlotId);
            _v2PlaintextWriter.Put(subject.WireUserId);
            _v2PlaintextWriter.Put(subject.DisplayName);
            SendEncryptedV2(target, _v2PlaintextWriter, DeliveryMethod.ReliableOrdered);
            return;
        }

        _v1Writer.Reset();
        _v1Writer.Put(ProtocolMarker);
        _v1Writer.Put(ServerPlayerJoined);
        _v1Writer.Put(subject.WireUserId);
        _v1Writer.Put(subject.DisplayName);
        target.Send(_v1Writer, DeliveryMethod.ReliableOrdered);
    }

    private void BroadcastPlayerLeft(MatchRoom room, PlayerSession departed)
    {
        var hasV1 = false;
        var hasV2 = false;
        foreach (var peer in room.JoinedSessions)
        {
            if (peer.IsEncryptedV2) hasV2 = true;
            else hasV1 = true;
        }

        if (hasV1)
        {
            _v1Writer.Reset();
            _v1Writer.Put(ProtocolMarker);
            _v1Writer.Put(ServerPlayerLeft);
            _v1Writer.Put(departed.WireUserId);
            foreach (var peer in room.JoinedSessions)
            {
                if (!peer.IsEncryptedV2)
                {
                    peer.Send(_v1Writer, DeliveryMethod.ReliableOrdered);
                }
            }
        }

        if (hasV2)
        {
            _v2PlaintextWriter.Reset();
            _v2PlaintextWriter.Put(ProtocolV2Marker);
            _v2PlaintextWriter.Put(ServerPlayerLeft);
            _v2PlaintextWriter.Put(departed.SlotId);
            foreach (var peer in room.JoinedSessions)
            {
                if (peer.IsEncryptedV2)
                {
                    SendEncryptedV2(peer, _v2PlaintextWriter, DeliveryMethod.ReliableOrdered);
                }
            }
        }
    }

    private void SendPlayerState(PlayerSession target, PlayerSession subject, PlayerState state)
    {
        if (target.IsEncryptedV2)
        {
            WriteV2PlayerState(_v2PlaintextWriter, subject.SlotId, state);
            SendEncryptedV2(target, _v2PlaintextWriter, DeliveryMethod.Sequenced);
            return;
        }

        WriteV1PlayerState(_v1Writer, subject.WireUserId, state);
        target.Send(_v1Writer, DeliveryMethod.Sequenced);
    }

    private void BroadcastPlayerState(MatchRoom room, PlayerSession sender, PlayerState state)
    {
        var hasV1 = false;
        var hasV2 = false;
        foreach (var peer in room.JoinedSessions)
        {
            if (peer == sender) continue;
            if (peer.IsEncryptedV2) hasV2 = true;
            else hasV1 = true;
        }

        if (hasV1)
        {
            WriteV1PlayerState(_v1Writer, sender.WireUserId, state);
            foreach (var peer in room.JoinedSessions)
            {
                if (peer != sender && !peer.IsEncryptedV2)
                {
                    peer.Send(_v1Writer, DeliveryMethod.Sequenced);
                }
            }
        }

        if (hasV2)
        {
            WriteV2PlayerState(_v2PlaintextWriter, sender.SlotId, state);
            foreach (var peer in room.JoinedSessions)
            {
                if (peer != sender && peer.IsEncryptedV2)
                {
                    SendEncryptedV2(peer, _v2PlaintextWriter, DeliveryMethod.Sequenced);
                }
            }
        }
    }

    private void SendGameData(
        PlayerSession target,
        PlayerSession sender,
        int payloadLength,
        DeliveryMethod deliveryMethod)
    {
        if (target.IsEncryptedV2)
        {
            _v2PlaintextWriter.Reset();
            _v2PlaintextWriter.Put(ProtocolV2Marker);
            _v2PlaintextWriter.Put(ServerGameData);
            _v2PlaintextWriter.Put(sender.SlotId);
            _v2PlaintextWriter.Put(_gameDataScratch, 0, payloadLength);
            SendEncryptedV2(target, _v2PlaintextWriter, deliveryMethod);
            return;
        }

        _v1Writer.Reset();
        _v1Writer.Put(ProtocolMarker);
        _v1Writer.Put(ServerGameData);
        _v1Writer.Put(sender.WireUserId);
        _v1Writer.Put(_gameDataScratch, 0, payloadLength);
        target.Send(_v1Writer, deliveryMethod);
    }

    private void BroadcastGameData(
        MatchRoom room,
        PlayerSession sender,
        int payloadLength,
        DeliveryMethod deliveryMethod)
    {
        var hasV1 = false;
        var hasV2 = false;
        foreach (var peer in room.JoinedSessions)
        {
            if (peer == sender) continue;
            if (peer.IsEncryptedV2) hasV2 = true;
            else hasV1 = true;
        }

        if (hasV1)
        {
            _v1Writer.Reset();
            _v1Writer.Put(ProtocolMarker);
            _v1Writer.Put(ServerGameData);
            _v1Writer.Put(sender.WireUserId);
            _v1Writer.Put(_gameDataScratch, 0, payloadLength);
            foreach (var peer in room.JoinedSessions)
            {
                if (peer != sender && !peer.IsEncryptedV2)
                {
                    peer.Send(_v1Writer, deliveryMethod);
                }
            }
        }

        if (hasV2)
        {
            _v2PlaintextWriter.Reset();
            _v2PlaintextWriter.Put(ProtocolV2Marker);
            _v2PlaintextWriter.Put(ServerGameData);
            _v2PlaintextWriter.Put(sender.SlotId);
            _v2PlaintextWriter.Put(_gameDataScratch, 0, payloadLength);
            foreach (var peer in room.JoinedSessions)
            {
                if (peer != sender && peer.IsEncryptedV2)
                {
                    SendEncryptedV2(peer, _v2PlaintextWriter, deliveryMethod);
                }
            }
        }
    }

    private void SendEncryptedV2(PlayerSession target, NetDataWriter plaintextWriter, DeliveryMethod deliveryMethod)
    {
        if (target.AesGcm is null)
        {
            return;
        }

        var seq = target.NextOutgoingSequence();
        var length = UdpPacketCipher.Encrypt(
            target.AesGcm,
            UdpPacketCipher.DirectionServerToClient,
            seq,
            plaintextWriter.Data.AsSpan(0, plaintextWriter.Length),
            _encryptScratch);
        target.Send(_encryptScratch, 0, length, deliveryMethod);
    }

    private bool AllowConnectionAttempt(IPAddress remoteAddress, long nowMs)
    {
        lock (_ipRateSync)
        {
            var windowMs = _options.ConnectionRequestWindowSeconds * 1000L;
            if (_connectionAttemptsByIp.TryGetValue(remoteAddress, out var window))
            {
                if (nowMs - window.WindowStartMs >= windowMs)
                {
                    _connectionAttemptsByIp[remoteAddress] = new IpRateWindow(nowMs, 1);
                    return true;
                }

                if (window.Count >= _options.MaxConnectionRequestsPerIpPerWindow)
                {
                    return false;
                }

                _connectionAttemptsByIp[remoteAddress] = window with { Count = window.Count + 1 };
                return true;
            }

            if (_connectionAttemptsByIp.Count >= MaxTrackedConnectionIps)
            {
                PruneConnectionAttemptWindowsLocked(nowMs);
                if (_connectionAttemptsByIp.Count >= MaxTrackedConnectionIps)
                {
                    _connectionAttemptsByIp.Clear();
                }
            }

            _connectionAttemptsByIp[remoteAddress] = new IpRateWindow(nowMs, 1);
            return true;
        }
    }

    private void RunMaintenance(long nowMs)
    {
        var handshakeTimeoutMs = _options.HandshakeTimeoutSeconds * 1000L;
        for (var i = _pendingHandshakeSessions.Count - 1; i >= 0; i--)
        {
            var session = _pendingHandshakeSessions[i];
            if (session.HasJoined)
            {
                _pendingHandshakeSessions.RemoveAt(i);
                continue;
            }

            if (nowMs - session.ConnectedTickMs >= handshakeTimeoutMs)
            {
                _pendingHandshakeSessions.RemoveAt(i);
                DisconnectMalformedSession(session, "handshake timeout");
            }
        }

        lock (_ipRateSync)
        {
            PruneConnectionAttemptWindowsLocked(nowMs);
        }
    }

    private void PruneConnectionAttemptWindowsLocked(long nowMs)
    {
        if (_connectionAttemptsByIp.Count == 0)
        {
            return;
        }

        var expiryMs = _options.ConnectionRequestWindowSeconds * 1000L;
        List<IPAddress>? expired = null;
        foreach (var entry in _connectionAttemptsByIp)
        {
            if (nowMs - entry.Value.WindowStartMs >= expiryMs)
            {
                expired ??= [];
                expired.Add(entry.Key);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var address in expired)
        {
            _connectionAttemptsByIp.Remove(address);
        }
    }

    private void DisposeAllSessions()
    {
        foreach (var session in _pendingHandshakeSessions)
        {
            session.Dispose();
        }
        _pendingHandshakeSessions.Clear();

        foreach (var session in _wsSessions.Values)
        {
            session.Dispose();
        }
        _wsSessions.Clear();

        foreach (var room in _rooms.Values)
        {
            foreach (var session in room.JoinedSessions)
            {
                session.Dispose();
            }
        }
        _rooms.Clear();
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

    private static DeliveryMethod NormalizeRelayDeliveryMethod(DeliveryMethod deliveryMethod) =>
        deliveryMethod switch
        {
            DeliveryMethod.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            DeliveryMethod.ReliableUnordered => DeliveryMethod.ReliableUnordered,
            DeliveryMethod.Sequenced => DeliveryMethod.Sequenced,
            DeliveryMethod.Unreliable => DeliveryMethod.Unreliable,
            _ => DeliveryMethod.ReliableOrdered
        };

    private static void WriteV1PlayerState(NetDataWriter writer, string wireUserId, PlayerState state)
    {
        writer.Reset();
        writer.Put(ProtocolMarker);
        writer.Put(ServerPlayerState);
        writer.Put(wireUserId);
        WriteStateBody(writer, state);
    }

    private static void WriteV2PlayerState(NetDataWriter writer, ushort slotId, PlayerState state)
    {
        writer.Reset();
        writer.Put(ProtocolV2Marker);
        writer.Put(ServerPlayerState);
        writer.Put(slotId);
        WriteStateBody(writer, state);
    }

    private static void WriteStateBody(NetDataWriter writer, PlayerState state)
    {
        writer.Put(state.X);
        writer.Put(state.Y);
        writer.Put(state.Z);
        writer.Put(state.RotationY);
        writer.Put(state.RunX);
        writer.Put(state.RunZ);
        writer.Put(state.Flags);
    }

    private void DisconnectMalformedSession(PlayerSession session, string reason)
    {
        _logger.LogDebug("Disconnected a peer because {Reason}.", reason);
        session.Disconnect();
    }

    private enum WsEventKind
    {
        Connect,
        Packet,
        Disconnect
    }

    private readonly record struct InboundWsEvent(
        WsEventKind Kind,
        WebSocketPeerChannel Channel,
        PlayerConnection? Connection,
        bool IsEncryptedV2,
        byte[]? Payload)
    {
        public static InboundWsEvent Connect(
            WebSocketPeerChannel channel,
            PlayerConnection connection,
            bool isEncryptedV2) =>
            new(WsEventKind.Connect, channel, connection, isEncryptedV2, null);

        public static InboundWsEvent Packet(WebSocketPeerChannel channel, byte[] payload) =>
            new(WsEventKind.Packet, channel, null, false, payload);

        public static InboundWsEvent Disconnect(WebSocketPeerChannel channel) =>
            new(WsEventKind.Disconnect, channel, null, false, null);
    }

    private sealed class MatchRoom
    {
        private readonly Dictionary<Guid, PlayerSession> _sessionsByUserId = [];
        private readonly Dictionary<ushort, PlayerSession> _sessionsBySlot = [];
        private ushort _nextSlotId = 1;

        public List<PlayerSession> JoinedSessions { get; } = [];

        public bool IsEmpty => JoinedSessions.Count == 0;

        public bool HasDirtyStates { get; set; }

        public bool TryAdd(PlayerSession session)
        {
            ushort allocatedSlot = 0;
            for (var attempts = 0; attempts < ushort.MaxValue; attempts++)
            {
                if (_nextSlotId == 0)
                {
                    _nextSlotId = 1;
                }

                var candidate = _nextSlotId++;
                if (!_sessionsBySlot.ContainsKey(candidate))
                {
                    allocatedSlot = candidate;
                    break;
                }
            }

            if (allocatedSlot == 0)
            {
                return false;
            }

            session.SlotId = allocatedSlot;
            JoinedSessions.Add(session);
            _sessionsByUserId[session.Connection.UserId] = session;
            _sessionsBySlot[allocatedSlot] = session;
            return true;
        }

        public void Remove(PlayerSession session)
        {
            JoinedSessions.Remove(session);
            _sessionsByUserId.Remove(session.Connection.UserId);
            if (session.SlotId != 0)
            {
                _sessionsBySlot.Remove(session.SlotId);
            }
        }

        public bool TryGetSession(Guid userId, out PlayerSession session) =>
            _sessionsByUserId.TryGetValue(userId, out session!);

        public bool TryGetSessionBySlot(ushort slotId, out PlayerSession session) =>
            _sessionsBySlot.TryGetValue(slotId, out session!);
    }

    private sealed class PlayerSession : IDisposable
    {
        private readonly NetPeer? _udpPeer;
        private readonly WebSocketPeerChannel? _webSocketChannel;
        private long _packetWindowStartMs;
        private int _packetCountInWindow;
        private ulong _outgoingSequence;
        private UdpPacketCipher.ReplayWindow _incomingReplayWindow;

        public PlayerSession(
            NetPeer peer,
            PlayerConnection connection,
            long connectedTickMs,
            bool isEncryptedV2)
            : this(connection, connectedTickMs, isEncryptedV2)
        {
            _udpPeer = peer;
        }

        public PlayerSession(
            WebSocketPeerChannel webSocketChannel,
            PlayerConnection connection,
            long connectedTickMs,
            bool isEncryptedV2)
            : this(connection, connectedTickMs, isEncryptedV2)
        {
            _webSocketChannel = webSocketChannel;
        }

        private PlayerSession(
            PlayerConnection connection,
            long connectedTickMs,
            bool isEncryptedV2)
        {
            Connection = connection;
            ConnectedTickMs = connectedTickMs;
            IsEncryptedV2 = isEncryptedV2;
            _packetWindowStartMs = connectedTickMs;
            WireUserId = connection.UserId.ToString("N");
            DisplayName = string.IsNullOrWhiteSpace(connection.PlayerName)
                ? $"Player-{WireUserId[..6]}"
                : connection.PlayerName.Trim();

            if (isEncryptedV2 && connection.UdpSessionKey is { Length: UdpPacketCipher.KeySizeBytes })
            {
                AesGcm = new AesGcm(connection.UdpSessionKey, UdpPacketCipher.TagSizeBytes);
            }
        }

        public PlayerConnection Connection { get; }

        public long ConnectedTickMs { get; }

        public bool IsEncryptedV2 { get; }

        public AesGcm? AesGcm { get; private set; }

        public ushort SlotId { get; set; }

        public string WireUserId { get; }

        public string DisplayName { get; }

        public bool HasJoined { get; set; }

        public bool HasReceivedState { get; set; }

        public bool HasDirtyState { get; set; }

        public long NextStateLogTickMs { get; set; }

        public PlayerState? LastLoggedState { get; set; }

        public PlayerState? State { get; set; }

        public void Send(NetDataWriter writer, DeliveryMethod deliveryMethod) =>
            Send(writer.Data, 0, writer.Length, deliveryMethod);

        public void Send(byte[] buffer, int offset, int length, DeliveryMethod deliveryMethod)
        {
            if (_udpPeer is not null)
            {
                _udpPeer.Send(buffer, offset, length, ReliableChannel, deliveryMethod);
                return;
            }

            _webSocketChannel?.EnqueueSend(buffer.AsSpan(offset, length), deliveryMethod);
        }

        public void Disconnect()
        {
            _udpPeer?.Disconnect();
            _webSocketChannel?.RequestDisconnect();
        }

        public ulong NextOutgoingSequence() => ++_outgoingSequence;

        public bool TryAcceptIncomingSequence(ulong sequence) =>
            _incomingReplayWindow.TryAccept(sequence);

        public bool AllowPacket(long nowMs, int maxPacketsPerSecond)
        {
            if (nowMs - _packetWindowStartMs >= 1000)
            {
                _packetWindowStartMs = nowMs;
                _packetCountInWindow = 1;
                return true;
            }

            if (_packetCountInWindow >= maxPacketsPerSecond)
            {
                return false;
            }

            _packetCountInWindow++;
            return true;
        }

        public void Dispose()
        {
            AesGcm?.Dispose();
            AesGcm = null;
        }
    }

    private readonly record struct IpRateWindow(long WindowStartMs, int Count);

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
