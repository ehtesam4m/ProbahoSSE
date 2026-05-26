using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProbahoSSE.Abstractions;
using ProbahoSSE.Models;

namespace ProbahoSSE.Core;

/// <summary>
/// Handles incoming SSE HTTP requests, registering connections and streaming events.
/// Uses <see cref="TypedResults.ServerSentEvents"/> for standards-compliant SSE framing.
/// </summary>
public static class SseEndpointHandler
{
    /// <summary>
    /// Handles an HTTP request as a Server-Sent Events stream.
    /// </summary>
    /// <param name="context">The HTTP context for the request.</param>
    /// <param name="group">Optional group name to associate with the connection for targeted delivery.</param>
    public static async Task HandleAsync(HttpContext context, string? group = null)
    {
        var manager = context.RequestServices.GetRequiredService<IProbahoSseManager>();
        var options = context.RequestServices.GetRequiredService<IOptions<ProbahoSseOptions>>().Value;
        var logger = context.RequestServices.GetRequiredService<ILogger<SseConnection>>();
        var metrics = context.RequestServices.GetRequiredService<ProbahoSseMetrics>();

        // Start a Server span that covers the full SSE connection lifetime.
        // When OTel is configured, this automatically becomes a child of the
        // ASP.NET Core HTTP span (Activity.Current at call time), keeping the same TraceId.
        using var activity = ProbahoSseTelemetry.ActivitySource.StartActivity(
            ProbahoSseTelemetry.Activities.Connection, ActivityKind.Server);
        activity?.SetTag(ProbahoSseTelemetry.Tags.Group, group ?? "(none)");

        using var connection = new SseConnection(group);

        if (!manager.TryRegister(connection))
        {
            logger.LogWarning(
                "SSE connection rejected (429): group={Group} globalCount={GlobalCount} perGroupCount={PerGroupCount}",
                group ?? "(none)",
                manager.GetConnectionCount(),
                group is not null ? manager.GetGroupConnectionCount(group) : 0);

            activity?.SetTag("http.status_code", 429);
            activity?.SetStatus(ActivityStatusCode.Error, "Too many connections");

            metrics.RecordConnectionRejected(group);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Too many connections.", context.RequestAborted);
            return;
        }

        activity?.SetTag(ProbahoSseTelemetry.Tags.ConnectionId, connection.ConnectionId);

        logger.LogDebug(
            "SSE connection {ConnectionId} opened — group={Group} totalConnections={Total}",
            connection.ConnectionId, group ?? "(none)", manager.GetConnectionCount());

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            // TypedResults.ServerSentEvents sets Content-Type: text/event-stream, writes correct
            // SSE wire framing (id:, event:, data: lines + blank-line separator) and flushes
            // after every item — no manual response.WriteAsync needed anywhere.
            var result = TypedResults.ServerSentEvents(
                EventStreamAsync(context, connection, options, logger, cts.Token));

            await result.ExecuteAsync(context);
            await cts.CancelAsync();
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SSE connection {ConnectionId}", connection.ConnectionId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            manager.Unregister(connection.ConnectionId);
            logger.LogDebug(
                "SSE connection {ConnectionId} closed — group={Group} totalConnections={Total}",
                connection.ConnectionId, group ?? "(none)", manager.GetConnectionCount());
        }
    }

    /// <summary>
    /// Single merged async-enumerable that drives <see cref="TypedResults.ServerSentEvents"/>:
    /// <list type="number">
    ///   <item>Replays missed events (if backplane is <see cref="IProbahoSseReplayable"/> and
    ///   client sent <c>Last-Event-ID</c>).</item>
    ///   <item>Streams live events from the connection channel, interleaving keep-alive
    ///   comment frames when the timer fires.</item>
    /// </list>
    /// </summary>
    private static async IAsyncEnumerable<SseItem<string>> EventStreamAsync(
        HttpContext context,
        SseConnection connection,
        ProbahoSseOptions options,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ── Phase 0: connected event ─────────────────────────────────────────
        yield return new SseItem<string>("connected", eventType: "connected");

        // ── Phase 1: replay missed events ────────────────────────────────────
        var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(lastEventId))
        {
            var replayable = context.RequestServices.GetService<IProbahoSseReplayable>();
            if (replayable is not null)
            {
                logger.LogInformation(
                    "Replaying from Last-Event-ID={Id} for connection {Conn}",
                    lastEventId, connection.ConnectionId);

                await foreach (var item in ReplayAsync(replayable, lastEventId, options, cancellationToken))
                    yield return item;
            }
        }

        // ── Phase 2: live stream + keep-alive ────────────────────────────────
        // Race the channel reader against a periodic timer so keep-alives are
        // emitted even during long quiet periods with no incoming events.
        using var keepAliveTimer = new PeriodicTimer(options.KeepAliveInterval);
        var keepAliveTask = keepAliveTimer.WaitForNextTickAsync(cancellationToken).AsTask();

        var reader = connection.ChannelReader;
        var waitToReadTask = reader.WaitToReadAsync(cancellationToken).AsTask();

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.WhenAny(waitToReadTask, keepAliveTask);

            // Drain all currently available events first.
            while (reader.TryRead(out var sseEvent))
            {
                yield return ToSseItem(sseEvent, options);
            }

            // If the channel is permanently closed, exit.
            if (reader.Completion.IsCompleted) yield break;

            // Refresh the WaitToRead task if it completed (channel had data or was closed).
            if (waitToReadTask.IsCompleted)
                waitToReadTask = reader.WaitToReadAsync(cancellationToken).AsTask();

            // Emit a keep-alive comment frame for every elapsed timer tick.
            while (keepAliveTask.IsCompleted)
            {
                yield return new SseItem<string>(string.Empty, eventType: null);
                keepAliveTask = keepAliveTimer.WaitForNextTickAsync(cancellationToken).AsTask();
            }
        }
    }

    /// <summary>
    /// Buffers replayed events from <see cref="IProbahoSseReplayable"/> then yields each as
    /// a <see cref="SseItem{T}"/> (buffering is required because you cannot mix await and
    /// yield inside the same try/catch block).
    /// </summary>
    private static async IAsyncEnumerable<SseItem<string>> ReplayAsync(
        IProbahoSseReplayable replayable,
        string lastEventId,
        ProbahoSseOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<IProbahoSseEvent>();
        await replayable.ReplayFromAsync(
            lastEventId,
            e => { buffer.Add(e); return Task.CompletedTask; },
            cancellationToken);

        foreach (var e in buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToSseItem(e, options);
        }
    }

    /// <summary>
    /// Maps an <see cref="IProbahoSseEvent"/> to a <see cref="SseItem{T}"/> carrying the
    /// data string, the event type name, and the event ID (<c>id:</c> SSE field).
    /// </summary>
    public static SseItem<string> ToSseItem(IProbahoSseEvent sseEvent, ProbahoSseOptions options)
        => new(sseEvent.Data, eventType: sseEvent.EventType ?? options.DefaultEventType)
        {
            EventId = sseEvent.Id
        };
}
