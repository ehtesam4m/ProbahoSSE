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
}


