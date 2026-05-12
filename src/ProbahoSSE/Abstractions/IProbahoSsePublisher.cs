namespace ProbahoSSE.Abstractions;

/// <summary>
/// Defines the ability to publish SSE events to a backplane so all server instances receive them.
/// Inject this interface in application code (e.g. a Kafka consumer service) to push events
/// into the ProbahoSSE distribution layer.
/// </summary>
public interface IProbahoSsePublisher
{
    /// <summary>
    /// Publishes an event to the backplane targeted at a specific group.
    /// Only SSE connections registered for <paramref name="group"/> on any server instance
    /// will receive the event.
    /// </summary>
    /// <param name="group">The target group name.</param>
    /// <param name="sseEvent">The event to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task PublishToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes an event to the backplane for fan-out to ALL connected clients across every instance.
    /// Use sparingly and intentionally — prefer <see cref="PublishToGroupAsync"/> to avoid data leaks.
    /// </summary>
    /// <param name="sseEvent">The event to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task PublishToAllAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default);
}
