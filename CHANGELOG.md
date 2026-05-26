# Changelog

All notable changes to **ProbahoSSE** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-05-26

First stable release of **ProbahoSSE**. All public APIs are considered stable and will follow semantic versioning from this point forward.

### Added
- **Core library — `ProbahoSSE`**
  - `IProbahoSsePublisher` / `IProbahoSseManager` — primary public API for publishing events and managing connections
  - `SseConnectionManager` — thread-safe in-memory connection registry using `Channel<IProbahoSseEvent>` per connection for async pub/sub delivery, backed by two `ConcurrentDictionary` maps for O(group size) group-targeted dispatch
  - `SseEndpointHandler` — drives `TypedResults.ServerSentEvents` via `IAsyncEnumerable<SseItem<string>>`; keep-alive frames sent on a separate timer via `Task.WhenAny` so quiet periods don't drop proxied connections
  - `IProbahoSseBackplane` / `IProbahoSseReplayable` — pluggable backplane contracts; implement either to connect any message broker
  - Global and per-group connection limits with `429` responses on breach
  - `event: connected` frame sent immediately on stream open
  - Fluent `AddProbahoSse(...)` registration with `IProbahoSseBuilder` for chained backplane setup

- **`ProbahoSSE.RedisPubSub`** — fire-and-forget backplane using Redis Pub/Sub (`StackExchange.Redis`); uses `ChannelMessageQueue` / `queue.OnMessage` for automatic reconnect handling without app-level resurrection logic
- **`ProbahoSSE.RedisStream`** — persistent backplane using Redis Streams with `Last-Event-ID` replay; each instance independently polls via `XREAD` — no consumer groups, no stale state on autoscaling
- **`ProbahoSSE.RabbitMq`** — fire-and-forget backplane using a single RabbitMQ fanout exchange; each instance gets an exclusive, server-named, auto-delete queue that is cleaned up automatically on disconnect
- **`ProbahoSSE.Backplane`** — shared serialisation and abstractions consumed by all three backplane packages
- **Full client configuration exposure** — `ConfigureOptions` (`Action<ConfigurationOptions>`) on both Redis backplane options; `ConfigureFactory` (`Action<ConnectionFactory>`) on `RabbitMqOptions` — full control over the underlying client without losing the convenience API
- **OpenTelemetry distributed tracing** — `ActivitySource` (`"ProbahoSSE"`) emits `sse.connection`, `sse.broadcast`, `sse.send_to_group`, and `sse.backplane.receive` spans. `TraceParent` field in the serialised event propagates W3C trace context across the backplane boundary so the full publish→deliver chain appears as one distributed trace
- **Metrics via `IMeterFactory`** — meter `"ProbahoSSE"` with five instruments: `probahosse.connections.active` (ObservableGauge), `sse.connections.rejected` (Counter), `probahosse.backplane.messages_sent` (Counter), `probahosse.backplane.messages_failed` (Counter), `sse.backplane.publish.duration` (Histogram). Compatible with OpenTelemetry Metrics and `dotnet counters`
- **Backplane health check** — `AddProbahoSseHealthCheck()` registers a named `IHealthCheck` (`"probahosse-backplane"`) reporting `Unhealthy` on publish failure, self-clearing on next successful publish
- **OTel constants** — `ProbahoSseTelemetry.Activities` and `ProbahoSseTelemetry.Tags` expose all span names and tag key strings as typed constants; `ProbahoSseMetrics.MeterName` and `ProbahoSseTelemetry.SourceName` for OTel registration
- **Samples** — three fully self-contained Docker Compose samples (`Sample.RedisPubSub`, `Sample.RedisStream`, `Sample.RabbitMq`) each with two API instances, nginx load balancer, `Common.IoTSensorSimulator`, and a dark-themed browser UI at `http://localhost:8080` that auto-detects the active backplane and highlights replayed events

### Notes
This release graduates the library from pre-release (`0.x`) to stable (`1.0.0`). No breaking changes from `0.6.0`. All public interfaces, extension methods, and option types are source-compatible. The `0.x` releases are considered deprecated.

---

## [0.6.0] - 2026-05-26

### Added
- **OpenTelemetry distributed tracing** — `ActivitySource` (`"ProbahoSSE"`) emits four spans: `sse.connection` (per HTTP connection), `sse.broadcast`, `sse.send_to_group`, and `sse.backplane.receive` (all three backplane listeners). Trace context is propagated across the backplane boundary via a `TraceParent` field embedded in every serialised event, so inbound HTTP traces are automatically correlated with backplane fan-out.
- **Metrics via `IMeterFactory`** — `ProbahoSseMetrics` exposes five instruments under the meter `"ProbahoSSE"`: `sse.connections.active` (ObservableGauge), `sse.connections.rejected` (Counter), `sse.events.published` (Counter), `sse.backplane.publish.duration` (Histogram), and `sse.backplane.reconnects` counter. Instruments are wired into `SseEndpointHandler` and all three backplane `PublishAsync` methods.
- **Backplane health check** — `AddProbahoSseHealthCheck()` registers an `IHealthCheck` implementation (`BackplaneHealthCheck`) that reports `Unhealthy` when the last publish attempt failed, and `Healthy` otherwise. Integrates with the standard ASP.NET Core health-check middleware.
- **OTel tag/activity-name constants** — `ProbahoSseTelemetry.Activities` and `ProbahoSseTelemetry.Tags` static classes centralise all `"sse.*"` string literals used in `SetTag` / `StartActivity` calls, eliminating duplication across `SseConnectionManager`, `SseEndpointHandler`, and the three backplane listener services.
- **Observability section in README** — documents distributed tracing setup (`AddSource("ProbahoSSE")`), full metrics instrument table, health check registration, and a reference of all tag/activity-name constants.

### Changed
- `SseConnectionManager` — replaced flat `ConcurrentDictionary` linear scan with a secondary group index (`ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>`), making `SendToGroupAsync` O(group size) instead of O(total connections). Removed a separate `_groupConnectionCounts` dictionary; group size is now derived from `set.Count` which is O(1).
- `RedisPubSubListenerService` — migrated from `SubscribeAsync` callback pattern to `ChannelMessageQueue` / `queue.OnMessage(...)`. StackExchange.Redis automatically re-attaches the queue to the channel after a reconnect, removing the need for app-level resurrection logic and a dedicated `ConnectionRestored` event handler.
- `RedisStreamListenerService` — startup `StreamInfoAsync` now uses a targeted `catch (RedisServerException ex) when (ex.Message.Contains("ERR no such key", ...))` instead of a bare `catch`, so `RedisConnectionException` and `RedisTimeoutException` propagate to the `BackgroundService` host for proper restart backoff.

### Fixed
- Missing structured log entries added throughout `SseConnectionManager`, `SseEndpointHandler`, and all three backplane listener services — library failures are no longer silent from the operator's perspective.

### Notes
No breaking changes to public APIs. All existing backplane registrations and `AddProbahoSse` calls are source-compatible. The metrics and health check features are opt-in via `IMeterFactory` / `AddProbahoSseHealthCheck()`.

---

## [0.5.0] - 2026-05-22

### Notes
Published during branch renaming. No functional changes.

## [0.4.0] - 2026-05-22

### Added
- `ConfigureOptions` callback on `RedisPubSubOptions` and `RedisStreamOptions` — exposes the full StackExchange.Redis `ConfigurationOptions` object (SSL, timeouts, retry policy, reconnect behaviour, etc.)
- `ConfigureFactory` callback on `RabbitMqOptions` — exposes the full `RabbitMQ.Client` `ConnectionFactory` object (heartbeat, automatic recovery, SSL, network recovery interval, etc.)

### Notes
All three backplane packages now give users full control over the underlying client configuration without losing the convenience of the simple fluent API. The callbacks are optional and applied after the base properties are set, so existing configurations require no changes. No breaking changes.

---

## [0.3.0] - 2026-05-19

### Added
- **ProbahoSSE.RabbitMq** — new backplane package using a single RabbitMQ fanout exchange; each API instance gets an exclusive, auto-delete queue bound to the exchange on startup
- `RabbitMqOptions` — configurable `HostName`, `Port`, `UserName`, `Password`, `VirtualHost`, and `ExchangeName`
- `AddRabbitMqBackplane(...)` extension method on `IProbahoSseBuilder`, consistent with existing Redis backplane registration pattern
- **Sample.RabbitMq** — new Docker Compose sample demonstrating two API instances behind nginx backed by RabbitMQ, including the RabbitMQ Management UI on port `15672`
- Demo UI (`index.html`) updated to dynamically detect and display the active backplane name and icon — no longer hard-coded to Redis

### Notes
This release adds RabbitMQ as a first-class fire-and-forget backplane option alongside the existing Redis implementations. The architecture uses a single fanout exchange so all instances receive every message and filter locally — identical behaviour to `ProbahoSSE.RedisPubSub` but backed by RabbitMQ. No breaking changes to existing packages or public APIs.

---

## [0.2.0] - 2026-05-14

### Changed
- Redis Stream backplane reworked to use independent per-instance polling via `XREAD` instead of consumer groups — eliminates stale state on autoscaling and simplifies deployment

### Notes
This release focuses on the Redis Stream backplane reliability. Each API instance now independently tracks its own read position, making horizontal scaling seamless with no shared consumer group coordination required.

---

## [0.1.2] - 2026-05-13

### Fixed
- Corrected and improved per-package README files published to NuGet

### Notes
Housekeeping release — no functional changes. Fixes the README content displayed on the NuGet package pages for `ProbahoSSE`, `ProbahoSSE.RedisPubSub`, and `ProbahoSSE.RedisStream`.

---

## [0.1.1] - 2026-05-13

### Added
- Extracted shared `ProbahoSSE.Backplane` project containing `SseEventSerializer` and shared backplane abstractions, consumed by both Redis backplane packages

### Notes
Internal refactor to reduce duplication between the two Redis backplane packages. No breaking changes to public APIs.

---

## [0.1.0] - 2026-05-12

### Added
- Initial public developer release of **ProbahoSSE** core library
- **ProbahoSSE.RedisPubSub** — fire-and-forget pub/sub backplane using Redis Pub/Sub
- **ProbahoSSE.RedisStream** — persistent stream backplane with `Last-Event-ID` replay using Redis Streams
- NuGet publishing via GitHub Actions

### Notes
This version is deprecated due to reference issues in the initial release.

---

