using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Games.MassiveServer.Contracts;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Games.MassiveServer.Tests;

[TestClass]
[TestCategory("Integration")]
public sealed class GameServerIntegrationTests
{
    [TestMethod]
    public async Task MatchmakingTicket_ConnectsToUdpServerAndReceivesPong()
    {
        if (Environment.GetEnvironmentVariable("GAMESERVER_INTEGRATION_TESTS") != "1")
        {
            Assert.Inconclusive("Set GAMESERVER_INTEGRATION_TESTS=1 to run against local Docker services.");
        }

        var authApiUrl = Environment.GetEnvironmentVariable("AUTH_API_BASE_URL") ?? "http://localhost:5000";
        var gameApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API_BASE_URL") ?? "http://localhost:5001";
        using var httpClient = new HttpClient();

        var firstAccessToken = await RegisterAndLoginAsync(httpClient, authApiUrl);
        var secondAccessToken = await RegisterAndLoginAsync(httpClient, authApiUrl);
        var firstTicket = await EnqueueAsync(httpClient, gameApiUrl, firstAccessToken);
        var secondTicket = await EnqueueAsync(httpClient, gameApiUrl, secondAccessToken);
        var firstReady = await WaitForMatchAsync(httpClient, gameApiUrl, firstAccessToken, firstTicket.TicketId);
        var secondReady = await WaitForMatchAsync(httpClient, gameApiUrl, secondAccessToken, secondTicket.TicketId);

        Assert.AreEqual("ready", firstReady.Status);
        Assert.AreEqual(firstReady.MatchId, secondReady.MatchId);
        Assert.IsNotNull(firstReady.ConnectionTicket);
        Assert.IsNotNull(firstReady.ServerAddress);
        Assert.IsNotNull(firstReady.ServerPort);

        var listener = new EventBasedNetListener();
        var connected = new TaskCompletionSource<NetPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pong = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.PeerConnectedEvent += peer => connected.TrySetResult(peer);
        listener.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try
            {
                pong.TrySetResult(reader.GetString());
            }
            finally
            {
                reader.Recycle();
            }
        };

        var client = new NetManager(listener);
        Assert.IsTrue(client.Start());
        try
        {
            var connectData = new NetDataWriter();
            connectData.Put(firstReady.ConnectionTicket);
            client.Connect(firstReady.ServerAddress, firstReady.ServerPort.Value, connectData);

            var peer = await PollUntilCompletedAsync(client, connected.Task);
            var ping = new NetDataWriter();
            ping.Put("ping");
            peer.Send(ping, 0, DeliveryMethod.ReliableOrdered);

            Assert.AreEqual("pong", await PollUntilCompletedAsync(client, pong.Task));
        }
        finally
        {
            client.Stop();
        }
    }

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string authApiUrl)
    {
        var email = $"multiplayer-{Guid.NewGuid():N}@example.test";
        var password = $"GameTest!{Guid.NewGuid():N}";
        using var registration = await client.PostAsJsonAsync(
            $"{authApiUrl}/api/auth/register",
            new { email, password });
        registration.EnsureSuccessStatusCode();

        using var login = await client.PostAsJsonAsync(
            $"{authApiUrl}/api/auth/login",
            new { email, password });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.IsNotNull(tokens);
        return tokens.AccessToken;
    }

    private static async Task<MatchmakingTicketResponse> EnqueueAsync(
        HttpClient client,
        string gameApiUrl,
        string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{gameApiUrl}/api/matchmaking/tickets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(System.Net.HttpStatusCode.Accepted, response.StatusCode);

        var ticket = await response.Content.ReadFromJsonAsync<MatchmakingTicketResponse>();
        Assert.IsNotNull(ticket);
        return ticket;
    }

    private static async Task<MatchmakingTicketResponse> WaitForMatchAsync(
        HttpClient client,
        string gameApiUrl,
        string accessToken,
        Guid ticketId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{gameApiUrl}/api/matchmaking/tickets/{ticketId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var ticket = await response.Content.ReadFromJsonAsync<MatchmakingTicketResponse>(
                timeout.Token);
            Assert.IsNotNull(ticket);
            if (ticket.Status != "queued")
            {
                return ticket;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    private static async Task<T> PollUntilCompletedAsync<T>(NetManager client, Task<T> task)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!task.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            client.PollEvents();
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        client.PollEvents();
        Assert.IsTrue(task.IsCompleted, "The UDP event did not arrive before the timeout.");
        return await task;
    }

    private sealed record AuthTokenResponse(string AccessToken);
}
