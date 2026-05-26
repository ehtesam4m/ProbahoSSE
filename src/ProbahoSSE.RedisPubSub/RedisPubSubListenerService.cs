using System.Diagnostics;
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
/// </summary>
/// <remarks>
/// <para>
/// Uses the <c>ChannelMessageQueue</c> pattern (SubscribeAsync → OnMessage) rather than
/// the callback pattern, mirroring the approach used by ASP.NET Core SignalR's
/// <c>RedisHubLifetimeManager</c>. StackExchange.Redis automatically re-attaches the queue
/// to the channel after a connection drop, so no <c>ConnectionRestored</c> handler or
/// manual re-subscribe logic is needed.
/// </para>
/// <para>
/// On graceful shutdown the queue is unsubscribed via <c>queue.UnsubscribeAsync()</c>
/// so that downstream pub/sub handlers are cleaned up before the process exits.
/// </para>
/// </remarks>
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
        _manager   = manager;
        _logger    = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var redisChannel = RedisChannel.Literal(_backplane.ChannelName);
        var subscriber   = _backplane.GetSubscriber();

        _logger.LogInformation(
            "[RedisPubSub] Subscribing to channel '{Channel}'", _backplane.ChannelName);

        // ChannelMessageQueue keeps the subscription alive across Redis reconnects
        // automatically — no ConnectionRestored handler needed.
        var queue = await subscriber.SubscribeAsync(redisChannel).ConfigureAwait(false);

        queue.OnMessage(async channelMessage =>
        {
            await ProcessMessageAsync(channelMessage.Message).ConfigureAwait(false);
        });

        _logger.LogInformation(
            "[RedisPubSub] Subscribed to channel '{Channel}'", _backplane.ChannelName);

        // Hold until application shutdown.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        // Clean shutdown — unsubscribe via the queue object, not the channel name.
        _logger.LogInformation(
            "[RedisPubSub] Unsubscribing from channel '{Channel}'", _backplane.ChannelName);
        await queue.UnsubscribeAsync().ConfigureAwait(false);
    }

    // ── Message handling ──────────────────────────────────────────────────────

    private async Task ProcessMessageAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty) return;

        var sseEvent = SseEventSerializer.Deserialize(message!);
        if (sseEvent is null)
        {
            _logger.LogWarning("[RedisPubSub] Failed to deserialize incoming message.");
            return;
        }

        var group = sseEvent.Group;

        if (string.IsNullOrEmpty(group))
        {
            _logger.LogWarning(
                "[RedisPubSub] Event id={Id} has no group — dropped to prevent data leak. " +
                "Call PublishToAllAsync for intentional fan-out.", sseEvent.Id);
            return;
        }

        using var activity = sseEvent.TraceParent is not null
            ? ProbahoSseTelemetry.ActivitySource.StartActivity(ProbahoSseTelemetry.Activities.BackplaneReceive, ActivityKind.Consumer, sseEvent.TraceParent)
            : ProbahoSseTelemetry.ActivitySource.StartActivity(ProbahoSseTelemetry.Activities.BackplaneReceive, ActivityKind.Consumer);
        activity?.SetTag(ProbahoSseTelemetry.Tags.Backplane, "redis-pubsub");
        activity?.SetTag(ProbahoSseTelemetry.Tags.EventId, sseEvent.Id);
        activity?.SetTag(ProbahoSseTelemetry.Tags.Group, group);

        _logger.LogDebug("[RedisPubSub] Received event id={Id} group={Group}, forwarding to local connections.",
            sseEvent.Id, group);

        try
        {
            if (group == ProbahoSseGroups.Broadcast)
                await _manager.BroadcastAsync(sseEvent).ConfigureAwait(false);
            else
                await _manager.SendToGroupAsync(group, sseEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "[RedisPubSub] Error forwarding event.");
        }
    }
}
