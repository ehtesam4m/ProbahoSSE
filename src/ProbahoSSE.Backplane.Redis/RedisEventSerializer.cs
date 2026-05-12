using System.Text.Json;
using System.Text.Json.Serialization;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Backplane.Redis;

/// <summary>
/// JSON serialization context for AOT-compatible serialization of SSE events.
/// </summary>
[JsonSerializable(typeof(ProbahoSseEvent))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ProbahoSseJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Shared serialization helpers for Redis backplane implementations.
/// </summary>
public static class RedisEventSerializer
{
    /// <summary>Serializes an SSE event to a JSON string.</summary>
    public static string Serialize(IProbahoSseEvent sseEvent)
    {
        var model = sseEvent is ProbahoSseEvent e ? e : new ProbahoSseEvent
        {
            Id = sseEvent.Id,
            EventType = sseEvent.EventType,
            Data = sseEvent.Data
        };
        return JsonSerializer.Serialize(model, ProbahoSseJsonContext.Default.ProbahoSseEvent);
    }

    /// <summary>Deserializes an SSE event from a JSON string. Returns null on failure.</summary>
    public static IProbahoSseEvent? Deserialize(string json)
        => JsonSerializer.Deserialize(json, ProbahoSseJsonContext.Default.ProbahoSseEvent);
}

