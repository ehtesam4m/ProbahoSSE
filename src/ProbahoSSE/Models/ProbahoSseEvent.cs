using System.Text.Json.Serialization;

namespace ProbahoSSE.Models;

/// <summary>
/// A concrete, immutable implementation of <see cref="Abstractions.IProbahoSseEvent"/>.
/// </summary>
public sealed record ProbahoSseEvent : Abstractions.IProbahoSseEvent
{
    /// <inheritdoc />
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <inheritdoc />
    [JsonPropertyName("eventType")]
    public string? EventType { get; init; }

    /// <inheritdoc />
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    /// <summary>
    /// Optional group name for targeted delivery. When set the backplane listener routes this
    /// event only to SSE connections registered for this group via
    /// <see cref="Abstractions.IProbahoSseManager.SendToGroupAsync"/>.
    /// </summary>
    [JsonPropertyName("group")]
    public string? Group { get; init; }

    /// <summary>
    /// Creates a new <see cref="ProbahoSseEvent"/> with the specified data.
    /// </summary>
    /// <param name="data">The data payload.</param>
    /// <param name="eventType">Optional event type name.</param>
    /// <param name="id">Optional unique event ID.</param>
    /// <param name="group">Optional group name for targeted delivery.</param>
    public static ProbahoSseEvent Create(string data, string? eventType = null, string? id = null, string? group = null)
        => new() { Data = data, EventType = eventType, Id = id ?? Guid.NewGuid().ToString("N"), Group = group };
}
