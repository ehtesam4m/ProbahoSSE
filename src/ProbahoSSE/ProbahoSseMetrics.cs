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

    // ── Existing instruments (kept for backward compatibility) ────────────────
    private readonly Counter<long> _connectionsRejected;
    private readonly Counter<long> _eventsPublished;
    private readonly Histogram<double> _publishDuration;

    // ── New instruments per plan ──────────────────────────────────────────────
    private readonly Counter<long> _messagesSent;
    private readonly Counter<long> _messagesFailed;

    // ── Health-check state ─────────────────────────────────────────────────────
    // Updated atomically; read by BackplaneHealthCheck without locks.
    private volatile bool _lastPublishFailed;

    /// <summary>
    /// Initializes the metrics instruments.
    /// </summary>
    /// <param name="meterFactory"><see cref="IMeterFactory"/> from DI.</param>
    /// <param name="manager">Provides the live connection count for the observable gauge.</param>
    public ProbahoSseMetrics(IMeterFactory meterFactory, IProbahoSseManager manager)
    {
        _meter = meterFactory.Create(MeterName);

        // probahosse.connections.active — pulled on demand, no drift risk.
        _meter.CreateObservableGauge(
            name: "probahosse.connections.active",
            observeValue: () => (long)manager.GetConnectionCount(),
            description: "Number of currently active SSE connections.");

        // Legacy name kept for backward compatibility.
        _meter.CreateObservableGauge(
            name: "sse.connections.active",
            observeValue: () => manager.GetConnectionCount(),
            description: "Number of currently active SSE connections (legacy name).");

        _connectionsRejected = _meter.CreateCounter<long>(
            name: "sse.connections.rejected",
            description: "Total SSE connections rejected due to connection limit enforcement.");

        _eventsPublished = _meter.CreateCounter<long>(
            name: "sse.events.published",
            description: "Total events successfully published to the backplane (legacy name).");

        _publishDuration = _meter.CreateHistogram<double>(
            name: "sse.backplane.publish.duration",
            unit: "ms",
            description: "Publish latency in ms. Tagged with 'sse.backplane'.");


        _messagesSent = _meter.CreateCounter<long>(
            name: "probahosse.backplane.messages_sent",
            description: "Total messages successfully published to the backplane.");

        _messagesFailed = _meter.CreateCounter<long>(
            name: "probahosse.backplane.messages_failed",
            description: "Total publish attempts that threw an exception.");
    }

    // ── Health-check state accessors (internal — not part of the public API) ──

    /// <summary>
    /// <see langword="true"/> when the most recent publish attempt threw an exception.
    /// Cleared automatically on the next successful publish.
    /// </summary>
    internal bool LastPublishFailed => _lastPublishFailed;


    // ── Public recording methods ───────────────────────────────────────────────

    /// <summary>Records a rejected SSE connection.</summary>
    public void RecordConnectionRejected(string? group) =>
        _connectionsRejected.Add(1, new TagList { { "sse.group", group ?? "(none)" } });

    /// <summary>
    /// Records a successful backplane publish with its latency (legacy method).
    /// Used by RabbitMQ and RedisStream backplanes.
    /// </summary>
    public void RecordPublish(string backplane, double durationMs)
    {
        var tags = new TagList { { "sse.backplane", backplane } };
        _eventsPublished.Add(1, tags);
        _publishDuration.Record(durationMs, tags);

        // Also update the new per-plan instruments.
        _messagesSent.Add(1, tags);
        _lastPublishFailed = false;
    }

    /// <summary>
    /// Records a successfully published message on the new <c>probahosse.*</c> instruments.
    /// Preferred over <see cref="RecordPublish"/> for new backplane integrations.
    /// </summary>
    public void RecordMessageSent(string backplane)
    {
        var tags = new TagList { { "sse.backplane", backplane } };
        _messagesSent.Add(1, tags);
        _eventsPublished.Add(1, tags); // keep legacy in sync
        _lastPublishFailed = false;
    }

    /// <summary>Records a failed publish attempt.</summary>
    public void RecordMessageFailed(string backplane)
    {
        _messagesFailed.Add(1, new TagList { { "sse.backplane", backplane } });
        _lastPublishFailed = true;
    }


    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
