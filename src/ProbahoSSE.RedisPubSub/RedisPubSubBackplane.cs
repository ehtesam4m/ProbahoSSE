using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane.Redis;
using ProbahoSSE.Models;
using StackExchange.Redis;

namespace ProbahoSSE.RedisPubSub;

/// <summary>
/// An <see cref="IProbahoSseBackplane"/> implementation using Redis Pub/Sub for low-latency,
/// fire-and-forget broadcasting across multiple server instances.
/// Subscription is handled by <see cref="RedisPubSubListenerService"/>.
/// </summary>
public sealed class RedisPubSubBackplane : IProbahoSseBackplane, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisBackplaneOptions _options;
    private readonly ILogger<RedisPubSubBackplane> _logger;

    /// <summary>The Redis channel name all instances publish/subscribe to.</summary>
    internal string ChannelName { get; }

    /// <summary>Initializes the Redis Pub/Sub backplane.</summary>
    public RedisPubSubBackplane(
        IConnectionMultiplexer redis,
        IOptions<RedisBackplaneOptions> options,
        ILogger<RedisPubSubBackplane> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        ChannelName = $"{_options.ChannelPrefix}:sse";
    }

    /// <inheritdoc />
    public Task PublishToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        // Stamp the group onto the event before serializing so the listener can route it.
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = group }
            : sseEvent;
        return PublishToChannelAsync(stamped);
    }

    /// <inheritdoc />
    public Task PublishToAllAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        // Stamp the broadcast sentinel so the listener calls BroadcastAsync explicitly.
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = ProbahoSseGroups.Broadcast }
            : sseEvent;
        return PublishToChannelAsync(stamped);
    }

    private async Task PublishToChannelAsync(IProbahoSseEvent sseEvent)
    {
        var subscriber = _redis.GetSubscriber();
        var payload = RedisEventSerializer.Serialize(sseEvent);
        _logger.LogDebug("[PubSub] Publishing event id={Id} group={Group} to channel {Channel}",
            sseEvent.Id, sseEvent.Group, ChannelName);
        await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload).ConfigureAwait(false);
    }

    /// <summary>
    /// Not used directly — subscription is managed by <see cref="RedisPubSubListenerService"/>.
    /// Calling this will throw <see cref="NotSupportedException"/>.
    /// </summary>

    internal ISubscriber GetSubscriber() => _redis.GetSubscriber();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _redis.GetSubscriber().UnsubscribeAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while unsubscribing from Redis Pub/Sub.");
        }
    }
}
