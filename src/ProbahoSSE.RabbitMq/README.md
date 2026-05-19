# ProbahoSSE.RabbitMq

RabbitMQ fanout backplane for ProbahoSSE — enables fire-and-forget broadcasting across multiple ASP.NET Core server instances via a single fanout exchange. Each instance maintains an exclusive, auto-delete queue. Best suited for scenarios where losing events on reconnect is acceptable.

For full documentation, samples, and source code, visit the GitHub repository:

https://github.com/ehtesam4m/ProbahoSSE
