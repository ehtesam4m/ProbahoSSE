using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Extensions;

namespace ProbahoSSE.RabbitMq;

/// <summary>
/// Extension methods for registering the RabbitMQ fanout backplane with ProbahoSSE.
/// </summary>
public static class RabbitMqBackplaneExtensions
{
    /// <summary>
    /// Adds the RabbitMQ fanout backplane implementation to ProbahoSSE.
    /// </summary>
    /// <param name="builder">The ProbahoSSE builder.</param>
    /// <param name="configure">Delegate to configure <see cref="RabbitMqOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static ProbahoSseBuilder AddRabbitMqBackplane(
        this ProbahoSseBuilder builder,
        Action<RabbitMqOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.Configure<RabbitMqOptions>(_ => { });

        // The backplane holds the publish channel; the listener service owns the connection
        // and calls backplane.SetPublishChannel(...) during StartAsync.
        builder.Services.AddSingleton<RabbitMqBackplane>();
        builder.Services.AddSingleton<IProbahoSseBackplane>(
            sp => sp.GetRequiredService<RabbitMqBackplane>());
        builder.Services.AddSingleton<IProbahoSsePublisher>(
            sp => sp.GetRequiredService<RabbitMqBackplane>());
        builder.Services.AddHostedService<RabbitMqListenerService>();

        return builder;
    }
}

