using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Core;
using ProbahoSSE.Models;

namespace ProbahoSSE.Extensions;

/// <summary>
/// Extension methods for registering ProbahoSSE services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ProbahoSseServiceCollectionExtensions
{
    /// <summary>
    /// Adds ProbahoSSE core services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="ProbahoSseOptions"/>.</param>
    /// <returns>A <see cref="ProbahoSseBuilder"/> for further configuration.</returns>
    public static ProbahoSseBuilder AddProbahoSse(
        this IServiceCollection services,
        Action<ProbahoSseOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<ProbahoSseOptions>(_ => { });

        services.AddSingleton<SseConnectionManager>();
        services.AddSingleton<IProbahoSseManager>(sp => sp.GetRequiredService<SseConnectionManager>());

        // IMeterFactory is registered by AddMetrics() which ASP.NET Core calls automatically,
        // but we call it explicitly here so ProbahoSSE works in non-web host scenarios too.
        services.AddMetrics();
        services.AddSingleton<ProbahoSseMetrics>();

        return new ProbahoSseBuilder(services);
    }
}



