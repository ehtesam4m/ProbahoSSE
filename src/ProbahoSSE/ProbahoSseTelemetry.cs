using System.Diagnostics;

namespace ProbahoSSE;

/// <summary>
/// OpenTelemetry / <see cref="ActivitySource"/> instrumentation entry point for ProbahoSSE.
/// </summary>
/// <remarks>
/// Register the source with your tracing pipeline to receive spans:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t
///         .AddSource(ProbahoSseTelemetry.SourceName)
///         .AddAspNetCoreInstrumentation()
///         .AddOtlpExporter());
/// </code>
/// When no listener is registered, <see cref="ActivitySource.StartActivity"/> returns
/// <c>null</c> and has near-zero overhead — no exceptions, no allocations.
/// </remarks>
public static class ProbahoSseTelemetry
{
    /// <summary>
    /// The <see cref="ActivitySource"/> name to pass to <c>AddSource()</c>.
    /// </summary>
    public const string SourceName = "ProbahoSSE";

    /// <summary>Shared source used internally by all ProbahoSSE components.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>Canonical activity (span) names used across all ProbahoSSE components.</summary>
    public static class Activities
    {
        public const string Connection       = "sse.connection";
        public const string Broadcast        = "sse.broadcast";
        public const string SendToGroup      = "sse.send_to_group";
        public const string BackplaneReceive = "sse.backplane.receive";
    }

    /// <summary>Canonical OTel tag keys used across all ProbahoSSE components.</summary>
    public static class Tags
    {
        public const string EventId              = "sse.event_id";
        public const string Group                = "sse.group";
        public const string ConnectionId         = "sse.connection_id";
        public const string ConnectionCount      = "sse.connection_count";
        public const string GroupConnectionCount = "sse.group_connection_count";
        public const string Backplane            = "sse.backplane";
        public const string StreamEntryId        = "sse.stream_entry_id";
    }
}


