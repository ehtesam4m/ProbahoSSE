using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Extensions;
using StackExchange.Redis;

namespace ProbahoSSE.RedisStream;

/// <summary>
/// Extension methods for registering the Redis Stream backplane with ProbahoSSE.
/// </summary>
public static class RedisStreamBackplaneExtensions
{
    /// <summary>
    /// Adds the Redis Streams backplane implementation to ProbahoSSE.
    /// Provides at-least-once delivery semantics with Last-Event-ID message replay.
    /// </summary>
    /// <param name="builder">The ProbahoSSE builder.</param>
    /// <param name="configure">Delegate to configure <see cref="RedisStreamOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static ProbahoSseBuilder AddRedisStreamBackplane(
        this ProbahoSseBuilder builder,
        Action<RedisStreamOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.Configure<RedisStreamOptions>(_ => { });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisStreamOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        builder.Services.AddSingleton<RedisStreamBackplane>();
        builder.Services.AddSingleton<IProbahoSseBackplane>(sp => sp.GetRequiredService<RedisStreamBackplane>());
        builder.Services.AddSingleton<IProbahoSsePublisher>(sp => sp.GetRequiredService<RedisStreamBackplane>());
        builder.Services.AddSingleton<IProbahoSseReplayable>(sp => sp.GetRequiredService<RedisStreamBackplane>());
        builder.Services.AddHostedService<RedisStreamListenerService>();

        return builder;
    }
}
