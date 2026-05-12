namespace ProbahoSSE.Abstractions;

/// <summary>
/// Optional capability for backplanes that support replaying historical events.
/// Implemented by backplanes using persistent storage (e.g., Redis Streams).
/// The <see cref="SseEndpointHandler"/> checks for this interface on each connection
/// and flushes missed events before the live stream starts.
/// </summary>
public interface IProbahoSseReplayable
{
    /// <summary>
    /// Replays all events persisted in the backplane since <paramref name="lastEventId"/>,
    /// delivering them in order to <paramref name="handler"/> before live streaming resumes.
    /// </summary>
    /// <param name="lastEventId">
    /// The event ID the client last received, taken from the <c>Last-Event-ID</c> HTTP header.
    /// </param>
    /// <param name="handler">Callback invoked for each replayed event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ReplayFromAsync(
        string lastEventId,
        Func<IProbahoSseEvent, Task> handler,
        CancellationToken cancellationToken = default);
}

