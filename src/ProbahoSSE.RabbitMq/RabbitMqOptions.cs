namespace ProbahoSSE.RabbitMq;

/// <summary>
/// Configuration options for the RabbitMQ fanout backplane.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Gets or sets the RabbitMQ host name.
    /// Default is "localhost".
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the RabbitMQ AMQP port.
    /// Default is 5672.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the RabbitMQ username.
    /// Default is "guest".
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the RabbitMQ password.
    /// Default is "guest".
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the RabbitMQ virtual host.
    /// Default is "/".
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the fanout exchange name all instances publish and subscribe to.
    /// Default is "probaho".
    /// </summary>
    public string ExchangeName { get; set; } = "probaho";
}

