namespace ProbahoSSE.Abstractions;

/// <summary>
/// Marker interface for a ProbahoSSE backplane implementation.
/// Extends <see cref="IProbahoSsePublisher"/> — the only public capability
/// of a backplane is publishing events. Subscription and fan-out are internal
/// implementation details handled by each backplane's hosted listener service.
/// </summary>
/// <remarks>
/// Prefer injecting <see cref="IProbahoSsePublisher"/> in application code
/// (e.g. Kafka consumer services). Use <see cref="IProbahoSseBackplane"/> only
/// when you need to resolve the concrete backplane registration.
/// </remarks>
public interface IProbahoSseBackplane : IProbahoSsePublisher
{
}
