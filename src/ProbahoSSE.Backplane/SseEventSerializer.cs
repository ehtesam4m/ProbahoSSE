using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Backplane;

/// <summary>
/// JSON serialization context for AOT-compatible serialization of SSE events.
/// Used by all backplane implementations (Redis, RabbitMQ, etc.).
/// </summary>
[JsonSerializable(typeof(ProbahoSseEvent))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ProbahoSseJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Shared serialization helpers for ProbahoSSE backplane implementations.
/// Provides AOT-compatible JSON serialize/deserialize for IProbahoSseEvent.
/// </summary>
public static class SseEventSerializer
{
    /// <summary>
    /// Serializes an SSE event to a JSON string.
    /// Embeds the current W3C <c>traceparent</c> (<see cref="Activity.Current"/>.<see cref="Activity.Id"/>)
    /// so backplane consumers can restore the parent span and link delivery to the originating request.
    /// </summary>
    public static string Serialize(IProbahoSseEvent sseEvent)
    {
        var model = sseEvent is ProbahoSseEvent e ? e : new ProbahoSseEvent
        {
            Id = sseEvent.Id,
            EventType = sseEvent.EventType,
            Data = sseEvent.Data,
            Group = sseEvent is ProbahoSseEvent pe ? pe.Group : null
        };

        // Capture the current trace context so consumers can restore the parent span.
        var traceParent = Activity.Current?.Id;
        if (traceParent is not null && model.TraceParent is null)
            model = model with { TraceParent = traceParent };

        return JsonSerializer.Serialize(model, ProbahoSseJsonContext.Default.ProbahoSseEvent);
    }

    /// <summary>Deserializes an SSE event from a JSON string. Returns null on failure.</summary>
    public static IProbahoSseEvent? Deserialize(string json)
        => JsonSerializer.Deserialize(json, ProbahoSseJsonContext.Default.ProbahoSseEvent);
}
