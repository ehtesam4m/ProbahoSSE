namespace ProbahoSSE.Abstractions;

/// <summary>
/// Represents an active SSE client connection capable of receiving events.
/// </summary>
public interface IProbahoSseConnection
{
    /// <summary>Gets the unique identifier for this connection.</summary>
    string ConnectionId { get; }

    /// <summary>Gets the optional group associated with this connection for targeted delivery.</summary>
    string? Group { get; }

    /// <summary>Gets whether the connection is currently active and open.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sends an SSE event to this specific client connection.
    /// </summary>
    /// <param name="sseEvent">The event to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask SendAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default);
}
