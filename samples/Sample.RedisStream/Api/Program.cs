using Microsoft.AspNetCore.Builder;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Extensions;
using ProbahoSSE.Models;
using ProbahoSSE.RedisStream;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ── ProbahoSSE with Redis Stream backplane ───────────────────────────────────
builder.Services.AddProbahoSse(options =>
    {
        options.MaxConnectionsPerGroup = 10;
        options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    })
    .AddRedisStreamBackplane(redis =>
    {
        redis.ConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
        redis.ChannelPrefix = "probaho-stream";
        redis.StreamMaxLength = 10_000;
    });

var app = builder.Build();

app.UseProbahoSse();

// ── SSE endpoint ──────────────────────────────────────────────────────────────
app.MapProbahoSse("/sse", ctx => ctx.Request.Query["group"].FirstOrDefault());

// ── Webhook ingest endpoint ───────────────────────────────────────────────────
// Receives sensor readings from the IoT Simulator (or any HTTP producer) and
// publishes them to the Redis Stream backplane for fan-out to SSE clients.
app.MapPost("/ingest", async (SensorReading reading, IProbahoSsePublisher publisher) =>
{
    if (string.IsNullOrWhiteSpace(reading.Group))
        return Results.BadRequest("'group' field is required.");

    var sseEvent = ProbahoSseEvent.Create(
        data: JsonSerializer.Serialize(reading),
        eventType: reading.Alert ? "alert" : "reading",
        group: reading.Group);

    await publisher.PublishToGroupAsync(reading.Group, sseEvent);
    return Results.Ok(new { published = true, group = reading.Group, sensor = reading.Sensor });
});

// ── Explicit replay demo endpoint ─────────────────────────────────────────────
app.MapGet("/sse/replay", async (HttpContext ctx, string? from, RedisStreamBackplane backplane) =>
{
    if (string.IsNullOrWhiteSpace(from))
        return Results.BadRequest("Query param 'from' (Redis Stream entry ID) is required.");

    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.ContentType = "text/event-stream";
    var options = new ProbahoSseOptions();
    var replayed = 0;

    await backplane.ReplayFromAsync(
        lastEventId: from,
        handler: async sseEvent =>
        {
            if (sseEvent.Id is not null)
                await ctx.Response.WriteAsync($"id: {sseEvent.Id}\n", ctx.RequestAborted);
            await ctx.Response.WriteAsync($"event: {sseEvent.EventType ?? options.DefaultEventType}\n", ctx.RequestAborted);
            await ctx.Response.WriteAsync($"data: {sseEvent.Data}\n\n", ctx.RequestAborted);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            replayed++;
        },
        cancellationToken: ctx.RequestAborted);

    await ctx.Response.WriteAsync($": replayed {replayed} events\n\n", ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
    return Results.Empty;
});

// ── Health & info ─────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    instance = Environment.MachineName,
    backplane = "RedisStream",
    supportsReplay = true
}));

app.MapGet("/info", () => Results.Ok(new
{
    backplane = "RedisStream",
    displayName = "Redis Stream",
    supportsReplay = true,
    description = "Persistent — reconnect replays all missed sensor readings via Last-Event-ID.",
    instance = Environment.MachineName
}));

app.Run();

// ── Sensor reading model (must match Common.IoTSensorSimulator payload) ────────
record SensorReading(
    [property: JsonPropertyName("group")]     string Group,
    [property: JsonPropertyName("sensor")]    string Sensor,
    [property: JsonPropertyName("value")]     double Value,
    [property: JsonPropertyName("unit")]      string Unit,
    [property: JsonPropertyName("alert")]     bool   Alert,
    [property: JsonPropertyName("timestamp")] string Timestamp);
