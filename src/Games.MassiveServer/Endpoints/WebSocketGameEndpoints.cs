using System.Net.WebSockets;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Services;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Endpoints;

/// <summary>Maps the WebSocket bridge endpoint used by browser/WebGL clients to join the game server.</summary>
public static class WebSocketGameEndpoints
{
    /// <summary>Adds the <c>/ws/game</c> WebSocket endpoint to the game server host.</summary>
    public static IEndpointRouteBuilder MapWebSocketGameEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/ws/game", HandleWebSocketAsync)
            .WithName("ConnectGameWebSocket")
            .WithSummary("Connect to a match over WebSocket (WebGL bridge)")
            .RequireRateLimiting("matchmaking-write");

        return endpoints;
    }

    private static async Task HandleWebSocketAsync(
        HttpContext context,
        GameServerHostedService gameServer,
        IOptions<GameServerOptions> options,
        IHostApplicationLifetime hostLifetime,
        ILoggerFactory loggerFactory)
    {
        if (!options.Value.EnableWebSocketTransport)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        var logger = loggerFactory.CreateLogger("Games.MassiveServer.WebSocketBridge");

        var connectBuffer = new byte[gameServer.MaxConnectionPayloadSize];
        int connectLength;
        using (var handshakeTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted))
        {
            handshakeTimeoutCts.CancelAfter(TimeSpan.FromSeconds(options.Value.HandshakeTimeoutSeconds));
            try
            {
                connectLength = await ReceiveBinaryMessageAsync(
                    webSocket,
                    connectBuffer,
                    handshakeTimeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                await SafeCloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "Handshake timeout.");
                return;
            }
            catch (InvalidOperationException)
            {
                await SafeCloseAsync(webSocket, WebSocketCloseStatus.InvalidPayloadData, "Invalid connect frame.");
                return;
            }
            catch (WebSocketException)
            {
                return;
            }
        }

        if (connectLength <= 0)
        {
            await SafeCloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "Missing connection ticket.");
            return;
        }

        if (!gameServer.TryAcceptWebSocketConnection(
                remoteIp,
                connectBuffer.AsSpan(0, connectLength),
                out var channel))
        {
            logger.LogDebug("Rejected a WebSocket game connection from {RemoteIp}.", remoteIp);
            await SafeCloseAsync(webSocket, WebSocketCloseStatus.PolicyViolation, "Invalid or rate-limited ticket.");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            hostLifetime.ApplicationStopping,
            channel.CancellationToken);

        try
        {
            var sendTask = RunSendLoopAsync(webSocket, channel, linkedCts.Token);
            var receiveTask = RunReceiveLoopAsync(
                webSocket,
                channel,
                gameServer,
                gameServer.MaxReceivePayloadSize,
                linkedCts.Token);

            await Task.WhenAny(sendTask, receiveTask);
            channel.RequestDisconnect();
            await Task.WhenAll(
                IgnoreCancellationAsync(sendTask),
                IgnoreCancellationAsync(receiveTask));
            await SafeCloseAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Disconnected.");
        }
        finally
        {
            gameServer.EnqueueWebSocketDisconnect(channel);
        }
    }

    private static async Task RunReceiveLoopAsync(
        WebSocket webSocket,
        WebSocketPeerChannel channel,
        GameServerHostedService gameServer,
        int maxMessageBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maxMessageBytes];
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            int length;
            try
            {
                length = await ReceiveBinaryMessageAsync(webSocket, buffer, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                channel.RequestDisconnect();
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }

            if (length < 0)
            {
                return;
            }

            if (length == 0)
            {
                channel.RequestDisconnect();
                return;
            }

            var packetCopy = new byte[length];
            Buffer.BlockCopy(buffer, 0, packetCopy, 0, length);
            gameServer.EnqueueWebSocketPacket(channel, packetCopy);
        }
    }

    private static async Task RunSendLoopAsync(
        WebSocket webSocket,
        WebSocketPeerChannel channel,
        CancellationToken cancellationToken)
    {
        await foreach (var payload in channel.OutboundReader.ReadAllAsync(cancellationToken))
        {
            if (webSocket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await webSocket.SendAsync(
                    payload,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken);
            }
            catch (WebSocketException)
            {
                return;
            }
        }
    }

    private static async Task<int> ReceiveBinaryMessageAsync(
        WebSocket webSocket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalBytes = 0;
        while (true)
        {
            var result = await webSocket.ReceiveAsync(
                buffer.AsMemory(totalBytes, buffer.Length - totalBytes),
                cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return -1;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidOperationException("Only binary WebSocket frames are supported.");
            }

            totalBytes += result.Count;
            if (result.EndOfMessage)
            {
                return totalBytes;
            }

            if (totalBytes >= buffer.Length)
            {
                throw new InvalidOperationException("WebSocket message exceeded maximum allowed size.");
            }
        }
    }

    private static async Task SafeCloseAsync(
        WebSocket webSocket,
        WebSocketCloseStatus status,
        string description)
    {
        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await webSocket.CloseAsync(status, description, closeCts.Token);
            }
            catch
            {
                // Best-effort close handshake on teardown.
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }
}
