namespace ProbahoSSE.Abstractions;

/// <summary>
/// Manages active SSE connections and provides broadcasting capabilities.
/// </summary>
public interface IProbahoSseManager
{
    /// <summary>Gets the total number of active connections.</summary>
    int GetConnectionCount();

    /// <summary>Gets the number of active connections for a specific group.</summary>
    /// <param name="group">The group name.</param>
    int GetGroupConnectionCount(string group);

    /// <summary>
    /// Broadcasts an event to all connected clients.
    /// </summary>
    /// <param name="sseEvent">The event to broadcast.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task BroadcastAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an event to all connections belonging to a specific group.
    /// </summary>
    /// <param name="group">The target group name.</param>
    /// <param name="sseEvent">The event to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new connection with the manager.
    /// </summary>
    /// <param name="connection">The connection to register.</param>
    /// <returns>True if the connection was accepted; false if limits were exceeded.</returns>
    bool TryRegister(IProbahoSseConnection connection);

    /// <summary>
    /// Unregisters a connection from the manager.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to remove.</param>
    void Unregister(string connectionId);
}
