using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger("Simulator");

var ingestUrl    = config["Simulator:IngestUrl"] ?? "http://localhost:8080/ingest";
var retryDelayMs = int.TryParse(config["Simulator:RetryDelayMs"], out var d) ? d : 3000;
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

// ── Sensor definitions — every group gets all four sensor types ───────────────
string[] groups = ["alice", "bob", "charlie", "diana"];

// Sensor templates: (sensor, unit, min, max, alertThreshold, alertAbove, intervalMs)
(string Sensor, string Unit, double Min, double Max, double AlertThreshold, bool AlertAbove, int IntervalMs)[] templates =
[
    ("temperature", "°C",  18.0,   40.0,   35.0,   true,  3000),
    ("humidity",    "%",   30.0,   95.0,   80.0,   true,  4000),
    ("pressure",    "hPa", 960.0, 1040.0,  980.0,  false, 5000),
    ("aqi",         "AQI", 10.0,  200.0,  150.0,   true,  7000),
];

var sensors = groups
    .SelectMany(group => templates.Select(t =>
        new SensorDef(group, t.Sensor, t.Unit, t.Min, t.Max, t.AlertThreshold, t.AlertAbove, t.IntervalMs)))
    .ToArray();

Console.WriteLine($"IoT Sensor Simulator starting.");
Console.WriteLine($"Ingest URL : {ingestUrl}");
Console.WriteLine($"Groups     : {string.Join(", ", groups)}");
Console.WriteLine("Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// ── Wait for the API to be ready ─────────────────────────────────────────────
logger.LogInformation("Waiting for API at {Url}…", ingestUrl);
var healthUrl = new Uri(new Uri(ingestUrl), "/health").ToString();
while (!cts.Token.IsCancellationRequested)
{
    try
    {
        var probe = await http.GetAsync(healthUrl, cts.Token);
        if (probe.IsSuccessStatusCode) break;
    }
    catch { /* not ready yet */ }
    await Task.Delay(retryDelayMs, cts.Token).ContinueWith(_ => { });
}
logger.LogInformation("API is ready — starting sensor loops.");

// ── Run one Task per sensor in parallel ──────────────────────────────────────
var rng = new Random();
var tasks = sensors.Select(sensor => Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var value = Math.Round(sensor.Min + rng.NextDouble() * (sensor.Max - sensor.Min), 1);
            var alert = sensor.AlertAbove ? value > sensor.AlertThreshold
                                           : value < sensor.AlertThreshold;

            var reading = new SensorReading(
                Group:     sensor.Group,
                Sensor:    sensor.Sensor,
                Value:     value,
                Unit:      sensor.Unit,
                Alert:     alert,
                Timestamp: DateTimeOffset.UtcNow.ToString("O"));

            var response = await http.PostAsJsonAsync(ingestUrl, reading, cts.Token);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("[{Group}] {Sensor}={Value}{Unit}{Alert}",
                    sensor.Group, sensor.Sensor, value, sensor.Unit,
                    alert ? " ⚠ ALERT" : string.Empty);
            else
                logger.LogWarning("[{Group}] Ingest returned {Status}", sensor.Group, response.StatusCode);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Group}] Failed to post reading — retrying in {Delay}ms",
                sensor.Group, retryDelayMs);
            await Task.Delay(retryDelayMs, cts.Token).ContinueWith(_ => { });
            continue;
        }

        await Task.Delay(sensor.IntervalMs, cts.Token).ContinueWith(_ => { });
    }
}, cts.Token));

await Task.WhenAll(tasks);
logger.LogInformation("Simulator stopped.");

// ── Types ─────────────────────────────────────────────────────────────────────
record SensorDef(
    string Group,
    string Sensor,
    string Unit,
    double Min,
    double Max,
    double AlertThreshold,
    bool   AlertAbove,
    int    IntervalMs);

record SensorReading(
    [property: JsonPropertyName("group")]     string Group,
    [property: JsonPropertyName("sensor")]    string Sensor,
    [property: JsonPropertyName("value")]     double Value,
    [property: JsonPropertyName("unit")]      string Unit,
    [property: JsonPropertyName("alert")]     bool   Alert,
    [property: JsonPropertyName("timestamp")] string Timestamp);

