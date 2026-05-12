using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Backplane.Redis;
using ProbahoSSE.Extensions;
using StackExchange.Redis;

namespace ProbahoSSE.RedisPubSub;

/// <summary>
/// Extension methods for registering the Redis Pub/Sub backplane with ProbahoSSE.
/// </summary>
public static class RedisPubSubBackplaneExtensions
{
    /// <summary>
    /// Adds the Redis Pub/Sub backplane implementation to ProbahoSSE.
    /// </summary>
    /// <param name="builder">The ProbahoSSE builder.</param>
    /// <param name="configure">Delegate to configure <see cref="RedisBackplaneOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static ProbahoSseBuilder AddRedisPubSubBackplane(
        this ProbahoSseBuilder builder,
        Action<RedisBackplaneOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.Configure<RedisBackplaneOptions>(_ => { });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisBackplaneOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        builder.Services.AddSingleton<RedisPubSubBackplane>();
        builder.Services.AddSingleton<IProbahoSseBackplane>(sp => sp.GetRequiredService<RedisPubSubBackplane>());
        // Register IProbahoSsePublisher so application code (e.g. KafkaConsumerService)
        // can inject the narrow publishing interface without knowing the backplane type.
        builder.Services.AddSingleton<IProbahoSsePublisher>(sp => sp.GetRequiredService<RedisPubSubBackplane>());
        builder.Services.AddHostedService<RedisPubSubListenerService>();

        return builder;
    }
}

