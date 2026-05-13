using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
using ProbahoSSE.Models;
using StackExchange.Redis;

namespace ProbahoSSE.RedisPubSub;

/// <summary>
/// A <see cref="BackgroundService"/> that holds the long-lived Redis Pub/Sub subscription
/// and forwards every received message to all locally connected SSE clients.
/// This is the proper home for a subscription — not a fire-and-forget task or an infinite delay.
/// </summary>
public sealed class RedisPubSubListenerService : BackgroundService
{
    private readonly RedisPubSubBackplane _backplane;
    private readonly IProbahoSseManager _manager;
    private readonly ILogger<RedisPubSubListenerService> _logger;

    /// <summary>Initializes the listener service.</summary>
    public RedisPubSubListenerService(
        RedisPubSubBackplane backplane,
        IProbahoSseManager manager,
        ILogger<RedisPubSubListenerService> logger)
    {
        _backplane = backplane;
        _manager = manager;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _backplane.GetSubscriber();
        var channel = RedisChannel.Literal(_backplane.ChannelName);

        _logger.LogInformation("[RedisPubSub] Subscribing to channel '{Channel}'", _backplane.ChannelName);

        // Use the async-handler overload (Func<RedisChannel, RedisValue, Task>) to avoid
        // blocking the StackExchange.Redis I/O callback thread when broadcasting to SSE clients.
        var queue = await subscriber.SubscribeAsync(channel).ConfigureAwait(false);

        // Process messages from the channel queue on the thread pool.
        _ = Task.Run(async () =>
        {
            await foreach (var msg in queue)
            {
                if (msg.Message.IsNullOrEmpty) continue;

                var sseEvent = SseEventSerializer.Deserialize(msg.Message!);
                if (sseEvent is null)
                {
                    _logger.LogWarning("[RedisPubSub] Failed to deserialize incoming message.");
                    continue;
                }

                var group = sseEvent.Group;

                if (string.IsNullOrEmpty(group))
                {
                    // No group set — drop to prevent accidental fan-out to all users.
                    // Use PublishToAllAsync (sets Group = ProbahoSseGroups.Broadcast) for intentional broadcasts.
                    _logger.LogWarning(
                        "[RedisPubSub] Event id={Id} has no group — dropped to prevent data leak. " +
                        "Call PublishToAllAsync for intentional fan-out.", sseEvent.Id);
                    continue;
                }

                _logger.LogDebug("[RedisPubSub] Received event id={Id} group={Group}, forwarding to local connections.",
                    sseEvent.Id, group);

                try
                {
                    if (group == ProbahoSseGroups.Broadcast)
                        await _manager.BroadcastAsync(sseEvent, stoppingToken).ConfigureAwait(false);
                    else
                        await _manager.SendToGroupAsync(group, sseEvent, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RedisPubSub] Error forwarding event.");
                }
            }
        }, stoppingToken);

        // Keep the hosted service alive until the application shuts down.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RedisPubSub] Unsubscribing from channel '{Channel}'.", _backplane.ChannelName);
        try
        {
            var subscriber = _backplane.GetSubscriber();
            await subscriber.UnsubscribeAsync(RedisChannel.Literal(_backplane.ChannelName)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RedisPubSub] Error during unsubscribe on shutdown.");
        }

        await base.StopAsync(cancellationToken);
    }
}
