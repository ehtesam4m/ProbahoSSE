using Microsoft.Extensions.Options;
using ProbahoSSE.Core;
using ProbahoSSE.Models;

namespace ProbahoSSE.Tests.Unit.Stubs;

/// <summary>
/// Builder for <see cref="SseConnectionManager"/> — lets each test configure limits cleanly.
/// </summary>
internal sealed class ConnectionManagerBuilder
{
    private int _maxGlobal;
    private int _maxPerUser;

    public ConnectionManagerBuilder WithMaxGlobal(int max) { _maxGlobal = max; return this; }
    public ConnectionManagerBuilder WithMaxPerUser(int max) { _maxPerUser = max; return this; }

    public SseConnectionManager Build()
    {
        var options = Options.Create(new ProbahoSseOptions
        {
            MaxGlobalConnections = _maxGlobal,
            MaxConnectionsPerUser = _maxPerUser
        });
        return new SseConnectionManager(options);
    }
}

