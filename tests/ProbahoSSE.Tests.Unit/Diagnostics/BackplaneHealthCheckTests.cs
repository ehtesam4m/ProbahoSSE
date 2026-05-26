using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProbahoSSE.Diagnostics;
using ProbahoSSE.Tests.Unit.Stubs;

namespace ProbahoSSE.Tests.Unit.Diagnostics;

/// <summary>Tests for <see cref="BackplaneHealthCheck"/>.</summary>
public sealed class BackplaneHealthCheckTests
{
    private static ProbahoSseMetrics BuildMetrics()
    {
        var svc = new ServiceCollection();
        svc.AddSingleton<IProbahoSseManager>(_ => new ConnectionManagerBuilder().Build());
        svc.AddMetrics();
        var sp = svc.BuildServiceProvider();
        return new ProbahoSseMetrics(
            sp.GetRequiredService<IMeterFactory>(),
            sp.GetRequiredService<IProbahoSseManager>());
    }

    [Fact]
    public async Task ReturnsHealthy_WhenNoFailures()
    {
        var metrics = BuildMetrics();
        var check   = new BackplaneHealthCheck(metrics);

        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReturnsUnhealthy_WhenLastPublishFailed()
    {
        var metrics = BuildMetrics();
        metrics.RecordMessageFailed("redis-pubsub");

        var check  = new BackplaneHealthCheck(metrics);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("exception", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReturnsHealthy_AfterSuccessfulPublish_ClearsUnhealthy()
    {
        var metrics = BuildMetrics();
        metrics.RecordMessageFailed("redis-pubsub");  // unhealthy
        metrics.RecordMessageSent("redis-pubsub");    // clears failure

        var check  = new BackplaneHealthCheck(metrics);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
