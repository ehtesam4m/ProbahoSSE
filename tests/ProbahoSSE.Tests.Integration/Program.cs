// Top-level entry point for the integration test host.
// WebApplicationFactory<Program> uses this to spin up an in-process TestServer.
// xunit never executes this code — it has its own runner entry point.
// CS7022 is suppressed in the .csproj.
using ProbahoSSE.Abstractions;
using ProbahoSSE.Core;
using ProbahoSSE.Extensions;
using ProbahoSSE.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProbahoSse();
var app = builder.Build();

app.MapGet("/sse", (HttpContext ctx) => SseEndpointHandler.HandleAsync(ctx));
app.MapGet("/broadcast", async (IProbahoSseManager mgr, string data, string? eventType) =>
{
    await mgr.BroadcastAsync(ProbahoSseEvent.Create(data, eventType));
    return Results.Ok();
});

app.Run();

// Required so WebApplicationFactory<Program> can reference this assembly's entry type.
public partial class Program { }






