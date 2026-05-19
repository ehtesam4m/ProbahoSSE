using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane;
using ProbahoSSE.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ProbahoSSE.RabbitMq;

/// <summary>
/// A hosted service that owns the RabbitMQ <see cref="IConnection"/> lifecycle, declares the
/// fanout exchange, creates an exclusive per-instance queue, and forwards every received message
/// to locally connected SSE clients via <see cref="IProbahoSseManager"/>.
/// </summary>
public sealed class RabbitMqListenerService : IHostedService, IAsyncDisposable
{
    private readonly RabbitMqBackplane _backplane;
    private readonly IProbahoSseManager _manager;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqListenerService> _logger;

    private IConnection? _connection;
    private IChannel? _consumeChannel;
    private IChannel? _publishChannel;

    /// <summary>Initializes the listener service.</summary>
    public RabbitMqListenerService(
        RabbitMqBackplane backplane,
        IProbahoSseManager manager,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqListenerService> logger)
    {
        _backplane = backplane;
        _manager = manager;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[RabbitMq] Connecting to {Host}:{Port}, exchange '{Exchange}'",
            _options.HostName, _options.Port, _options.ExchangeName);

        _connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetupPublishChannelAsync(_connection, cancellationToken).ConfigureAwait(false);
        await SetupConsumeChannelAsync(_connection, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RabbitMq] Shutting down listener.");

        if (_consumeChannel is not null)
        {
            try
            {
                await _consumeChannel.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMq] Error closing consume channel.");
            }
        }

        if (_publishChannel is not null)
        {
            try
            {
                await _publishChannel.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMq] Error closing publish channel.");
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RabbitMq] Error closing connection.");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_consumeChannel is not null) await _consumeChannel.DisposeAsync().ConfigureAwait(false);
        if (_publishChannel is not null) await _publishChannel.DisposeAsync().ConfigureAwait(false);
        if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }

    private async Task SetupPublishChannelAsync(IConnection connection, CancellationToken cancellationToken)
    {
        _publishChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _publishChannel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Fanout,
            durable: false,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _backplane.SetPublishChannel(_publishChannel);
    }

    private async Task SetupConsumeChannelAsync(IConnection connection, CancellationToken cancellationToken)
    {
        _consumeChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var queueName = await DeclareAndBindQueueAsync(_consumeChannel, cancellationToken).ConfigureAwait(false);

        await StartConsumingAsync(_consumeChannel, queueName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> DeclareAndBindQueueAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // Exclusive, auto-delete queue — automatically removed when this instance disconnects.
        // Server generates a unique name so multiple instances don't share state.
        var queueDeclare = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var queueName = queueDeclare.QueueName;

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: _options.ExchangeName,
            routingKey: string.Empty, // fanout ignores routing key
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[RabbitMq] Listening on queue '{Queue}' bound to exchange '{Exchange}'",
            queueName, _options.ExchangeName);

        return queueName;
    }

    private async Task StartConsumingAsync(IChannel channel, string queueName, CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: true, // fire-and-forget — no replay, no persistence
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(ea.Body.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RabbitMq] Failed to decode message body — skipped.");
            return;
        }

        var sseEvent = SseEventSerializer.Deserialize(payload);
        if (sseEvent is null)
        {
            _logger.LogWarning("[RabbitMq] Failed to deserialize incoming message — skipped.");
            return;
        }

        var group = sseEvent.Group;

        if (string.IsNullOrEmpty(group))
        {
            // Guard against accidental fan-out. Use PublishToAllAsync for intentional broadcasts.
            _logger.LogWarning(
                "[RabbitMq] Event id={Id} has no group — dropped to prevent data leak. " +
                "Call PublishToAllAsync for intentional fan-out.", sseEvent.Id);
            return;
        }

        _logger.LogDebug(
            "[RabbitMq] Received event id={Id} group={Group}, forwarding to local connections.",
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
            _logger.LogError(ex, "[RabbitMq] Error forwarding event id={Id}.", sseEvent.Id);
        }
    }
}
