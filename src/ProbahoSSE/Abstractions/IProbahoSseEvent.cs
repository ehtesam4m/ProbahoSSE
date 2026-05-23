namespace ProbahoSSE.Abstractions;

/// <summary>
/// Represents a Server-Sent Event with an optional ID, event type, data payload, and optional group for targeted delivery.
/// </summary>
public interface IProbahoSseEvent
{
    /// <summary>Gets the unique identifier for this event, used for Last-Event-ID tracking.</summary>
    string? Id { get; }

    /// <summary>Gets the event type name. If null, the default event type is used.</summary>
    string? EventType { get; }

    /// <summary>Gets the data payload of the event.</summary>
    string Data { get; }

    /// <summary>
    /// Optional group name for targeted delivery. When set the backplane listener routes this
    /// event only to SSE connections registered for this group via
    /// <see cref="IProbahoSseManager.SendToGroupAsync"/>.
    /// </summary>
    string? Group { get; }

    /// <summary>
    /// W3C <c>traceparent</c> captured at publish time.
    /// Backplane consumers use this to restore the parent span and link delivery traces
    /// to the originating HTTP request across the message-broker boundary.
    /// </summary>
    string? TraceParent { get; }
}
