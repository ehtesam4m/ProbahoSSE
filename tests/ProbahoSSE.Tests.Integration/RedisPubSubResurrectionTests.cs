using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;
using ProbahoSSE.RedisPubSub;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisPubSubListenerService"/> shutdown behaviour.
/// Verifies that after <see cref="RedisPubSubListenerService.StopAsync"/> the underlying
/// <c>ChannelMessageQueue</c> is unsubscribed and no further messages are delivered.
/// </summary>
/// <remarks>
/// Container-restart tests (simulating Redis reconnect) are intentionally omitted: the
/// <c>ChannelMessageQueue</c> pattern delegates reconnect handling entirely to
/// StackExchange.Redis, which is tested by its own test suite. Restarting a Testcontainers
/// Redis container assigns a new mapped port, making the existing multiplexer unable to
/// reconnect and causing flaky timeouts.
/// </remarks>
public sealed class RedisPubSubResurrectionTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.2-alpine")
        .Build();

    private IConnectionMultiplexer _mux = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        _mux.Dispose();
        await _redis.StopAsync();
    }

    private (RedisPubSubBackplane backplane, RedisPubSubListenerService listener, RecordingManager manager)
        BuildStack(string prefix)
    {
        var opts    = Options.Create(new RedisPubSubOptions { ChannelPrefix = prefix });
        var manager = new RecordingManager();
        var metrics = new ProbahoSseMetrics(new TestMeterFactory(), manager);
        var bp      = new RedisPubSubBackplane(_mux, opts, NullLogger<RedisPubSubBackplane>.Instance, metrics);
        var svc     = new RedisPubSubListenerService(bp, manager, NullLogger<RedisPubSubListenerService>.Instance);
        return (bp, svc, manager);
    }

    // ── After StopAsync no more messages are delivered (queue.UnsubscribeAsync called) ──

    [DockerAvailableFact]
    public async Task AfterStopAsync_NoMoreMessagesDelivered()
    {
        var (bp, listener, manager) = BuildStack("resurrection-shutdown");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await listener.StartAsync(cts.Token);
        await Task.Delay(300, cts.Token);

        // Confirm messages arrive while running.
        await bp.PublishToAllAsync(ProbahoSseEvent.Create("before-stop"));
        await WaitForCountAsync(manager, 1, TimeSpan.FromSeconds(5));
        Assert.Single(manager.Received);

        // Stop — ExecuteAsync catches OperationCanceledException then calls queue.UnsubscribeAsync().
        await listener.StopAsync(CancellationToken.None);
        await Task.Delay(200);

        // Publish after stop — must NOT be delivered.
        await bp.PublishToAllAsync(ProbahoSseEvent.Create("after-stop"));
        await Task.Delay(500);

        Assert.Single(manager.Received); // still only the one from before stop
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task WaitForCountAsync(RecordingManager manager, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (manager.Received.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(manager.Received.Count >= count,
            $"Expected {count} message(s) but only {manager.Received.Count} arrived.");
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class RecordingManager : IProbahoSseManager
    {
        private readonly List<IProbahoSseEvent> _store = [];

        public IReadOnlyList<IProbahoSseEvent> Received => _store;

        public int GetConnectionCount() => 0;
        public int GetGroupConnectionCount(string group) => 0;
        public bool TryRegister(IProbahoSseConnection connection) => true;
        public void Unregister(string connectionId) { }

        public Task BroadcastAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
        {
            lock (_store) _store.Add(sseEvent);
            return Task.CompletedTask;
        }

        public Task SendToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
