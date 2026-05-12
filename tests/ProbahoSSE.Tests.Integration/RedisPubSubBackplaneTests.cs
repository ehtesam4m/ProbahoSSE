using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane.Redis;
using ProbahoSSE.Models;
using ProbahoSSE.RedisPubSub;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisPubSubBackplane"/> and <see cref="RedisPubSubListenerService"/>
/// using a real Redis instance via Testcontainers.
/// Verifies: publish reaches listener, fan-out across two instances, connection cleanup.
/// </summary>
public sealed class RedisPubSubBackplaneTests : IAsyncLifetime
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

    private RedisPubSubBackplane MakeBackplane(string prefix = "test")
    {
        var opts = Options.Create(new RedisBackplaneOptions { ChannelPrefix = prefix });
        return new RedisPubSubBackplane(_mux, opts, NullLogger<RedisPubSubBackplane>.Instance);
    }

    // ── Publish → Listener → Manager.BroadcastAsync ─────────────────────────

    [DockerAvailableFact]
    public async Task PublishAsync_MessageDeliveredToListenerService()
    {
        var backplane = MakeBackplane("pubsub-basic");
        var received = new List<IProbahoSseEvent>();
        var manager = new RecordingManager(received);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new RedisPubSubListenerService(
            backplane, manager, NullLogger<RedisPubSubListenerService>.Instance);
        await listener.StartAsync(cts.Token);

        // Give subscription time to establish
        await Task.Delay(300, cts.Token);

        var evt = ProbahoSseEvent.Create("hello-pubsub", "update");
        await backplane.PublishToAllAsync(evt);

        // Wait for delivery
        await WaitForConditionAsync(() => received.Count >= 1, TimeSpan.FromSeconds(5));

        Assert.Single(received);
        Assert.Equal("hello-pubsub", received[0].Data);
        Assert.Equal("update", received[0].EventType);

        await listener.StopAsync(CancellationToken.None);
    }

    // ── Fan-out: two listeners on same channel both receive ──────────────────

    [DockerAvailableFact]
    public async Task PublishAsync_TwoListeners_BothReceiveSameMessage()
    {
        // Two separate backplane instances (simulating two server processes) on the same channel
        var bp1 = MakeBackplane("pubsub-fanout");
        var bp2 = MakeBackplane("pubsub-fanout");

        var received1 = new List<IProbahoSseEvent>();
        var received2 = new List<IProbahoSseEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var l1 = new RedisPubSubListenerService(bp1, new RecordingManager(received1), NullLogger<RedisPubSubListenerService>.Instance);
        var l2 = new RedisPubSubListenerService(bp2, new RecordingManager(received2), NullLogger<RedisPubSubListenerService>.Instance);

        await l1.StartAsync(cts.Token);
        await l2.StartAsync(cts.Token);
        await Task.Delay(300, cts.Token);

        await bp1.PublishToAllAsync(ProbahoSseEvent.Create("fan-out-msg"));

        await WaitForConditionAsync(() => received1.Count >= 1 && received2.Count >= 1, TimeSpan.FromSeconds(5));

        // Both instances must receive the event — this is the proof of fan-out
        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal("fan-out-msg", received1[0].Data);
        Assert.Equal("fan-out-msg", received2[0].Data);

        await l1.StopAsync(CancellationToken.None);
        await l2.StopAsync(CancellationToken.None);
    }

    // ── Multiple messages delivered in order ─────────────────────────────────

    [DockerAvailableFact]
    public async Task PublishAsync_MultipleEvents_AllDeliveredInOrder()
    {
        var backplane = MakeBackplane("pubsub-order");
        var received = new List<IProbahoSseEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new RedisPubSubListenerService(
            backplane, new RecordingManager(received), NullLogger<RedisPubSubListenerService>.Instance);
        await listener.StartAsync(cts.Token);
        await Task.Delay(300, cts.Token);

        for (int i = 1; i <= 5; i++)
            await backplane.PublishToAllAsync(ProbahoSseEvent.Create($"msg-{i}"));

        await WaitForConditionAsync(() => received.Count >= 5, TimeSpan.FromSeconds(5));

        Assert.Equal(5, received.Count);
        for (int i = 0; i < 5; i++)
            Assert.Equal($"msg-{i + 1}", received[i].Data);

        await listener.StopAsync(CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(condition(), "Condition not met within timeout.");
    }

    /// <summary>Manual stub manager that records all broadcast calls.</summary>
    private sealed class RecordingManager : IProbahoSseManager
    {
        private readonly List<IProbahoSseEvent> _store;
        public RecordingManager(List<IProbahoSseEvent> store) => _store = store;
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




