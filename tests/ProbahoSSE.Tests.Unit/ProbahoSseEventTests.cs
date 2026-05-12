using ProbahoSSE.Models;

namespace ProbahoSSE.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ProbahoSseEvent"/>.
/// </summary>
public sealed class ProbahoSseEventTests
{
    [Fact]
    public void Create_SetsDataField()
    {
        var evt = ProbahoSseEvent.Create("payload");
        Assert.Equal("payload", evt.Data);
    }

    [Fact]
    public void Create_GeneratesIdWhenNotProvided()
    {
        var evt = ProbahoSseEvent.Create("data");
        Assert.NotNull(evt.Id);
        Assert.NotEmpty(evt.Id);
    }

    [Fact]
    public void Create_UsesExplicitId()
    {
        var evt = ProbahoSseEvent.Create("data", id: "my-id");
        Assert.Equal("my-id", evt.Id);
    }

    [Fact]
    public void Create_SetsEventType()
    {
        var evt = ProbahoSseEvent.Create("data", eventType: "price-update");
        Assert.Equal("price-update", evt.EventType);
    }

    [Fact]
    public void Create_EventTypeIsNullByDefault()
    {
        var evt = ProbahoSseEvent.Create("data");
        Assert.Null(evt.EventType);
    }

    [Fact]
    public void TwoEventsWithSameValues_AreEqual()
    {
        var e1 = new ProbahoSseEvent { Data = "x", Id = "1", EventType = "t" };
        var e2 = new ProbahoSseEvent { Data = "x", Id = "1", EventType = "t" };
        Assert.Equal(e1, e2);
    }

    [Fact]
    public void TwoEventsWithDifferentData_AreNotEqual()
    {
        var e1 = ProbahoSseEvent.Create("a", id: "same");
        var e2 = ProbahoSseEvent.Create("b", id: "same");
        Assert.NotEqual(e1, e2);
    }
}

