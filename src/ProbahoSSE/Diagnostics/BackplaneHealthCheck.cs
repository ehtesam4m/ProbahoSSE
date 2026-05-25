using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProbahoSSE.Extensions;

namespace ProbahoSSE.Diagnostics;

/// <summary>
/// An <see cref="IHealthCheck"/> that reports the health of the ProbahoSSE backplane
/// based on recent publish results.
/// </summary>
/// <remarks>
/// Health logic:
/// <list type="table">
///   <item>
///     <term><see cref="HealthStatus.Unhealthy"/></term>
///     <description>The most recent publish attempt threw an exception.</description>
///   </item>
///   <item>
///     <term><see cref="HealthStatus.Healthy"/></term>
///     <description>Last publish succeeded (or no publish has been attempted yet).</description>
///   </item>
/// </list>
/// State is tracked by <see cref="ProbahoSseMetrics"/> — the health check does
/// <em>not</em> make any Redis calls itself.
/// </remarks>
public sealed class BackplaneHealthCheck : IHealthCheck
{
    private readonly ProbahoSseMetrics _metrics;

    /// <summary>
    /// Initializes the health check.
    /// </summary>
    /// <param name="metrics">Shared metrics instance that tracks publish outcomes.</param>
    public BackplaneHealthCheck(ProbahoSseMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_metrics.LastPublishFailed)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The last backplane publish attempt threw an exception."));

        return Task.FromResult(HealthCheckResult.Healthy("Backplane is operating normally."));
    }
}

/// <summary>Extension methods for registering <see cref="BackplaneHealthCheck"/>.</summary>
public static class BackplaneHealthCheckExtensions
{
    /// <summary>
    /// Registers the ProbahoSSE <see cref="BackplaneHealthCheck"/> with ASP.NET Core health checks.
    /// This is opt-in — do not call it if you manage health checks elsewhere.
    /// </summary>
    /// <param name="builder">The ProbahoSSE builder.</param>
    /// <param name="name">Health-check name shown on the <c>/health</c> endpoint. Defaults to <c>"probahosse-backplane"</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static ProbahoSseBuilder AddHealthCheck(
        this ProbahoSseBuilder builder,
        string name = "probahosse-backplane")
    {
        builder.Services.AddHealthChecks()
            .AddCheck<BackplaneHealthCheck>(name);

        return builder;
    }
}
