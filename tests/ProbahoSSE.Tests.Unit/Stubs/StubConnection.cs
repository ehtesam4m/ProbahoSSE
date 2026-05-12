using ProbahoSSE.Abstractions;

namespace ProbahoSSE.Tests.Unit.Stubs;

/// <summary>
/// Manual stub for <see cref="IProbahoSseConnection"/>.
/// Records every event sent via <see cref="SendAsync"/>.
/// </summary>
internal sealed class StubConnection : IProbahoSseConnection
{
    public string ConnectionId { get; }
    public string? Group { get; }
    public bool IsConnected { get; private set; } = true;

    public List<IProbahoSseEvent> Received { get; } = [];
    public bool SendShouldThrow { get; set; }

    public StubConnection(string? group = null, string? connectionId = null)
    {
        Group = group;
        ConnectionId = connectionId ?? Guid.NewGuid().ToString("N");
    }

    public ValueTask SendAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        if (SendShouldThrow) throw new InvalidOperationException("Send failed");
        Received.Add(sseEvent);
        return ValueTask.CompletedTask;
    }

    public void Disconnect() => IsConnected = false;
}
