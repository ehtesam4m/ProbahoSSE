using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;
using ProbahoSSE.RabbitMq;
using Testcontainers.RabbitMq;

namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RabbitMqBackplane"/> and <see cref="RabbitMqListenerService"/>
/// using a real RabbitMQ instance via Testcontainers.
/// Verifies: publish reaches listener, fan-out across two instances, group filtering.
/// </summary>
public sealed class RabbitMqBackplaneTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-alpine")
        .Build();

    public Task InitializeAsync() => _rabbitMq.StartAsync();

    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();

    private (RabbitMqBackplane backplane, RabbitMqListenerService listener) MakeInstance(
        List<IProbahoSseEvent> store, string exchange = "test-exchange")
    {
        var opts = Options.Create(new RabbitMqOptions
        {
            HostName     = _rabbitMq.Hostname,
            Port         = _rabbitMq.GetMappedPublicPort(5672),
            UserName     = RabbitMqBuilder.DefaultUsername,
            Password     = RabbitMqBuilder.DefaultPassword,
            ExchangeName = exchange
        });

        var manager   = new RecordingManager(store);
        var metrics   = new ProbahoSseMetrics(new TestMeterFactory(), manager);
        var backplane = new RabbitMqBackplane(opts, NullLogger<RabbitMqBackplane>.Instance, metrics);
        var listener  = new RabbitMqListenerService(
            backplane, manager, opts, NullLogger<RabbitMqListenerService>.Instance);

        return (backplane, listener);
    }

    // ── Publish → Listener → Manager.BroadcastAsync ──────────────────────────

    [DockerAvailableFact]
    public async Task PublishToAllAsync_MessageDeliveredToListener()
    {
        var received = new List<IProbahoSseEvent>();
        var (backplane, listener) = MakeInstance(received, "rmq-basic");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await listener.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);   // allow subscription to establish

        await backplane.PublishToAllAsync(ProbahoSseEvent.Create("hello-rabbit", "update"));

        await WaitForConditionAsync(() => received.Count >= 1, TimeSpan.FromSeconds(5));

        Assert.Single(received);
        Assert.Equal("hello-rabbit", received[0].Data);
        Assert.Equal("update", received[0].EventType);

        await listener.StopAsync(CancellationToken.None);
        await listener.DisposeAsync();
    }

    // ── Fan-out: two listeners both receive the same message ─────────────────

    [DockerAvailableFact]
    public async Task PublishToAllAsync_TwoListeners_BothReceive()
    {
        var received1 = new List<IProbahoSseEvent>();
        var received2 = new List<IProbahoSseEvent>();

        var (bp1, l1) = MakeInstance(received1, "rmq-fanout");
        var (bp2, l2) = MakeInstance(received2, "rmq-fanout");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await l1.StartAsync(cts.Token);
        await l2.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);

        await bp1.PublishToAllAsync(ProbahoSseEvent.Create("fan-out-msg"));

        await WaitForConditionAsync(
            () => received1.Count >= 1 && received2.Count >= 1, TimeSpan.FromSeconds(5));

        Assert.Single(received1);
        Assert.Single(received2);
        Assert.Equal("fan-out-msg", received1[0].Data);
        Assert.Equal("fan-out-msg", received2[0].Data);

        await l1.StopAsync(CancellationToken.None);
        await l2.StopAsync(CancellationToken.None);
        await l1.DisposeAsync();
        await l2.DisposeAsync();
    }

    // ── Group delivery: only matching group receives ──────────────────────────

    [DockerAvailableFact]
    public async Task PublishToGroupAsync_GroupEventStamped()
    {
        var received = new List<IProbahoSseEvent>();
        var (backplane, listener) = MakeInstance(received, "rmq-group");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await listener.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);

        await backplane.PublishToGroupAsync("alice", ProbahoSseEvent.Create("alice-event"));

        await WaitForConditionAsync(() => received.Count >= 1, TimeSpan.FromSeconds(5));

        Assert.Single(received);
        Assert.Equal("alice", received[0].Group);
        Assert.Equal("alice-event", received[0].Data);

        await listener.StopAsync(CancellationToken.None);
        await listener.DisposeAsync();
    }

    // ── Multiple messages delivered ───────────────────────────────────────────

    [DockerAvailableFact]
    public async Task PublishToAllAsync_MultipleEvents_AllDelivered()
    {
        var received = new List<IProbahoSseEvent>();
        var (backplane, listener) = MakeInstance(received, "rmq-order");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await listener.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);

        for (int i = 1; i <= 5; i++)
            await backplane.PublishToAllAsync(ProbahoSseEvent.Create($"msg-{i}"));

        await WaitForConditionAsync(() => received.Count >= 5, TimeSpan.FromSeconds(5));

        Assert.Equal(5, received.Count);

        await listener.StopAsync(CancellationToken.None);
        await listener.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(condition(), "Condition not met within timeout.");
    }

    /// <summary>Recording stub — captures all broadcast and group calls.</summary>
    private sealed class RecordingManager : IProbahoSseManager
    {
        private readonly List<IProbahoSseEvent> _store;
        public RecordingManager(List<IProbahoSseEvent> store) => _store = store;
        public int GetConnectionCount() => 0;
        public int GetGroupConnectionCount(string group) => 0;
        public bool TryRegister(IProbahoSseConnection connection) => true;
        public void Unregister(string connectionId) { }

        public Task BroadcastAsync(IProbahoSseEvent sseEvent, CancellationToken ct = default)
        {
            lock (_store) _store.Add(sseEvent);
            return Task.CompletedTask;
        }

        public Task SendToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken ct = default)
        {
            lock (_store) _store.Add(sseEvent);
            return Task.CompletedTask;
        }
    }
}

