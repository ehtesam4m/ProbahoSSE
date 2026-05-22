using StackExchange.Redis;

namespace ProbahoSSE.RedisPubSub;

/// <summary>
/// Configuration options for the Redis Pub/Sub backplane.
/// </summary>
public sealed class RedisPubSubOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string (e.g., "localhost:6379").
    /// Parsed into a <see cref="ConfigurationOptions"/> before connecting.
    /// Use <see cref="ConfigureOptions"/> for advanced settings.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis channel names.
    /// Default is "probaho".
    /// </summary>
    public string ChannelPrefix { get; set; } = "probaho";

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
