namespace ProbahoSSE.Backplane.Redis;

/// <summary>
/// Configuration options for Redis-based backplane implementations.
/// </summary>
public sealed class RedisBackplaneOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string (e.g., "localhost:6379").
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis channels and stream keys.
    /// Default is "probaho".
    /// </summary>
    public string ChannelPrefix { get; set; } = "probaho";

    /// <summary>
    /// Gets or sets the maximum number of messages to retain in a Redis Stream.
    /// Used with XTRIM MAXLEN to prevent memory exhaustion. Default is 10,000.
    /// </summary>
    public int StreamMaxLength { get; set; } = 10_000;
}

