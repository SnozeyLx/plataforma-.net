using System.Threading.Channels;
using LiteNetLib;

namespace Games.MassiveServer.Services;

/// <summary>Buffers outbound binary frames for a connected WebSocket game client without blocking the relay loop.</summary>
public sealed class WebSocketPeerChannel : IDisposable
{
    private const int TotalChannelCapacity = 512;
    private const int StateBackpressureLimit = 256;

    private readonly Channel<byte[]> _outboundChannel = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(TotalChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly CancellationTokenSource _disconnectCts = new();
    private int _closed;

    /// <summary>Gets a token canceled when the WebSocket session closes or is aborted by the server.</summary>
    public CancellationToken CancellationToken => _disconnectCts.Token;

    /// <summary>Reads outbound binary frames sequentially for transmission on the WebSocket.</summary>
    public ChannelReader<byte[]> OutboundReader => _outboundChannel.Reader;

    /// <summary>Enqueues a copy of an outbound frame for asynchronous WebSocket delivery.</summary>
    public void EnqueueSend(ReadOnlySpan<byte> payload, DeliveryMethod deliveryMethod)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        if (deliveryMethod is DeliveryMethod.Sequenced or DeliveryMethod.Unreliable)
        {
            if (_outboundChannel.Reader.Count >= StateBackpressureLimit)
            {
                return;
            }

            _outboundChannel.Writer.TryWrite(payload.ToArray());
            return;
        }

        if (!_outboundChannel.Writer.TryWrite(payload.ToArray()))
        {
            RequestDisconnect();
        }
    }

    /// <summary>Requests disconnection of the underlying WebSocket transport.</summary>
    public void RequestDisconnect()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _outboundChannel.Writer.TryComplete();
            _disconnectCts.Cancel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        RequestDisconnect();
        _disconnectCts.Dispose();
    }
}
