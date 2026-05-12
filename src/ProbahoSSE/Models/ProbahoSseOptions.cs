namespace ProbahoSSE.Models;

/// <summary>
/// Configuration options for the ProbahoSSE library.
/// </summary>
public sealed class ProbahoSseOptions
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent SSE connections across all users.
    /// Set to 0 for unlimited. Default is 0.
    /// </summary>
    public int MaxGlobalConnections { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum number of concurrent SSE connections per user.
    /// Set to 0 for unlimited. Default is 10.
    /// </summary>
    public int MaxConnectionsPerUser { get; set; } = 10;

    /// <summary>
    /// Gets or sets the default event type used when an event has no explicit type.
    /// Default is "message".
    /// </summary>
    public string DefaultEventType { get; set; } = "message";

    /// <summary>
    /// Gets or sets the interval for sending keep-alive ping comments to the client.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
}

