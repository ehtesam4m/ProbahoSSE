using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
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
    /// <param name="configure">Delegate to configure <see cref="RedisPubSubOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static ProbahoSseBuilder AddRedisPubSubBackplane(
        this ProbahoSseBuilder builder,
        Action<RedisPubSubOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.Configure<RedisPubSubOptions>(_ => { });

        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisPubSubOptions>>().Value;
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        builder.Services.AddSingleton<RedisPubSubBackplane>();
        builder.Services.AddSingleton<IProbahoSseBackplane>(sp => sp.GetRequiredService<RedisPubSubBackplane>());
        builder.Services.AddSingleton<IProbahoSsePublisher>(sp => sp.GetRequiredService<RedisPubSubBackplane>());
        builder.Services.AddHostedService<RedisPubSubListenerService>();

        return builder;
    }
}
