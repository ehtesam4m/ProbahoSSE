namespace ProbahoSSE.RedisPubSub;

/// <summary>
/// Configuration options for the Redis Pub/Sub backplane.
/// </summary>
public sealed class RedisPubSubOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string (e.g., "localhost:6379").
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis channel names.
    /// Default is "probaho".
    /// </summary>
    public string ChannelPrefix { get; set; } = "probaho";
}
