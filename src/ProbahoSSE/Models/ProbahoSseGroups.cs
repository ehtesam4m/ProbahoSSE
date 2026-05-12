namespace ProbahoSSE.Models;

/// <summary>
/// Well-known group name constants for ProbahoSSE event routing.
/// </summary>
public static class ProbahoSseGroups
{
    /// <summary>
    /// Sentinel group for intentional fan-out to ALL connected clients across every server instance.
    /// Set <see cref="IProbahoSseEvent.Group"/> to this value — or call
    /// <see cref="Abstractions.IProbahoSsePublisher.PublishToAllAsync"/> — to broadcast explicitly.
    /// Events with no group set are dropped by the listener services to prevent accidental data leaks.
    /// </summary>
    public const string Broadcast = "__broadcast__";
}

