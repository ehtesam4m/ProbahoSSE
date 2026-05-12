using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProbahoSSE.Core;
using ProbahoSSE.Middleware;
#pragma warning disable CS0618

namespace ProbahoSSE.Extensions;

/// <summary>
/// Extension methods for configuring ProbahoSSE in the ASP.NET Core request pipeline.
/// </summary>
public static class ProbahoSseApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="ProbahoSseMiddleware"/> to the request processing pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for chaining.</returns>
    public static IApplicationBuilder UseProbahoSse(this IApplicationBuilder app)
        => app.UseMiddleware<ProbahoSseMiddleware>();

    /// <summary>
    /// Maps an SSE endpoint at the specified route pattern.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern (e.g. "/sse").</param>
    /// <param name="getGroup">Optional delegate to extract the group name from the <see cref="HttpContext"/>.</param>
    /// <returns>A <see cref="RouteHandlerBuilder"/> for further configuration.</returns>
    public static IEndpointConventionBuilder MapProbahoSse(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<HttpContext, string?>? getGroup = null)
    {
        return endpoints.MapGet(pattern, (HttpContext context) =>
        {
            var group = getGroup?.Invoke(context);
            return SseEndpointHandler.HandleAsync(context, group);
        });
    }
}



