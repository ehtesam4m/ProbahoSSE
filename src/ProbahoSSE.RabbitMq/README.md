# ProbahoSSE.RabbitMq

> **RabbitMQ fanout backplane for ProbahoSSE.**

[![NuGet](https://img.shields.io/nuget/v/ProbahoSSE.RabbitMq?logo=nuget)](https://www.nuget.org/packages/ProbahoSSE.RabbitMq)
[![Downloads](https://img.shields.io/nuget/dt/ProbahoSSE.RabbitMq?logo=nuget&label=downloads)](https://www.nuget.org/packages/ProbahoSSE.RabbitMq)

Implements `IProbahoSseBackplane` using a single **RabbitMQ fanout exchange**. Every API instance publishes to the same exchange; every instance receives every message and filters locally by group — identical behaviour to `ProbahoSSE.RedisPubSub` but backed by RabbitMQ.

## Architecture

```
Publisher → RabbitMQ Fanout Exchange ("probaho")
                  ↓                         ↓
    [Queue: instance-A]         [Queue: instance-B]
    exclusive · auto-delete     exclusive · auto-delete
                  ↓                         ↓
         API Instance A              API Instance B
         local SSE clients           local SSE clients
```

- One **single fanout exchange** for the entire application
- Each instance gets an **exclusive, server-named, auto-delete queue** — cleaned up automatically on disconnect
- **Fire-and-forget** — no message persistence; offline consumers miss events permanently
- `PublishToAllAsync` stamps events with `ProbahoSseGroups.Broadcast`; the listener broadcasts to all local connections

## Getting Started

```bash
dotnet add package ProbahoSSE
dotnet add package ProbahoSSE.RabbitMq
```

```csharp
// Program.cs
builder.Services
    .AddProbahoSse(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    })
    .AddRabbitMqBackplane(rabbit =>
    {
        rabbit.HostName    = builder.Configuration["RabbitMq:HostName"] ?? "localhost";
        rabbit.Port        = 5672;
        rabbit.UserName    = builder.Configuration["RabbitMq:UserName"] ?? "guest";
        rabbit.Password    = builder.Configuration["RabbitMq:Password"] ?? "guest";
        rabbit.ExchangeName = "my-app";
    });
```

## Configuration Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `HostName` | `string` | `"localhost"` | RabbitMQ host name. |
| `Port` | `int` | `5672` | AMQP port. |
| `UserName` | `string` | `"guest"` | RabbitMQ username. |
| `Password` | `string` | `"guest"` | RabbitMQ password. |
| `VirtualHost` | `string` | `"/"` | RabbitMQ virtual host. |
| `ExchangeName` | `string` | `"probaho"` | Fanout exchange name. Shared across all instances. |

## Best For

- Notification feeds
- Live dashboards
- Chat / presence
- Any workload where missed events during downtime are acceptable

## License

MIT — see [LICENSE](https://github.com/ehtesam4m/ProbahoSSE/blob/main/LICENSE).

