# Changelog

All notable changes to **ProbahoSSE** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

