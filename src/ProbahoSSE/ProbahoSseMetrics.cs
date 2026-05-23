using System.Diagnostics;
using System.Diagnostics.Metrics;
using ProbahoSSE.Abstractions;

namespace ProbahoSSE;

/// <summary>
/// Provides OpenTelemetry-compatible metrics for ProbahoSSE via <see cref="System.Diagnostics.Metrics"/>.
/// </summary>
/// <remarks>
/// Register the meter with your metrics pipeline to collect data:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m.AddMeter(ProbahoSseMetrics.MeterName));
/// </code>
/// Or inspect live without any code change:
/// <code>
/// dotnet counters monitor --counters ProbahoSSE --process-id &lt;pid&gt;
/// </code>
/// When no listener is registered, all instrument operations are no-ops with near-zero overhead.
/// </remarks>
public sealed class ProbahoSseMetrics : IDisposable
{
    /// <summary>The meter name to pass to <c>AddMeter()</c>.</summary>
    public const string MeterName = "ProbahoSSE";

    private readonly Meter _meter;
    private readonly Counter<long> _connectionsRejected;
    private readonly Counter<long> _eventsPublished;
    private readonly Histogram<double> _publishDuration;

    /// <summary>
    /// Initializes the metrics instruments.
    /// </summary>
    /// <param name="meterFactory">
    /// <see cref="IMeterFactory"/> from DI — respects DI lifetime, supports testing, and
    /// ensures proper disposal. Prefer this over <c>new Meter(...)</c>.
    /// </param>
    /// <param name="manager">
    /// Used by the <c>sse.connections.active</c> observable gauge to read the live count directly,
    /// avoiding a separate counter that could drift out of sync.
    /// </param>
    public ProbahoSseMetrics(IMeterFactory meterFactory, IProbahoSseManager manager)
    {
        _meter = meterFactory.Create(MeterName);

        // ObservableGauge pulls the current value on demand — zero synchronization overhead.
        _meter.CreateObservableGauge(
            name: "sse.connections.active",
            observeValue: () => manager.GetConnectionCount(),
            description: "Number of currently active SSE connections.");

        _connectionsRejected = _meter.CreateCounter<long>(
            name: "sse.connections.rejected",
            description: "Total SSE connections rejected due to connection limit enforcement.");

        _eventsPublished = _meter.CreateCounter<long>(
            name: "sse.events.published",
            description: "Total events successfully published to the backplane.");

        _publishDuration = _meter.CreateHistogram<double>(
            name: "sse.backplane.publish.duration",
            unit: "ms",
            description: "Time taken to publish an event to the backplane (milliseconds). " +
                         "Tagged with 'sse.backplane' (rabbitmq | redis-pubsub | redis-stream).");
    }

    /// <summary>
    /// Records a rejected SSE connection, tagged with the group name.
    /// Called automatically by the built-in endpoint handler when a limit is enforced.
    /// Custom endpoint handlers should also call this for consistent metrics.
    /// </summary>
    public void RecordConnectionRejected(string? group) =>
        _connectionsRejected.Add(1, new TagList { { "sse.group", group ?? "(none)" } });

    /// <summary>
    /// Records a successful backplane publish with its latency.
    /// Called automatically by each built-in backplane. Custom backplane implementations
    /// should also call this so the shared meter captures all activity.
    /// </summary>
    /// <param name="backplane">Backplane identifier, e.g. <c>"rabbitmq"</c>, <c>"redis-pubsub"</c>, <c>"redis-stream"</c>.</param>
    /// <param name="durationMs">Elapsed time in milliseconds for the publish operation.</param>
    public void RecordPublish(string backplane, double durationMs)
    {
        var tags = new TagList { { "sse.backplane", backplane } };
        _eventsPublished.Add(1, tags);
        _publishDuration.Record(durationMs, tags);
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}



