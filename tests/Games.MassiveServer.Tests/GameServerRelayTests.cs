using System.Buffers.Binary;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Endpoints;
using Games.MassiveServer.Hosting;
using Games.MassiveServer.Services;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Core.Configuration;

namespace Games.MassiveServer.Tests;

[TestClass]
public sealed class GameServerRelayTests
{
    [TestMethod]
    public async Task ConnectedPlayers_ReceivePeerPresenceAndVisualState()
    {
        var udpPort = GetAvailableUdpPort();
        var matchmaking = new MatchmakingService(
            Options.Create(new MatchmakingOptions
            {
                MinPlayers = 1,
                MaxPlayers = 2,
                PersistentWorld = true
            }),
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString()
            }),
            TimeProvider.System);
        var firstTicket = matchmaking.Enqueue(Guid.NewGuid());
        var secondTicket = matchmaking.Enqueue(Guid.NewGuid());
        var server = new GameServerHostedService(
            matchmaking,
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString()
            }),
            NullLogger<GameServerHostedService>.Instance);

        var firstJoined = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandshakeAccepted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedState = new TaskCompletionSource<RelayedState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstListener = new EventBasedNetListener();
        var secondListener = new EventBasedNetListener();
        var firstConnected = new TaskCompletionSource<NetPeer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnected = new TaskCompletionSource<NetPeer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        firstListener.PeerConnectedEvent += peer => firstConnected.TrySetResult(peer);
        secondListener.PeerConnectedEvent += peer => secondConnected.TrySetResult(peer);
        firstListener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                if (reader.PeekByte() != 0xD5)
                {
                    return;
                }

                reader.GetByte();
                var messageType = reader.GetByte();
                if (messageType == 13)
                {
                    Assert.AreEqual(0, reader.AvailableBytes);
                    firstHandshakeAccepted.TrySetResult(true);
                }
                else if (messageType == 10)
                {
                    var playerId = reader.GetString();
                    _ = reader.GetString();
                    firstJoined.TrySetResult(playerId);
                }
                else if (messageType == 12)
                {
                    var playerId = reader.GetString();
                    var x = reader.GetFloat();
                    var y = reader.GetFloat();
                    var z = reader.GetFloat();
                    var rotationY = reader.GetFloat();
                    var runX = reader.GetFloat();
                    var runZ = reader.GetFloat();
                    receivedState.TrySetResult(new RelayedState(
                        playerId,
                        x,
                        y,
                        z,
                        rotationY,
                        runX,
                        runZ));
                }
            }
            finally
            {
                reader.Recycle();
            }
        };

        var firstClient = new NetManager(firstListener);
        var secondClient = new NetManager(secondListener);
        Assert.IsTrue(firstClient.Start());
        Assert.IsTrue(secondClient.Start());

        await server.StartAsync(CancellationToken.None);
        try
        {
            Connect(firstClient, udpPort, firstTicket.ConnectionTicket!);
            Connect(secondClient, udpPort, secondTicket.ConnectionTicket!);
            var firstPeer = await PollUntilCompletedAsync(
                firstClient,
                secondClient,
                firstConnected.Task);
            var secondPeer = await PollUntilCompletedAsync(
                firstClient,
                secondClient,
                secondConnected.Task);

            SendHello(firstPeer);
            SendHello(secondPeer);
            Assert.IsTrue(await PollUntilCompletedAsync(
                firstClient,
                secondClient,
                firstHandshakeAccepted.Task));
            var joinedPlayerId = await PollUntilCompletedAsync(
                firstClient,
                secondClient,
                firstJoined.Task);

            var state = new NetDataWriter();
            state.Put((byte)0xD5);
            state.Put((byte)2);
            state.Put(1f);
            state.Put(2f);
            state.Put(3f);
            state.Put(45f);
            state.Put(2f);
            state.Put(-2f);
            state.Put((byte)1);
            secondPeer.Send(state, 0, DeliveryMethod.Sequenced);

            var relayed = await PollUntilCompletedAsync(
                firstClient,
                secondClient,
                receivedState.Task);
            Assert.AreEqual(joinedPlayerId, relayed.PlayerId);
            Assert.AreEqual(1f, relayed.X);
            Assert.AreEqual(2f, relayed.Y);
            Assert.AreEqual(3f, relayed.Z);
            Assert.AreEqual(45f, relayed.RotationY);
            Assert.AreEqual(2f, relayed.RunX);
            Assert.AreEqual(-2f, relayed.RunZ);
        }
        finally
        {
            firstClient.Stop();
            secondClient.Stop();
            await server.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ConnectedPlayers_RelayBroadcastAndUnicastGameDataWithinSameMatchOnly()
    {
        var udpPort = GetAvailableUdpPort();
        var matchmaking = new MatchmakingService(
            Options.Create(new MatchmakingOptions
            {
                MinPlayers = 1,
                MaxPlayers = 2,
                PersistentWorld = true
            }),
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString()
            }),
            TimeProvider.System);
        var match1Player1 = Guid.NewGuid();
        var match1Player2 = Guid.NewGuid();
        var match2Player1 = Guid.NewGuid();
        var ticket1 = matchmaking.Enqueue(match1Player1);
        var ticket2 = matchmaking.Enqueue(match1Player2);
        var ticket3 = matchmaking.Enqueue(match2Player1);
        var server = new GameServerHostedService(
            matchmaking,
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString(),
                EnableLegacyPing = false
            }),
            NullLogger<GameServerHostedService>.Instance);

        var player1GameData = new List<(string SenderId, byte[] Payload)>();
        var player3ReceivedCount = 0;
        var player1Joined = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player2Joined = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player3Handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unicastReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var listener1 = new EventBasedNetListener();
        var listener2 = new EventBasedNetListener();
        var listener3 = new EventBasedNetListener();
        var connected1 = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected2 = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected3 = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener1.PeerConnectedEvent += peer => connected1.TrySetResult(peer);
        listener2.PeerConnectedEvent += peer => connected2.TrySetResult(peer);
        listener3.PeerConnectedEvent += peer => connected3.TrySetResult(peer);

        listener1.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                if (reader.PeekByte() != 0xD5) return;
                reader.GetByte();
                var type = reader.GetByte();
                if (type == 10) player1Joined.TrySetResult(true);
                else if (type == 14)
                {
                    var sender = reader.GetString();
                    var bytes = new byte[reader.AvailableBytes];
                    reader.GetBytes(bytes, bytes.Length);
                    player1GameData.Add((sender, bytes));
                    if (player1GameData.Count == 2)
                    {
                        unicastReceived.TrySetResult(true);
                    }
                }
            }
            finally { reader.Recycle(); }
        };
        listener2.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                if (reader.PeekByte() != 0xD5) return;
                reader.GetByte();
                if (reader.GetByte() == 10) player2Joined.TrySetResult(true);
            }
            finally { reader.Recycle(); }
        };
        listener3.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                if (reader.PeekByte() != 0xD5) return;
                reader.GetByte();
                var type = reader.GetByte();
                if (type == 13) player3Handshake.TrySetResult(true);
                else if (type is 10 or 12 or 14) Interlocked.Increment(ref player3ReceivedCount);
            }
            finally { reader.Recycle(); }
        };

        var client1 = new NetManager(listener1);
        var client2 = new NetManager(listener2);
        var client3 = new NetManager(listener3);
        Assert.IsTrue(client1.Start());
        Assert.IsTrue(client2.Start());
        Assert.IsTrue(client3.Start());

        await server.StartAsync(CancellationToken.None);
        try
        {
            Connect(client1, udpPort, ticket1.ConnectionTicket!);
            Connect(client2, udpPort, ticket2.ConnectionTicket!);
            Connect(client3, udpPort, ticket3.ConnectionTicket!);

            var peer1 = await PollUntilCompletedAsync(client1, client2, client3, connected1.Task);
            var peer2 = await PollUntilCompletedAsync(client1, client2, client3, connected2.Task);
            var peer3 = await PollUntilCompletedAsync(client1, client2, client3, connected3.Task);

            SendHello(peer1);
            SendHello(peer2);
            SendHello(peer3);

            await PollUntilCompletedAsync(client1, client2, client3, player1Joined.Task);
            await PollUntilCompletedAsync(client1, client2, client3, player2Joined.Task);
            await PollUntilCompletedAsync(client1, client2, client3, player3Handshake.Task);

            var broadcast = new NetDataWriter();
            broadcast.Put((byte)0xD5);
            broadcast.Put((byte)3);
            broadcast.Put((byte)0);
            broadcast.Put(new byte[] { 10, 20, 30 });
            peer2.Send(broadcast, 0, DeliveryMethod.ReliableOrdered);

            var unicast = new NetDataWriter();
            unicast.Put((byte)0xD5);
            unicast.Put((byte)3);
            unicast.Put((byte)1);
            unicast.Put(match1Player1.ToString("N"));
            unicast.Put(new byte[] { 99, 100 });
            peer2.Send(unicast, 0, DeliveryMethod.ReliableOrdered);

            Assert.IsTrue(await PollUntilCompletedAsync(client1, client2, client3, unicastReceived.Task));
            Assert.HasCount(2, player1GameData);
            Assert.AreEqual(match1Player2.ToString("N"), player1GameData[0].SenderId);
            CollectionAssert.AreEqual(new byte[] { 10, 20, 30 }, player1GameData[0].Payload);
            CollectionAssert.AreEqual(new byte[] { 99, 100 }, player1GameData[1].Payload);
            Assert.AreEqual(0, Volatile.Read(ref player3ReceivedCount));
        }
        finally
        {
            client1.Stop();
            client2.Stop();
            client3.Stop();
            await server.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task PeerWithoutHello_DisconnectsOnHandshakeTimeout()
    {
        var udpPort = GetAvailableUdpPort();
        var matchmaking = new MatchmakingService(
            Options.Create(new MatchmakingOptions { MinPlayers = 1, MaxPlayers = 2, PersistentWorld = true }),
            Options.Create(new GameServerOptions { UdpPort = udpPort, PublicAddress = IPAddress.Loopback.ToString() }),
            TimeProvider.System);
        var ticket = matchmaking.Enqueue(Guid.NewGuid());
        var server = new GameServerHostedService(
            matchmaking,
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString(),
                HandshakeTimeoutSeconds = 1
            }),
            NullLogger<GameServerHostedService>.Instance);

        var listener = new EventBasedNetListener();
        var connected = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.PeerConnectedEvent += peer => connected.TrySetResult(peer);
        listener.PeerDisconnectedEvent += (_, info) => disconnected.TrySetResult(info);

        var client = new NetManager(listener);
        Assert.IsTrue(client.Start());
        await server.StartAsync(CancellationToken.None);
        try
        {
            Connect(client, udpPort, ticket.ConnectionTicket!);
            _ = await PollUntilCompletedAsync(client, client, connected.Task);
            var info = await PollUntilCompletedAsync(client, client, disconnected.Task);
            Assert.AreEqual(DisconnectReason.RemoteConnectionClose, info.Reason);
        }
        finally
        {
            client.Stop();
            await server.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task EncryptedV2Protocol_AuthenticatesWithHmac_RelaysCompactSlotsAndRejectsReplay()
    {
        var udpPort = GetAvailableUdpPort();
        var matchmaking = new MatchmakingService(
            Options.Create(new MatchmakingOptions { MinPlayers = 1, MaxPlayers = 4, PersistentWorld = true }),
            Options.Create(new GameServerOptions { UdpPort = udpPort, PublicAddress = IPAddress.Loopback.ToString() }),
            TimeProvider.System);
        var ticket1 = matchmaking.Enqueue(Guid.NewGuid(), "PlayerOne");
        var ticket2 = matchmaking.Enqueue(Guid.NewGuid(), "PlayerTwo");
        Assert.IsNotNull(ticket1.SessionKeyBase64);
        Assert.IsNotNull(ticket2.SessionKeyBase64);

        var key1 = Convert.FromBase64String(ticket1.SessionKeyBase64);
        var key2 = Convert.FromBase64String(ticket2.SessionKeyBase64);
        using var aes1 = new AesGcm(key1, UdpPacketCipher.TagSizeBytes);
        using var aes2 = new AesGcm(key2, UdpPacketCipher.TagSizeBytes);

        var server = new GameServerHostedService(
            matchmaking,
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString(),
                RequireEncryptedUdp = true,
                EnableLegacyPing = false
            }),
            NullLogger<GameServerHostedService>.Instance);

        ushort slot1 = 0;
        ushort slot2 = 0;
        string? joinedName = null;
        float relayedX = 0;
        ushort relayedSlot = 0;
        var accepted1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replayDisconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var v1Rejected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

        var listener1 = new EventBasedNetListener();
        var listener2 = new EventBasedNetListener();
        var listenerLegacy = new EventBasedNetListener();
        var conn1 = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conn2 = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener1.PeerConnectedEvent += p => conn1.TrySetResult(p);
        listener2.PeerConnectedEvent += p => conn2.TrySetResult(p);
        listener2.PeerDisconnectedEvent += (_, info) => replayDisconnected.TrySetResult(info);
        listenerLegacy.PeerDisconnectedEvent += (_, info) => v1Rejected.TrySetResult(info);

        var serverReplay1 = new UdpPacketCipher.ReplayWindow();
        var serverReplay2 = new UdpPacketCipher.ReplayWindow();
        listener1.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                var frame = new byte[reader.AvailableBytes];
                reader.GetBytes(frame, frame.Length);
                var plain = new byte[frame.Length];
                Assert.IsTrue(UdpPacketCipher.TryDecrypt(aes1, UdpPacketCipher.DirectionServerToClient, frame, plain, out var seq, out var len));
                Assert.IsTrue(serverReplay1.TryAccept(seq));
                var r = new NetDataReader(plain, 1, len);
                var type = r.GetByte();
                if (type == 13)
                {
                    slot1 = r.GetUShort();
                    accepted1.TrySetResult(true);
                }
                else if (type == 10)
                {
                    _ = r.GetUShort();
                    _ = r.GetString();
                    joinedName = r.GetString();
                    joined1.TrySetResult(true);
                }
                else if (type == 12)
                {
                    relayedSlot = r.GetUShort();
                    relayedX = r.GetFloat();
                    stateReceived.TrySetResult(true);
                }
            }
            finally { reader.Recycle(); }
        };
        listener2.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                var frame = new byte[reader.AvailableBytes];
                reader.GetBytes(frame, frame.Length);
                var plain = new byte[frame.Length];
                Assert.IsTrue(UdpPacketCipher.TryDecrypt(aes2, UdpPacketCipher.DirectionServerToClient, frame, plain, out var seq, out var len));
                Assert.IsTrue(serverReplay2.TryAccept(seq));
                var r = new NetDataReader(plain, 1, len);
                if (r.GetByte() == 13)
                {
                    slot2 = r.GetUShort();
                    accepted2.TrySetResult(true);
                }
            }
            finally { reader.Recycle(); }
        };

        var client1 = new NetManager(listener1);
        var client2 = new NetManager(listener2);
        var legacyClient = new NetManager(listenerLegacy)
        {
            ReconnectDelay = 100,
            MaxConnectAttempts = 3
        };
        Assert.IsTrue(client1.Start());
        Assert.IsTrue(client2.Start());
        Assert.IsTrue(legacyClient.Start());
        await server.StartAsync(CancellationToken.None);
        try
        {
            // Verify unencrypted v1 connection is silently dropped when RequireEncryptedUdp = true
            var ticket3 = matchmaking.Enqueue(Guid.NewGuid());
            Connect(legacyClient, udpPort, ticket3.ConnectionTicket!);
            var rejectedInfo = await PollUntilCompletedAsync(legacyClient, legacyClient, v1Rejected.Task);
            Assert.AreEqual(DisconnectReason.ConnectionFailed, rejectedInfo.Reason);

            ConnectV2(client1, udpPort, ticket1.TicketId, key1);
            ConnectV2(client2, udpPort, ticket2.TicketId, key2);
            var peer1 = await PollUntilCompletedAsync(client1, client2, conn1.Task);
            var peer2 = await PollUntilCompletedAsync(client1, client2, conn2.Task);

            SendEncryptedHello(peer1, aes1, 1);
            SendEncryptedHello(peer2, aes2, 1);
            Assert.IsTrue(await PollUntilCompletedAsync(client1, client2, accepted1.Task));
            Assert.IsTrue(await PollUntilCompletedAsync(client1, client2, accepted2.Task));
            Assert.IsTrue(await PollUntilCompletedAsync(client1, client2, joined1.Task));
            Assert.AreNotEqual(0, slot1);
            Assert.AreNotEqual(0, slot2);
            Assert.AreEqual("PlayerTwo", joinedName);

            var stateWriter = new NetDataWriter();
            stateWriter.Put(UdpPacketCipher.ProtocolV2Marker);
            stateWriter.Put((byte)2);
            stateWriter.Put(42.5f);
            stateWriter.Put(0f);
            stateWriter.Put(0f);
            stateWriter.Put(0f);
            stateWriter.Put(0f);
            stateWriter.Put(0f);
            stateWriter.Put((byte)0);
            var encryptedFrame = new byte[stateWriter.Length + UdpPacketCipher.HeaderSizeBytes];
            var frameLen = UdpPacketCipher.Encrypt(
                aes2,
                UdpPacketCipher.DirectionClientToServer,
                2,
                stateWriter.Data.AsSpan(0, stateWriter.Length),
                encryptedFrame);
            peer2.Send(encryptedFrame, 0, frameLen, 0, DeliveryMethod.Sequenced);

            Assert.IsTrue(await PollUntilCompletedAsync(client1, client2, stateReceived.Task));
            Assert.AreEqual(slot2, relayedSlot);
            Assert.AreEqual(42.5f, relayedX);

            // Replaying sequence 2 must disconnect peer2
            peer2.Send(encryptedFrame, 0, frameLen, 0, DeliveryMethod.ReliableOrdered);
            var replayInfo = await PollUntilCompletedAsync(client1, client2, replayDisconnected.Task);
            Assert.AreEqual(DisconnectReason.RemoteConnectionClose, replayInfo.Reason);
        }
        finally
        {
            client1.Stop();
            client2.Stop();
            legacyClient.Stop();
            await server.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task PlatformTokenValidator_ValidatesJwtLocally_WithoutHttpCall()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var keyBase64 = Convert.ToBase64String(keyBytes);
        var userId = Guid.NewGuid();
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "plataforma-auth",
            audience: "plataforma-games",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim("player_name", "AcePlayer")
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256)));

        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var validator = new PlatformTokenValidator(
            httpClient,
            Options.Create(new GameAuthOptions
            {
                BaseUrl = "http://127.0.0.1:1",
                JwtIssuer = "plataforma-auth",
                JwtAudience = "plataforma-games",
                JwtSigningKeyBase64 = keyBase64
            }));

        var player = await validator.ValidateAsync(token, CancellationToken.None);
        Assert.IsNotNull(player);
        Assert.AreEqual(userId, player.UserId);
        Assert.AreEqual("AcePlayer", player.PlayerName);
    }

    [TestMethod]
    public async Task WebSocketBridge_CrossPlaysWithEncryptedUdpClientInSameMatch()
    {
        var udpPort = GetAvailableUdpPort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<GameServerOptions>(options =>
        {
            options.UdpPort = udpPort;
            options.PublicAddress = IPAddress.Loopback.ToString();
            options.RequireEncryptedUdp = true;
            options.EnableWebSocketTransport = true;
            options.EnableLegacyPing = false;
        });
        builder.Services.Configure<MatchmakingOptions>(options =>
        {
            options.MinPlayers = 1;
            options.MaxPlayers = 10;
            options.PersistentWorld = true;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("matchmaking-write", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 20,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IMatchmakingService, MatchmakingService>();
        builder.Services.AddSingleton<GameServerHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameServerHostedService>());

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.MapWebSocketGameEndpoint();
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var baseHttpUri = new Uri(addresses!.Addresses.First());
        var wsUri = new Uri($"ws://127.0.0.1:{baseHttpUri.Port}/ws/game");

        var matchmaking = app.Services.GetRequiredService<IMatchmakingService>();
        var wsTicket = matchmaking.Enqueue(Guid.NewGuid(), "WebGlPlayer");
        var udpTicket = matchmaking.Enqueue(Guid.NewGuid(), "DesktopPlayer");
        var udpKey = Convert.FromBase64String(udpTicket.SessionKeyBase64!);
        using var udpAes = new AesGcm(udpKey, UdpPacketCipher.TagSizeBytes);

        // First verify invalid ticket over WebSocket is rejected cleanly
        using (var invalidWs = new ClientWebSocket())
        {
            await invalidWs.ConnectAsync(wsUri, CancellationToken.None);
            await invalidWs.SendAsync(Encoding.UTF8.GetBytes("invalid-ticket"), WebSocketMessageType.Binary, true, CancellationToken.None);
            var closeBuf = new byte[64];
            var closeResult = await invalidWs.ReceiveAsync(closeBuf, CancellationToken.None);
            Assert.AreEqual(WebSocketMessageType.Close, closeResult.MessageType);
        }

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(wsUri, CancellationToken.None);
        await ws.SendAsync(Encoding.UTF8.GetBytes(wsTicket.ConnectionTicket!), WebSocketMessageType.Binary, true, CancellationToken.None);

        var wsHello = new NetDataWriter();
        wsHello.Put((byte)0xD5);
        wsHello.Put((byte)1);
        await ws.SendAsync(wsHello.Data.AsMemory(0, wsHello.Length), WebSocketMessageType.Binary, true, CancellationToken.None);

        // Wait for WS handshake accepted + UDP peer join
        var wsAccepted = false;
        string? wsSawUdpPlayerName = null;
        byte[]? wsReceivedGameData = null;

        var wsReadLoop = Task.Run(async () =>
        {
            var buf = new byte[2048];
            while (ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Close) break;
                var reader = new NetDataReader(buf, 0, res.Count);
                if (reader.PeekByte() != 0xD5) continue;
                reader.GetByte();
                var msgType = reader.GetByte();
                if (msgType == 13)
                {
                    wsAccepted = true;
                }
                else if (msgType == 10)
                {
                    _ = reader.GetString();
                    wsSawUdpPlayerName = reader.GetString();
                }
                else if (msgType == 14)
                {
                    _ = reader.GetString();
                    var data = new byte[reader.AvailableBytes];
                    reader.GetBytes(data, data.Length);
                    wsReceivedGameData = data;
                    break;
                }
            }
        });

        var udpListener = new EventBasedNetListener();
        var udpConnected = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var udpAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var udpSawWsState = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? udpSawWsPlayerName = null;
        var serverReplay = new UdpPacketCipher.ReplayWindow();

        udpListener.PeerConnectedEvent += peer => udpConnected.TrySetResult(peer);
        udpListener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                var frame = new byte[reader.AvailableBytes];
                reader.GetBytes(frame, frame.Length);
                var plain = new byte[frame.Length];
                Assert.IsTrue(UdpPacketCipher.TryDecrypt(udpAes, UdpPacketCipher.DirectionServerToClient, frame, plain, out var seq, out var len));
                Assert.IsTrue(serverReplay.TryAccept(seq));
                var r = new NetDataReader(plain, 1, len);
                var type = r.GetByte();
                if (type == 13) udpAccepted.TrySetResult(true);
                else if (type == 10)
                {
                    _ = r.GetUShort();
                    _ = r.GetString();
                    udpSawWsPlayerName = r.GetString();
                }
                else if (type == 12)
                {
                    _ = r.GetUShort();
                    udpSawWsState.TrySetResult(r.GetFloat());
                }
            }
            finally { reader.Recycle(); }
        };

        var udpClient = new NetManager(udpListener);
        Assert.IsTrue(udpClient.Start());
        try
        {
            ConnectV2(udpClient, udpPort, udpTicket.TicketId, udpKey);
            var udpPeer = await PollUntilCompletedAsync(udpClient, udpClient, udpConnected.Task);
            SendEncryptedHello(udpPeer, udpAes, 1);
            Assert.IsTrue(await PollUntilCompletedAsync(udpClient, udpClient, udpAccepted.Task));

            // WebSocket sends ClientState -> UDP client receives it
            var wsState = new NetDataWriter();
            wsState.Put((byte)0xD5);
            wsState.Put((byte)2);
            wsState.Put(77.25f);
            wsState.Put(1f);
            wsState.Put(2f);
            wsState.Put(90f);
            wsState.Put(0.5f);
            wsState.Put(0.5f);
            wsState.Put((byte)1);
            await ws.SendAsync(wsState.Data.AsMemory(0, wsState.Length), WebSocketMessageType.Binary, true, CancellationToken.None);

            var relayedX = await PollUntilCompletedAsync(udpClient, udpClient, udpSawWsState.Task);
            Assert.AreEqual(77.25f, relayedX);
            Assert.AreEqual("WebGlPlayer", udpSawWsPlayerName);

            // UDP sends ClientGameData -> WebSocket client receives it
            var gameDataWriter = new NetDataWriter();
            gameDataWriter.Put(UdpPacketCipher.ProtocolV2Marker);
            gameDataWriter.Put((byte)3);
            gameDataWriter.Put((byte)0); // broadcast
            gameDataWriter.Put(new byte[] { 7, 8, 9 });
            var encrypted = new byte[gameDataWriter.Length + UdpPacketCipher.HeaderSizeBytes];
            var encLen = UdpPacketCipher.Encrypt(
                udpAes,
                UdpPacketCipher.DirectionClientToServer,
                2,
                gameDataWriter.Data.AsSpan(0, gameDataWriter.Length),
                encrypted);
            udpPeer.Send(encrypted, 0, encLen, 0, DeliveryMethod.ReliableOrdered);

            var sw = Stopwatch.StartNew();
            while (!wsReadLoop.IsCompleted && sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                udpClient.PollEvents();
                await Task.Delay(10);
            }

            Assert.IsTrue(wsReadLoop.IsCompleted, "WebSocket client did not receive relayed GameData in time.");
            Assert.IsTrue(wsAccepted);
            Assert.AreEqual("DesktopPlayer", wsSawUdpPlayerName);
            CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, wsReceivedGameData);
        }
        finally
        {
            udpClient.Stop();
            await app.StopAsync();
        }
    }

    private static void ConnectV2(NetManager client, int port, Guid ticketId, byte[] sessionKey)
    {
        var payload = new byte[UdpPacketCipher.HandshakePayloadSizeBytes];
        payload[0] = UdpPacketCipher.ProtocolV2Marker;
        ticketId.TryWriteBytes(payload.AsSpan(1, 16));
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(17, 8), nowMs);
        UdpPacketCipher.ComputeHandshakeMac(sessionKey, ticketId, nowMs, payload.AsSpan(25, 32));

        var writer = new NetDataWriter();
        writer.Put(payload);
        client.Connect(IPAddress.Loopback.ToString(), port, writer);
    }

    private static void SendEncryptedHello(NetPeer peer, AesGcm aesGcm, ulong sequence)
    {
        var plain = new byte[] { UdpPacketCipher.ProtocolV2Marker, 1 };
        var frame = new byte[plain.Length + UdpPacketCipher.HeaderSizeBytes];
        var len = UdpPacketCipher.Encrypt(aesGcm, UdpPacketCipher.DirectionClientToServer, sequence, plain, frame);
        peer.Send(frame, 0, len, 0, DeliveryMethod.ReliableOrdered);
    }

    [TestMethod]
    public async Task PeerExceedingPacketRateLimit_IsDisconnected()
    {
        var udpPort = GetAvailableUdpPort();
        var matchmaking = new MatchmakingService(
            Options.Create(new MatchmakingOptions { MinPlayers = 1, MaxPlayers = 2, PersistentWorld = true }),
            Options.Create(new GameServerOptions { UdpPort = udpPort, PublicAddress = IPAddress.Loopback.ToString() }),
            TimeProvider.System);
        var ticket = matchmaking.Enqueue(Guid.NewGuid());
        var server = new GameServerHostedService(
            matchmaking,
            Options.Create(new GameServerOptions
            {
                UdpPort = udpPort,
                PublicAddress = IPAddress.Loopback.ToString(),
                MaxPacketsPerSecondPerPeer = 10
            }),
            NullLogger<GameServerHostedService>.Instance);

        var listener = new EventBasedNetListener();
        var connected = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handshake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.PeerConnectedEvent += peer => connected.TrySetResult(peer);
        listener.PeerDisconnectedEvent += (_, info) => disconnected.TrySetResult(info);
        listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                if (reader.PeekByte() == 0xD5)
                {
                    reader.GetByte();
                    if (reader.GetByte() == 13) handshake.TrySetResult(true);
                }
            }
            finally { reader.Recycle(); }
        };

        var client = new NetManager(listener);
        Assert.IsTrue(client.Start());
        await server.StartAsync(CancellationToken.None);
        try
        {
            Connect(client, udpPort, ticket.ConnectionTicket!);
            var peer = await PollUntilCompletedAsync(client, client, connected.Task);
            SendHello(peer);
            Assert.IsTrue(await PollUntilCompletedAsync(client, client, handshake.Task));

            for (var i = 0; i < 15; i++)
            {
                var state = new NetDataWriter();
                state.Put((byte)0xD5);
                state.Put((byte)2);
                state.Put(1f);
                state.Put(0f);
                state.Put(0f);
                state.Put(0f);
                state.Put(0f);
                state.Put(0f);
                state.Put((byte)0);
                peer.Send(state, 0, DeliveryMethod.ReliableOrdered);
            }

            var info = await PollUntilCompletedAsync(client, client, disconnected.Task);
            Assert.AreEqual(DisconnectReason.RemoteConnectionClose, info.Reason);
        }
        finally
        {
            client.Stop();
            await server.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task WebSocketBridge_WithEncryptedV2AndPublicWebSocketUrl_RelaysCompactSlots()
    {
        var udpPort = GetAvailableUdpPort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<GameServerOptions>(options =>
        {
            options.UdpPort = udpPort;
            options.PublicAddress = IPAddress.Loopback.ToString();
            options.PublicWebSocketUrl = "wss://massive.example.com/ws/game";
            options.RequireEncryptedUdp = true;
            options.EnableWebSocketTransport = true;
            options.EnableLegacyPing = false;
        });
        builder.Services.Configure<MatchmakingOptions>(options =>
        {
            options.MinPlayers = 1;
            options.MaxPlayers = 10;
            options.PersistentWorld = true;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("matchmaking-write", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 20,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IMatchmakingService, MatchmakingService>();
        builder.Services.AddSingleton<GameServerHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameServerHostedService>());

        await using var app = builder.Build();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.MapWebSocketGameEndpoint();
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var baseHttpUri = new Uri(addresses!.Addresses.First());
        var wsUri = new Uri($"ws://127.0.0.1:{baseHttpUri.Port}/ws/game");

        var matchmaking = app.Services.GetRequiredService<IMatchmakingService>();
        var wsTicket = matchmaking.Enqueue(Guid.NewGuid(), "WebGlV2");
        Assert.AreEqual("wss://massive.example.com/ws/game", wsTicket.WebSocketUrl);
        Assert.IsNotNull(wsTicket.SessionKeyBase64);

        var wsKey = Convert.FromBase64String(wsTicket.SessionKeyBase64);
        using var wsAes = new AesGcm(wsKey, UdpPacketCipher.TagSizeBytes);

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(wsUri, CancellationToken.None);

        var handshake = new byte[UdpPacketCipher.HandshakePayloadSizeBytes];
        handshake[0] = UdpPacketCipher.ProtocolV2Marker;
        wsTicket.TicketId.TryWriteBytes(handshake.AsSpan(1, 16));
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        BinaryPrimitives.WriteInt64LittleEndian(handshake.AsSpan(17, 8), nowMs);
        UdpPacketCipher.ComputeHandshakeMac(wsKey, wsTicket.TicketId, nowMs, handshake.AsSpan(25, 32));
        await ws.SendAsync(handshake, WebSocketMessageType.Binary, true, CancellationToken.None);

        var helloPlain = new byte[] { UdpPacketCipher.ProtocolV2Marker, 1 };
        var helloFrame = new byte[helloPlain.Length + UdpPacketCipher.HeaderSizeBytes];
        var helloLen = UdpPacketCipher.Encrypt(wsAes, UdpPacketCipher.DirectionClientToServer, 1, helloPlain, helloFrame);
        await ws.SendAsync(helloFrame.AsMemory(0, helloLen), WebSocketMessageType.Binary, true, CancellationToken.None);

        ushort assignedSlot = 0;
        var replay = new UdpPacketCipher.ReplayWindow();
        var buf = new byte[2048];
        var res = await ws.ReceiveAsync(buf, CancellationToken.None);
        Assert.AreEqual(WebSocketMessageType.Binary, res.MessageType);

        var plain = new byte[res.Count];
        Assert.IsTrue(UdpPacketCipher.TryDecrypt(wsAes, UdpPacketCipher.DirectionServerToClient, buf.AsSpan(0, res.Count), plain, out var seq, out var plainLen));
        Assert.IsTrue(replay.TryAccept(seq));

        var reader = new NetDataReader(plain, 1, plainLen);
        Assert.AreEqual(13, reader.GetByte());
        assignedSlot = reader.GetUShort();
        Assert.AreNotEqual(0, assignedSlot);

        await app.StopAsync();
    }

    [TestMethod]
    public void CorsOriginHelper_ParsesValidHttpAndHttpsOrigins()
    {
        var origins = CorsOriginHelper.ParseAllowedOrigins(
            "https://plataforma.crefeed.com.br/subpath, http://localhost:3000/; invalid-url; ftp://files.example.com");
        CollectionAssert.AreEqual(
            new[] { "https://plataforma.crefeed.com.br", "http://localhost:3000" },
            origins);
        Assert.IsTrue(CorsOriginHelper.IsLoopbackOrigin("http://127.0.0.1:61234"));
        Assert.IsFalse(CorsOriginHelper.IsLoopbackOrigin("https://evil.example.com"));
    }

    private static Task<T> PollUntilCompletedAsync<T>(
        NetManager firstClient,
        NetManager secondClient,
        Task<T> task) =>
        PollClientsUntilCompletedAsync(task, firstClient, secondClient);

    private static Task<T> PollUntilCompletedAsync<T>(
        NetManager firstClient,
        NetManager secondClient,
        NetManager thirdClient,
        Task<T> task) =>
        PollClientsUntilCompletedAsync(task, firstClient, secondClient, thirdClient);

    private static async Task<T> PollClientsUntilCompletedAsync<T>(
        Task<T> task,
        params NetManager[] clients)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!task.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            foreach (var client in clients)
            {
                client.PollEvents();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        foreach (var client in clients)
        {
            client.PollEvents();
        }

        Assert.IsTrue(task.IsCompleted, "The UDP event did not arrive before the timeout.");
        return await task;
    }

    private static int GetAvailableUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static void Connect(NetManager client, int port, string ticket)
    {
        var connectData = new NetDataWriter();
        connectData.Put(ticket);
        client.Connect(IPAddress.Loopback.ToString(), port, connectData);
    }

    private static void SendHello(NetPeer peer)
    {
        var hello = new NetDataWriter();
        hello.Put((byte)0xD5);
        hello.Put((byte)1);
        peer.Send(hello, 0, DeliveryMethod.ReliableOrdered);
    }

    private sealed record RelayedState(
        string PlayerId,
        float X,
        float Y,
        float Z,
        float RotationY,
        float RunX,
        float RunZ);
}
