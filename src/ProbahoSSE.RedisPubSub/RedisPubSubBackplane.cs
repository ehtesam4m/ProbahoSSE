using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
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
    private readonly RedisPubSubOptions _options;
    private readonly ILogger<RedisPubSubBackplane> _logger;
    private readonly ProbahoSseMetrics _metrics;

    /// <summary>The Redis channel name all instances publish/subscribe to.</summary>
    internal string ChannelName { get; }

    /// <summary>Initializes the Redis Pub/Sub backplane.</summary>
    public RedisPubSubBackplane(
        IConnectionMultiplexer redis,
        IOptions<RedisPubSubOptions> options,
        ILogger<RedisPubSubBackplane> logger,
        ProbahoSseMetrics metrics)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        ChannelName = $"{_options.ChannelPrefix}:sse";
    }

    /// <inheritdoc />
    public Task PublishToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = group }
            : sseEvent;
        return PublishToChannelAsync(stamped);
    }

    /// <inheritdoc />
    public Task PublishToAllAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = ProbahoSseGroups.Broadcast }
            : sseEvent;
        return PublishToChannelAsync(stamped);
    }

    private async Task PublishToChannelAsync(IProbahoSseEvent sseEvent)
    {
        var subscriber = _redis.GetSubscriber();
        var payload    = SseEventSerializer.Serialize(sseEvent);
        _logger.LogDebug("[PubSub] Publishing event id={Id} group={Group} to channel {Channel}",
            sseEvent.Id, sseEvent.Group, ChannelName);

        var start   = Stopwatch.GetTimestamp();
        var success = false;
        try
        {
            await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload).ConfigureAwait(false);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PubSub] Publish failed for event id={Id}.", sseEvent.Id);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (success)
                _metrics.RecordMessageSent("redis-pubsub");
            else
                _metrics.RecordMessageFailed("redis-pubsub");

            _metrics.RecordPublish("redis-pubsub", elapsed); // legacy histogram keeps working
        }
    }

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
