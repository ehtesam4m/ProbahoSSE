using Microsoft.AspNetCore.Http;

namespace ProbahoSSE.Middleware;

/// <summary>
/// Optional ASP.NET Core middleware that enables SSE streaming for matched routes.
/// Use <see cref="Extensions.ProbahoSseApplicationBuilderExtensions.UseProbahoSse"/> to register.
/// </summary>
public sealed class ProbahoSseMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Initializes the middleware.</summary>
    public ProbahoSseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Processes the request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Middleware passes through; SSE is handled at the endpoint level.
        await _next(context);
    }
}

