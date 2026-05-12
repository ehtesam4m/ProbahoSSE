using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Core;

/// <summary>
/// Thread-safe manager for all active SSE connections, enforcing connection limits.
/// </summary>
public sealed class SseConnectionManager : IProbahoSseManager
{
    private readonly ConcurrentDictionary<string, IProbahoSseConnection> _connections = new();
    private readonly ConcurrentDictionary<string, int> _groupConnectionCounts = new();
    private readonly ProbahoSseOptions _options;

    /// <summary>Initializes the manager with the given options.</summary>
    public SseConnectionManager(IOptions<ProbahoSseOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public int GetConnectionCount() => _connections.Count;

    /// <inheritdoc />
    public int GetGroupConnectionCount(string group)
        => _groupConnectionCounts.TryGetValue(group, out var count) ? count : 0;

    /// <inheritdoc />
    public bool TryRegister(IProbahoSseConnection connection)
    {
        // Check global limit
        if (_options.MaxGlobalConnections > 0 && _connections.Count >= _options.MaxGlobalConnections)
            return false;

        // Check per-group limit
        if (connection.Group is not null && _options.MaxConnectionsPerUser > 0)
        {
            var groupCount = _groupConnectionCounts.GetOrAdd(connection.Group, 0);
            if (groupCount >= _options.MaxConnectionsPerUser)
                return false;
        }

        if (!_connections.TryAdd(connection.ConnectionId, connection))
            return false;

        if (connection.Group is not null)
            _groupConnectionCounts.AddOrUpdate(connection.Group, 1, (_, c) => c + 1);

        return true;
    }

    /// <inheritdoc />
    public void Unregister(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection) && connection.Group is not null)
        {
            _groupConnectionCounts.AddOrUpdate(connection.Group, 0, (_, c) => Math.Max(0, c - 1));
        }
    }

    /// <inheritdoc />
    public async Task BroadcastAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var tasks = _connections.Values.Select(c => c.SendAsync(sseEvent, cancellationToken).AsTask());
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var groupConnections = _connections.Values
            .Where(c => string.Equals(c.Group, group, StringComparison.Ordinal));

        var tasks = groupConnections.Select(c => c.SendAsync(sseEvent, cancellationToken).AsTask());
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
