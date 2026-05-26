using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
using ProbahoSSE.Models;
using StackExchange.Redis;

namespace ProbahoSSE.RedisStream;

/// <summary>
/// An <see cref="IProbahoSseBackplane"/> implementation using Redis Streams for live fan-out
/// and Last-Event-ID message replay support. The live read loop is owned by
/// <see cref="RedisStreamListenerService"/> using <c>XREAD</c> without consumer groups.
/// Replay of missed events uses <c>XRANGE</c> via <see cref="ReplayFromAsync"/>.
/// </summary>
public sealed class RedisStreamBackplane : IProbahoSseBackplane, IProbahoSseReplayable, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisStreamOptions _options;
    private readonly ILogger<RedisStreamBackplane> _logger;
    private readonly ProbahoSseMetrics _metrics;

    internal string StreamKey { get; }

    /// <summary>Initializes the Redis Stream backplane.</summary>
    public RedisStreamBackplane(
        IConnectionMultiplexer redis,
        IOptions<RedisStreamOptions> options,
        ILogger<RedisStreamBackplane> logger,
        ProbahoSseMetrics metrics)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        StreamKey = $"{_options.ChannelPrefix}:stream";
    }

    internal IDatabase GetDatabase() => _redis.GetDatabase();
    internal int PollingIntervalMs => _options.StreamPollingIntervalMs;

    /// <inheritdoc />
    public Task PublishToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = group }
            : sseEvent;
        return PublishToStreamAsync(stamped, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishToAllAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = ProbahoSseGroups.Broadcast }
            : sseEvent;
        return PublishToStreamAsync(stamped, cancellationToken);
    }

    private async Task PublishToStreamAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var payload = SseEventSerializer.Serialize(sseEvent);

        _logger.LogDebug("[RedisStream] Publishing event id={Id} group={Group} to stream {Stream}",
            sseEvent.Id, sseEvent.Group, StreamKey);

        var start = Stopwatch.GetTimestamp();
        try
        {
            await db.StreamAddAsync(
                StreamKey,
                [new NameValueEntry("payload", payload)],
                maxLength: _options.StreamMaxLength,
                useApproximateMaxLength: true).ConfigureAwait(false);
        }
        finally
        {
            _metrics.RecordPublish("redis-stream", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task ReplayFromAsync(
        string lastEventId,
        Func<IProbahoSseEvent, Task> handler,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        StreamEntry[] history;
        try
        {
            history = await db.StreamRangeAsync(
                StreamKey,
                minId: lastEventId,
                maxId: "+",
                count: _options.StreamMaxLength).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RedisStream] Failed to replay from {LastEventId}", lastEventId);
            return;
        }

        foreach (var entry in history)
        {
            if (entry.Id == lastEventId) continue;

            var payloadField = entry["payload"];
            if (payloadField.IsNullOrEmpty) continue;

            var sseEvent = SseEventSerializer.Deserialize(payloadField!);
            if (sseEvent is null) continue;

            try
            {
                await handler(sseEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RedisStream] Error replaying event {EntryId}", entry.Id);
            }
        }

        _logger.LogInformation("[RedisStream] Replayed {Count} messages from id={From}",
            history.Count(e => e.Id != lastEventId), lastEventId);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
