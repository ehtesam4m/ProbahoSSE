using ProbahoSSE.Tests.Unit.Stubs;
using ProbahoSSE.Core;
using ProbahoSSE.Models;

namespace ProbahoSSE.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SseConnectionManager"/>.
/// No Redis, no HTTP — just the in-process manager and stubs.
/// </summary>
public sealed class SseConnectionManagerTests
{
    // ── Registration ────────────────────────────────────────────────────────

    [Fact]
    public void TryRegister_FirstConnection_ReturnsTrue()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var conn = new StubConnection();

        var result = manager.TryRegister(conn);

        Assert.True(result);
        Assert.Equal(1, manager.GetConnectionCount());
    }

    [Fact]
    public void TryRegister_DuplicateConnectionId_ReturnsFalse()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var id = Guid.NewGuid().ToString("N");

        manager.TryRegister(new StubConnection(connectionId: id));
        var result = manager.TryRegister(new StubConnection(connectionId: id));

        Assert.False(result);
        Assert.Equal(1, manager.GetConnectionCount());
    }

    [Fact]
    public void TryRegister_ExceedsGlobalLimit_ReturnsFalse()
    {
        var manager = new ConnectionManagerBuilder().WithMaxGlobal(2).Build();

        Assert.True(manager.TryRegister(new StubConnection()));
        Assert.True(manager.TryRegister(new StubConnection()));
        Assert.False(manager.TryRegister(new StubConnection()));
        Assert.Equal(2, manager.GetConnectionCount());
    }

    [Fact]
    public void TryRegister_ExceedsPerGroupLimit_RejectsSameGroup()
    {
        var manager = new ConnectionManagerBuilder().WithMaxPerUser(2).Build();

        Assert.True(manager.TryRegister(new StubConnection(group: "alice")));
        Assert.True(manager.TryRegister(new StubConnection(group: "alice")));
        Assert.False(manager.TryRegister(new StubConnection(group: "alice")));
        // Different group is NOT affected by alice's limit
        Assert.True(manager.TryRegister(new StubConnection(group: "bob")));
    }

    [Fact]
    public void TryRegister_NoGroup_NotCountedAgainstPerGroupLimit()
    {
        var manager = new ConnectionManagerBuilder().WithMaxPerUser(1).Build();

        // Anonymous connections should not be blocked by per-group limit
        Assert.True(manager.TryRegister(new StubConnection(group: null)));
        Assert.True(manager.TryRegister(new StubConnection(group: null)));
    }

    // ── Unregistration ──────────────────────────────────────────────────────

    [Fact]
    public void Unregister_ExistingConnection_DecrementsCount()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var conn = new StubConnection(group: "alice");
        manager.TryRegister(conn);

        manager.Unregister(conn.ConnectionId);

        Assert.Equal(0, manager.GetConnectionCount());
        Assert.Equal(0, manager.GetGroupConnectionCount("alice"));
    }

    [Fact]
    public void Unregister_UnknownId_DoesNotThrow()
    {
        var manager = new ConnectionManagerBuilder().Build();
        // Should not throw
        manager.Unregister("non-existent-id");
    }

    [Fact]
    public void Unregister_AllowsNewRegistrationAfterLimit()
    {
        var manager = new ConnectionManagerBuilder().WithMaxGlobal(1).Build();
        var conn = new StubConnection();
        manager.TryRegister(conn);

        manager.Unregister(conn.ConnectionId);

        Assert.True(manager.TryRegister(new StubConnection()));
    }

    // ── GetGroupConnectionCount ──────────────────────────────────────────────

    [Fact]
    public void GetGroupConnectionCount_NoConnections_ReturnsZero()
    {
        var manager = new ConnectionManagerBuilder().Build();
        Assert.Equal(0, manager.GetGroupConnectionCount("nobody"));
    }

    [Fact]
    public void GetGroupConnectionCount_AfterRegister_ReturnsCorrectCount()
    {
        var manager = new ConnectionManagerBuilder().Build();
        manager.TryRegister(new StubConnection(group: "alice"));
        manager.TryRegister(new StubConnection(group: "alice"));
        manager.TryRegister(new StubConnection(group: "bob"));

        Assert.Equal(2, manager.GetGroupConnectionCount("alice"));
        Assert.Equal(1, manager.GetGroupConnectionCount("bob"));
    }

    // ── BroadcastAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task BroadcastAsync_DeliversToAllConnections()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var conn1 = new StubConnection();
        var conn2 = new StubConnection();
        manager.TryRegister(conn1);
        manager.TryRegister(conn2);

        var evt = ProbahoSseEvent.Create("hello");
        await manager.BroadcastAsync(evt);

        Assert.Single(conn1.Received);
        Assert.Single(conn2.Received);
        Assert.Equal("hello", conn1.Received[0].Data);
        Assert.Equal("hello", conn2.Received[0].Data);
    }

    [Fact]
    public async Task BroadcastAsync_NoConnections_DoesNotThrow()
    {
        var manager = new ConnectionManagerBuilder().Build();
        await manager.BroadcastAsync(ProbahoSseEvent.Create("empty broadcast"));
    }

    [Fact]
    public async Task BroadcastAsync_OneConnectionThrows_OthersStillReceive()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var bad  = new StubConnection { SendShouldThrow = true };
        var good = new StubConnection();
        manager.TryRegister(bad);
        manager.TryRegister(good);

        // BroadcastAsync uses Task.WhenAll — if one throws, an exception is raised
        // but all tasks are still started. The good connection should receive before the throw.
        try
        {
            await manager.BroadcastAsync(ProbahoSseEvent.Create("test"));
        }
        catch (Exception ex) when (ex is AggregateException || ex is InvalidOperationException)
        {
            // expected — one connection threw
        }

        Assert.Single(good.Received);
    }

    // ── SendToGroupAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendToGroupAsync_OnlySendsToTargetGroup()
    {
        var manager = new ConnectionManagerBuilder().Build();
        var alice1 = new StubConnection(group: "alice");
        var alice2 = new StubConnection(group: "alice");
        var bob    = new StubConnection(group: "bob");
        manager.TryRegister(alice1);
        manager.TryRegister(alice2);
        manager.TryRegister(bob);

        var evt = ProbahoSseEvent.Create("for alice");
        await manager.SendToGroupAsync("alice", evt);

        Assert.Single(alice1.Received);
        Assert.Single(alice2.Received);
        Assert.Empty(bob.Received);
    }

    [Fact]
    public async Task SendToGroupAsync_UnknownGroup_DoesNotThrow()
    {
        var manager = new ConnectionManagerBuilder().Build();
        await manager.SendToGroupAsync("ghost", ProbahoSseEvent.Create("nobody home"));
    }
}

