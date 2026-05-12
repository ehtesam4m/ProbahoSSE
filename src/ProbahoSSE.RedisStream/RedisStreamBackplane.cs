using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane.Redis;
using ProbahoSSE.Models;
using StackExchange.Redis;
namespace ProbahoSSE.RedisStream;

/// <summary>
/// An <see cref="IProbahoSseBackplane"/> implementation using Redis Streams for at-least-once delivery
/// with per-instance consumer groups and Last-Event-ID message replay support.
/// The read loop and fan-out logic is owned by <see cref="RedisStreamListenerService"/>.
/// </summary>
public sealed class RedisStreamBackplane : IProbahoSseBackplane, IProbahoSseReplayable, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisBackplaneOptions _options;
    private readonly ILogger<RedisStreamBackplane> _logger;

    internal string StreamKey { get; }

    /// <summary>Initializes the Redis Stream backplane.</summary>
    public RedisStreamBackplane(
        IConnectionMultiplexer redis,
        IOptions<RedisBackplaneOptions> options,
        ILogger<RedisStreamBackplane> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        StreamKey = $"{_options.ChannelPrefix}:stream";
    }

    internal IDatabase GetDatabase() => _redis.GetDatabase();
    internal RedisBackplaneOptions Options => _options;

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
        var payload = RedisEventSerializer.Serialize(sseEvent);

        _logger.LogDebug("[RedisStream] Publishing event id={Id} group={Group} to stream {Stream}",
            sseEvent.Id, sseEvent.Group, StreamKey);

        // XADD with XTRIM MAXLEN ~ to prevent unbounded memory growth.
        await db.StreamAddAsync(
            StreamKey,
            [new NameValueEntry("payload", payload)],
            maxLength: _options.StreamMaxLength,
            useApproximateMaxLength: true).ConfigureAwait(false);
    }


    /// <summary>
    /// Replays all stream messages since <paramref name="lastEventId"/> directly to <paramref name="handler"/>.
    /// Used to flush missed events to a reconnecting SSE client before live streaming resumes.
    /// </summary>
    /// <param name="lastEventId">The Redis Stream entry ID from the client's Last-Event-ID header.</param>
    /// <param name="handler">Callback invoked for each replayed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task ReplayFromAsync(
        string lastEventId,
        Func<IProbahoSseEvent, Task> handler,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        StreamEntry[] history;
        try
        {
            // XRANGE from exclusive lastEventId: use (lastEventId to skip the ID itself
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

        // Skip the first entry if it exactly equals lastEventId (XRANGE is inclusive on min)
        foreach (var entry in history)
        {
            if (entry.Id == lastEventId) continue;

            var payloadField = entry["payload"];
            if (payloadField.IsNullOrEmpty) continue;

            var sseEvent = RedisEventSerializer.Deserialize(payloadField!);
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
