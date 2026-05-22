using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Core;

/// <summary>
/// Thread-safe manager for all active SSE connections, enforcing connection limits.
/// </summary>
/// <remarks>
/// Two dictionaries only:
/// <list type="bullet">
///   <item><c>_connections</c> — primary index (connectionId → connection), used for broadcast and Unregister lookups.</item>
///   <item><c>_groupIndex</c> — secondary index (group → ConcurrentDictionary&lt;connectionId, byte&gt;), used for
///   O(group size) targeted sends and per-group counts. No separate counter dictionary is maintained —
///   <c>set.Count</c> is O(1) on <see cref="ConcurrentDictionary{TKey,TValue}"/>.</item>
/// </list>
/// All mutations follow the "always use the value returned by GetOrAdd" rule to avoid
/// lost-update races, and use reference-equality removal to prune empty group entries safely.
/// </remarks>
public sealed class SseConnectionManager : IProbahoSseManager
{
    // Primary index: connectionId → connection
    private readonly ConcurrentDictionary<string, IProbahoSseConnection> _connections = new();

    // Secondary index: group → set of connectionIds  (ConcurrentDictionary<id,byte> used as a ConcurrentHashSet)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _groupIndex = new();

    private readonly ProbahoSseOptions _options;
    private readonly ILogger<SseConnectionManager> _logger;

    /// <summary>Initializes the manager with the given options.</summary>
    public SseConnectionManager(IOptions<ProbahoSseOptions> options, ILogger<SseConnectionManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public int GetConnectionCount() => _connections.Count;

    /// <inheritdoc />
    /// <remarks>Derived from <c>_groupIndex[group].Count</c> — O(1), no separate counter.</remarks>
    public int GetGroupConnectionCount(string group)
        => _groupIndex.TryGetValue(group, out var set) ? set.Count : 0;

    /// <inheritdoc />
    public bool TryRegister(IProbahoSseConnection connection)
    {
        // Check global limit
        if (_options.MaxGlobalConnections > 0 && _connections.Count >= _options.MaxGlobalConnections)
        {
            _logger.LogWarning(
                "Global connection limit reached ({Limit}). Connection {ConnectionId} rejected.",
                _options.MaxGlobalConnections, connection.ConnectionId);
            return false;
        }

        // Check per-group limit — read directly from the index set count.
        // Note: there is an inherent TOCTOU window here between the read and the subsequent
        // TryAdd; under extreme concurrent contention the group may momentarily exceed the
        // limit by a small amount. This is the same trade-off as any lock-free counter
        // approach and is acceptable for connection-cap use cases.
        if (connection.Group is not null && _options.MaxConnectionsPerGroup > 0)
        {
            var groupCount = _groupIndex.TryGetValue(connection.Group, out var existing)
                ? existing.Count
                : 0;
            if (groupCount >= _options.MaxConnectionsPerGroup)
            {
                _logger.LogWarning(
                    "Per-group connection limit reached for group {Group} ({Limit}). Connection {ConnectionId} rejected.",
                    connection.Group, _options.MaxConnectionsPerGroup, connection.ConnectionId);
                return false;
            }
        }

        if (!_connections.TryAdd(connection.ConnectionId, connection))
        {
            _logger.LogWarning(
                "Duplicate connection ID {ConnectionId} — registration ignored.",
                connection.ConnectionId);
            return false;
        }

        if (connection.Group is not null)
        {
            // Always use the set returned by GetOrAdd — the factory may be called
            // multiple times under contention; only one insertion wins, and we must
            // operate on the winner to avoid a lost-update race.
            var set = _groupIndex.GetOrAdd(connection.Group, _ => new ConcurrentDictionary<string, byte>());
            set.TryAdd(connection.ConnectionId, 0);
        }

        _logger.LogDebug(
            "Connection {ConnectionId} registered — group={Group} total={Total}",
            connection.ConnectionId, connection.Group ?? "(none)", _connections.Count);

        return true;
    }

    /// <inheritdoc />
    public void Unregister(string connectionId)
    {
        if (!_connections.TryRemove(connectionId, out var connection))
            return;

        if (connection.Group is not null && _groupIndex.TryGetValue(connection.Group, out var set))
        {
            set.TryRemove(connectionId, out _);

            // Remove the group entry only when the set is empty AND still the same
            // reference — guards against a concurrent TryRegister recreating the slot
            // between our IsEmpty check and the outer Remove call.
            if (set.IsEmpty)
            {
                var pair = new KeyValuePair<string, ConcurrentDictionary<string, byte>>(connection.Group, set);
                ((ICollection<KeyValuePair<string, ConcurrentDictionary<string, byte>>>)_groupIndex).Remove(pair);
            }
        }

        _logger.LogDebug(
            "Connection {ConnectionId} unregistered — group={Group} total={Total}",
            connectionId, connection.Group ?? "(none)", _connections.Count);
    }

    /// <inheritdoc />
    public async Task BroadcastAsync(IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        var tasks = _connections.Values.Select(c => c.SendAsync(sseEvent, cancellationToken).AsTask());
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more errors occurred broadcasting event {EventId}.", sseEvent.Id);
        }
    }

    /// <inheritdoc />
    public async Task SendToGroupAsync(string group, IProbahoSseEvent sseEvent, CancellationToken cancellationToken = default)
    {
        // O(group size) lookup via secondary index — no full scan of _connections.
        if (!_groupIndex.TryGetValue(group, out var ids) || ids.IsEmpty)
        {
            _logger.LogDebug("SendToGroupAsync: no local connections for group {Group} — skipped.", group);
            return;
        }

        var tasks = ids.Keys
            .Select(id =>
            {
                // Guard: connection may have been removed between index lookup and send.
                return _connections.TryGetValue(id, out var conn)
                    ? conn.SendAsync(sseEvent, cancellationToken).AsTask()
                    : Task.CompletedTask;
            });

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more errors occurred sending event {EventId} to group {Group}.", sseEvent.Id, group);
        }
    }
}
