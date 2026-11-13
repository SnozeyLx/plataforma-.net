using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Services;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    private static async Task<T> PollUntilCompletedAsync<T>(
        NetManager firstClient,
        NetManager secondClient,
        Task<T> task)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!task.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            firstClient.PollEvents();
            secondClient.PollEvents();
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        firstClient.PollEvents();
        secondClient.PollEvents();
        Assert.IsTrue(task.IsCompleted, "The UDP event did not arrive before the timeout.");
        return await task;
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
