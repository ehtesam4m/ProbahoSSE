using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Extensions;
using ProbahoSSE.Models;
using ProbahoSSE.RabbitMq;

var builder = WebApplication.CreateBuilder(args);

// ── ProbahoSSE with RabbitMQ fanout backplane ────────────────────────────────
builder.Services.AddProbahoSse(options =>
    {
        options.MaxConnectionsPerUser = 10;
        options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    })
    .AddRabbitMqBackplane(rabbit =>
    {
        rabbit.HostName     = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
        rabbit.UserName     = builder.Configuration["RabbitMq:UserName"] ?? "guest";
        rabbit.Password     = builder.Configuration["RabbitMq:Password"] ?? "guest";
        rabbit.ExchangeName = "probaho-sample";
    });

var app = builder.Build();

app.UseProbahoSse();

// SSE endpoint — group from query string ?group=xxx
app.MapProbahoSse("/sse", ctx => ctx.Request.Query["group"].FirstOrDefault());

// ── Ingest endpoint — receives IoT sensor readings from the simulator ─────────
app.MapPost("/ingest", async (SensorReading reading, IProbahoSsePublisher publisher) =>
{
    var sseEvent = ProbahoSseEvent.Create(
        data:      JsonSerializer.Serialize(reading),
        eventType: reading.Alert ? "alert" : "reading",
        id:        null,
        group:     reading.Group);

    await publisher.PublishToGroupAsync(reading.Group, sseEvent);
    return Results.Ok(new { published = true, group = reading.Group, sensor = reading.Sensor });
});

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", instance = Environment.MachineName, backplane = "RabbitMQ" }));

// Info endpoint — used by the HTML demo to auto-detect which backplane is running
app.MapGet("/info", () => Results.Ok(new
{
    backplane      = "RabbitMQ",
    displayName    = "RabbitMQ Fanout",
    supportsReplay = false,
    description    = "Fire & forget — events missed while disconnected are permanently lost.",
    instance       = Environment.MachineName
}));

app.Run();

// ── Types ─────────────────────────────────────────────────────────────────────
record SensorReading(
    [property: JsonPropertyName("group")]     string Group,
    [property: JsonPropertyName("sensor")]    string Sensor,
    [property: JsonPropertyName("value")]     double Value,
    [property: JsonPropertyName("unit")]      string Unit,
    [property: JsonPropertyName("alert")]     bool   Alert,
    [property: JsonPropertyName("timestamp")] string Timestamp);

