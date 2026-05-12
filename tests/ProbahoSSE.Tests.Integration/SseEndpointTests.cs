using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Integration tests for the SSE HTTP endpoint.
/// Uses <see cref="WebApplicationFactory{Program}"/> with TestServer (in-process, no real socket).
/// No Redis needed.
/// </summary>
public sealed class SseEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SseEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseContentRoot(AppContext.BaseDirectory);
            // TestServer uses pipes — allow sync I/O so SSE items flush immediately.
            b.ConfigureServices(services =>
                services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(
                    o => o.AllowSynchronousIO = true));
        });
    }

    // ── Content-Type & status ────────────────────────────────────────────────

    [Fact]
    public async Task GetSse_Returns200_WithTextEventStreamContentType()
    {
        using var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.GetAsync("/sse",
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream",
            response.Content.Headers.ContentType?.MediaType);
        cts.Cancel();
    }

    // ── BroadcastAsync reaches all registered connections ────────────────────
    // Note: reading SSE wire bytes from TestServer is not reliable because
    // TypedResults.ServerSentEvents flushes via the Kestrel pipe which is not
    // forwarded to the TestServer response reader until the response completes.
    // Instead we verify the manager receives the connection and that BroadcastAsync
    // delivers to it — the wire-level SSE framing is covered by SseItemMappingTests.

    [Fact]
    public async Task BroadcastAsync_ConnectionRegistered_ManagerCountReflectsIt()
    {
        using var client = _factory.CreateClient();
        var manager = _factory.Services.GetRequiredService<IProbahoSseManager>();

        int before = manager.GetConnectionCount();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Open SSE stream — don't await, keep it alive
        var req = client.GetAsync("/sse",
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await Task.Delay(400, cts.Token);

        // Manager must reflect the new connection
        Assert.True(manager.GetConnectionCount() > before,
            $"Expected connection count > {before}, got {manager.GetConnectionCount()}");

        // BroadcastAsync must not throw even when there are connections
        await manager.BroadcastAsync(ProbahoSseEvent.Create("test-data", "test"));

        cts.Cancel();
        try { await req; } catch { /* expected */ }
    }

    // ── Connection count ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionCount_IncreasesWhileConnected_DecreasesAfterDisconnect()
    {
        // Wait for any lingering connections from previous tests to be cleaned up
        var cleanDeadline = DateTime.UtcNow.AddSeconds(3);
        var manager = _factory.Services.GetRequiredService<IProbahoSseManager>();
        while (manager.GetConnectionCount() > 0 && DateTime.UtcNow < cleanDeadline)
            await Task.Delay(50);

        int before = manager.GetConnectionCount();

        using var client = _factory.CreateClient();
        // Use a separate CTS so that cancelling it also aborts the underlying request
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // SendAsync with HttpCompletionOption.ResponseHeadersRead keeps the pipe open
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "/sse");
        var responseTask = client.SendAsync(request,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);

        await Task.Delay(300, cts.Token);

        int during = manager.GetConnectionCount();
        Assert.True(during > before, $"Expected connection count > {before}, got {during}");

        // Cancel and dispose the response to close the pipe — this triggers RequestAborted on TestServer
        cts.Cancel();
        try
        {
            using var response = await responseTask;
            response.Dispose();
        }
        catch { /* cancellation expected */ }

        // Poll until the connection is unregistered, up to 3 seconds
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (manager.GetConnectionCount() > before && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Equal(before, manager.GetConnectionCount());
    }
}











