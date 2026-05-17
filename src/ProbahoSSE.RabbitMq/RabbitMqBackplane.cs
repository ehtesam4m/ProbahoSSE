using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
using ProbahoSSE.Models;
using RabbitMQ.Client;

namespace ProbahoSSE.RabbitMq;

/// <summary>
/// An <see cref="IProbahoSseBackplane"/> implementation using a single RabbitMQ fanout exchange.
/// Every instance publishes to the same exchange; every instance receives every message and
/// filters locally by group in <see cref="RabbitMqListenerService"/>.
/// </summary>
public sealed class RabbitMqBackplane : IProbahoSseBackplane, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqBackplane> _logger;

    // Publish channel — initialised by RabbitMqListenerService.StartAsync before any request arrives.
    private IChannel? _publishChannel;

    /// <summary>Initializes the RabbitMQ backplane.</summary>
    public RabbitMqBackplane(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqBackplane> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Called by <see cref="RabbitMqListenerService"/> during startup to supply a ready-to-use
    /// publish channel <em>after</em> the exchange has been declared.
    /// </summary>
    internal void SetPublishChannel(IChannel channel) => _publishChannel = channel;

    /// <inheritdoc />
    public Task PublishToGroupAsync(string group, IProbahoSseEvent sseEvent,
        CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = group }
            : sseEvent;
        return PublishAsync(stamped, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishToAllAsync(IProbahoSseEvent sseEvent,
        CancellationToken cancellationToken = default)
    {
        var stamped = sseEvent is ProbahoSseEvent e
            ? e with { Group = ProbahoSseGroups.Broadcast }
            : sseEvent;
        return PublishAsync(stamped, cancellationToken);
    }

    private async Task PublishAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken)
    {
        if (_publishChannel is null)
            throw new InvalidOperationException(
                "RabbitMQ publish channel is not initialised. " +
                "Ensure RabbitMqListenerService has started before publishing.");

        var payload = SseEventSerializer.Serialize(sseEvent);
        var body = Encoding.UTF8.GetBytes(payload);

        _logger.LogDebug("[RabbitMq] Publishing event id={Id} group={Group} to exchange '{Exchange}'",
            sseEvent.Id, sseEvent.Group, _options.ExchangeName);

        await _publishChannel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: string.Empty,   // fanout ignores routing key
            body: body,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_publishChannel is not null)
        {
            try
            {
                await _publishChannel.CloseAsync().ConfigureAwait(false);
                await _publishChannel.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMq] Error closing publish channel.");
            }
        }
    }
}

