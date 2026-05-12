using System.Threading.Channels;
using ProbahoSSE.Abstractions;

namespace ProbahoSSE.Core;

/// <summary>
/// Represents a single active SSE client connection backed by a <see cref="Channel{T}"/>.
/// </summary>
internal sealed class SseConnection : IProbahoSseConnection, IDisposable
{
    private readonly Channel<IProbahoSseEvent> _channel;
    private bool _disposed;

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public string? Group { get; }

    /// <inheritdoc />
    public bool IsConnected => !_disposed && _channel.Reader.Completion.IsCompleted == false;

    /// <summary>Initializes a new SSE connection.</summary>
    public SseConnection(string? group)
    {
        ConnectionId = Guid.NewGuid().ToString("N");
        Group = group;
        _channel = Channel.CreateBounded<IProbahoSseEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public ValueTask SendAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        if (_disposed) return ValueTask.CompletedTask;
        return _channel.Writer.TryWrite(sseEvent)
            ? ValueTask.CompletedTask
            : _channel.Writer.WriteAsync(sseEvent, cancellationToken);
    }

    /// <summary>
    /// Direct access to the channel reader for low-level racing against timers.
    /// </summary>
    public ChannelReader<IProbahoSseEvent> ChannelReader => _channel.Reader;

    /// <summary>
    /// Returns an async enumerable of events for streaming to the HTTP response.
    /// </summary>
    public IAsyncEnumerable<IProbahoSseEvent> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Completes the channel, signalling the end of the stream.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
