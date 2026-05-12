using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane.Redis;
using ProbahoSSE.Models;
using StackExchange.Redis;

namespace ProbahoSSE.RedisStream;

/// <summary>
/// A <see cref="BackgroundService"/> that owns the Redis Stream <c>XREADGROUP</c> poll loop
/// for this server instance. On startup it creates a unique Consumer Group so every instance
/// independently receives every message (fan-out). Received messages are forwarded to the
/// local <see cref="IProbahoSseManager"/> to broadcast to connected SSE clients.
/// </summary>
public sealed class RedisStreamListenerService : BackgroundService
{
    private readonly RedisStreamBackplane _backplane;
    private readonly IProbahoSseManager _manager;
    private readonly ILogger<RedisStreamListenerService> _logger;

    // Unique per server instance — intentional so every instance gets all messages (fan-out).
    private readonly string _consumerGroup = $"probaho-group-{Guid.NewGuid():N}";
    private readonly string _consumerName = $"probaho-consumer-{Guid.NewGuid():N}";

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
        var db = _backplane.GetDatabase();

        // Create this instance's unique Consumer Group starting at the latest stream entry ($).
        // BUSYGROUP means the group already exists — safe to ignore.
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                _backplane.StreamKey, _consumerGroup, StreamPosition.NewMessages).ConfigureAwait(false);

            _logger.LogInformation(
                "[RedisStream] Consumer group created. Stream={Stream} Group={Group} Consumer={Consumer}",
                _backplane.StreamKey, _consumerGroup, _consumerName);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Already exists — harmless on restart.
        }

        _logger.LogInformation("[RedisStream] Read loop started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // XREADGROUP: fetch up to 100 entries not yet delivered to this consumer.
                var entries = await db.StreamReadGroupAsync(
                    _backplane.StreamKey,
                    _consumerGroup,
                    _consumerName,
                    StreamPosition.NewMessages,
                    count: 100).ConfigureAwait(false);

                if (entries is null || entries.Length == 0)
                {
                    // No new messages — short back-off before polling again.
                    await Task.Delay(100, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var entry in entries)
                {
                    var payloadField = entry["payload"];
                    if (payloadField.IsNullOrEmpty)
                    {
                        // ACK malformed entries so they don't clog the PEL.
                        await db.StreamAcknowledgeAsync(_backplane.StreamKey, _consumerGroup, entry.Id)
                            .ConfigureAwait(false);
                        continue;
                    }

                    var sseEvent = RedisEventSerializer.Deserialize(payloadField!);
                    if (sseEvent is null)
                    {
                        _logger.LogWarning("[RedisStream] Failed to deserialize entry {Id}.", entry.Id);
                        await db.StreamAcknowledgeAsync(_backplane.StreamKey, _consumerGroup, entry.Id)
                            .ConfigureAwait(false);
                        continue;
                    }

                    var group = sseEvent.Group;

                    if (string.IsNullOrEmpty(group))
                    {
                        // No group set — drop to prevent accidental fan-out to all users.
                        _logger.LogWarning(
                            "[RedisStream] Entry {EntryId} (event id={EventId}) has no group — dropped to prevent data leak. " +
                            "Call PublishToAllAsync for intentional fan-out.", entry.Id, sseEvent.Id);
                        await db.StreamAcknowledgeAsync(_backplane.StreamKey, _consumerGroup, entry.Id)
                            .ConfigureAwait(false);
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

                    // ACK after successful delivery to remove from the Pending Entries List (PEL).
                    await db.StreamAcknowledgeAsync(_backplane.StreamKey, _consumerGroup, entry.Id)
                        .ConfigureAwait(false);
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
}

