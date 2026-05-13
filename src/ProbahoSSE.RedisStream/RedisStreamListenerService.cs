using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
using ProbahoSSE.Models;
using StackExchange.Redis;

namespace ProbahoSSE.RedisStream;

/// <summary>
/// A <see cref="BackgroundService"/> that owns the Redis Stream <c>XREAD</c> loop
/// for this server instance. Uses <c>XREAD</c> without consumer groups so every instance
/// independently reads all messages — no stale group accumulation on autoscaling.
/// The in-memory <c>_lastId</c> tracks the stream position for the lifetime of this process.
/// Replay of missed events for reconnecting browsers is handled separately by
/// <see cref="RedisStreamBackplane.ReplayFromAsync"/> via <c>XRANGE</c>.
/// </summary>
public sealed class RedisStreamListenerService : BackgroundService
{
    private readonly RedisStreamBackplane _backplane;
    private readonly IProbahoSseManager _manager;
    private readonly ILogger<RedisStreamListenerService> _logger;

    // Tracks the last stream entry ID read by this instance.
    // Initialized in ExecuteAsync to the stream's current tail so only new messages are delivered.
    private string _lastId = "0-0";

    /// <summary>Initializes the listener service.</summary>
    public RedisStreamListenerService(
        RedisStreamBackplane backplane,
        IProbahoSseManager manager,
        ILogger<RedisStreamListenerService> logger)
    {
        _backplane = backplane;
        _manager = manager;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RedisStream] Read loop started (XREAD, no consumer groups).");

        // Resolve the current tail of the stream so we only deliver new messages going forward.
        // This is equivalent to XREAD $ — we capture the last ID once at startup.
        var db = _backplane.GetDatabase();
        try
        {
            var info = await db.StreamInfoAsync(_backplane.StreamKey).ConfigureAwait(false);
            _lastId = info.LastEntry.Id.ToString();
        }
        catch
        {
            // Stream doesn't exist yet — start from the very beginning.
            _lastId = "0-0";
        }

        _logger.LogInformation("[RedisStream] Starting from stream position {LastId}.", _lastId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // XREAD: fetch up to 100 entries after _lastId.
                // StackExchange.Redis does not support BLOCK on the shared multiplexer,
                // so we poll with a short back-off when no messages are available.
                var entries = await db.StreamReadAsync(
                    _backplane.StreamKey,
                    (RedisValue)_lastId,
                    count: 100).ConfigureAwait(false);

                if (entries is null || entries.Length == 0)
                {
                    // No new messages — back-off before polling again (configurable via StreamPollingIntervalMs).
                    await Task.Delay(_backplane.PollingIntervalMs, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var entry in entries)
                {
                    // Always advance _lastId so next XREAD starts after this entry.
                    _lastId = entry.Id.ToString();

                    var payloadField = entry["payload"];
                    if (payloadField.IsNullOrEmpty)
                    {
                        _logger.LogWarning("[RedisStream] Entry {Id} has no payload — skipped.", entry.Id);
                        continue;
                    }

                    var sseEvent = SseEventSerializer.Deserialize(payloadField!);
                    if (sseEvent is null)
                    {
                        _logger.LogWarning("[RedisStream] Failed to deserialize entry {Id}.", entry.Id);
                        continue;
                    }

                    var group = sseEvent.Group;

                    if (string.IsNullOrEmpty(group))
                    {
                        _logger.LogWarning(
                            "[RedisStream] Entry {EntryId} (event id={EventId}) has no group — dropped to prevent data leak. " +
                            "Call PublishToAllAsync for intentional fan-out.", entry.Id, sseEvent.Id);
                        continue;
                    }

                    _logger.LogDebug(
                        "[RedisStream] Forwarding entry {EntryId} (event id={EventId}) group={Group} to local connections.",
                        entry.Id, sseEvent.Id, group);

                    try
                    {
                        if (group == ProbahoSseGroups.Broadcast)
                            await _manager.BroadcastAsync(sseEvent, stoppingToken).ConfigureAwait(false);
                        else
                            await _manager.SendToGroupAsync(group, sseEvent, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[RedisStream] Error broadcasting entry {Id}.", entry.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RedisStream] Error in read loop. Retrying in 1s...");
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("[RedisStream] Read loop stopped.");
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to clean up in Redis — no consumer groups were created.
        _logger.LogInformation("[RedisStream] Read loop stopping.");
        return base.StopAsync(cancellationToken);
    }
}
