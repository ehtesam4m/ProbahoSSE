using Microsoft.Extensions.DependencyInjection;

namespace ProbahoSSE.Extensions;

/// <summary>
/// A fluent builder returned by <see cref="ProbahoSseServiceCollectionExtensions.AddProbahoSse"/>
/// for chaining backplane and other registrations.
/// </summary>
public sealed class ProbahoSseBuilder
{
    /// <summary>Gets the underlying service collection.</summary>
    public IServiceCollection Services { get; }

    internal ProbahoSseBuilder(IServiceCollection services)
    {
        Services = services;
    }
}

