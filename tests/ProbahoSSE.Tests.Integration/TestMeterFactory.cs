using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace ProbahoSSE.Tests.Integration;

/// <summary>
/// Minimal <see cref="IMeterFactory"/> for use in tests — creates real <see cref="Meter"/>
/// instances so instruments can be exercised without requiring a full OTel pipeline.
/// </summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var m in _meters) m.Dispose();
        _meters.Clear();
    }
}

