using StackExchange.Redis;

namespace ProbahoSSE.RedisStream;

/// <summary>
/// Configuration options for the Redis Streams backplane.
/// </summary>
public sealed class RedisStreamOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string (e.g., "localhost:6379").
    /// Parsed into a <see cref="ConfigurationOptions"/> before connecting.
    /// Use <see cref="ConfigureOptions"/> for advanced settings.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis stream keys.
    /// Default is "probaho".
    /// </summary>
    public string ChannelPrefix { get; set; } = "probaho";

    /// <summary>
    /// Gets or sets the maximum number of messages to retain in the Redis Stream.
    /// Used with XTRIM MAXLEN to prevent memory exhaustion. Default is 10,000.
    /// </summary>
    public int StreamMaxLength { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the polling interval in milliseconds used by the listener loop
    /// when no new messages are available. Lower values reduce delivery latency at
    /// the cost of more frequent Redis commands. Default is 100ms (10 polls/second).
    /// </summary>
    public int StreamPollingIntervalMs { get; set; } = 500;

    /// <summary>
    /// Optional callback to configure the underlying <see cref="ConfigurationOptions"/>
    /// before the <see cref="IConnectionMultiplexer"/> is created.
    /// Use this for full control over StackExchange.Redis settings such as timeouts,
    /// retry policy, SSL, reconnect behaviour, etc.
    /// </summary>
    /// <example>
    /// redis.ConfigureOptions = opt =>
    /// {
    ///     opt.ConnectTimeout         = 5000;
    ///     opt.AbortOnConnectFail     = false;
    ///     opt.ReconnectRetryPolicy   = new ExponentialRetry(5000);
    ///     opt.Ssl                    = true;
    /// };
    /// </example>
    public Action<ConfigurationOptions>? ConfigureOptions { get; set; }
}
