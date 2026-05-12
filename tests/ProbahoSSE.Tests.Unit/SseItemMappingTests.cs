using System.Net.ServerSentEvents;
using Microsoft.Extensions.Options;
using ProbahoSSE.Core;
using ProbahoSSE.Models;

namespace ProbahoSSE.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SseEndpointHandler.ToSseItem"/>.
/// Tests the mapping from IProbahoSseEvent → SseItem without any HTTP context.
/// </summary>
public sealed class SseItemMappingTests
{
    private static ProbahoSseOptions DefaultOptions() => new()
    {
        DefaultEventType = "message"
    };

    [Fact]
    public void ToSseItem_MapsDataCorrectly()
    {
        var evt = ProbahoSseEvent.Create("hello world");
        var item = SseEndpointHandler.ToSseItem(evt, DefaultOptions());
        Assert.Equal("hello world", item.Data);
    }

    [Fact]
    public void ToSseItem_UsesEventTypeFromEvent()
    {
        var evt = ProbahoSseEvent.Create("data", eventType: "price-update");
        var item = SseEndpointHandler.ToSseItem(evt, DefaultOptions());
        Assert.Equal("price-update", item.EventType);
    }

    [Fact]
    public void ToSseItem_FallsBackToDefaultEventType_WhenEventTypeIsNull()
    {
        var evt = ProbahoSseEvent.Create("data"); // no eventType
        var options = new ProbahoSseOptions { DefaultEventType = "my-default" };
        var item = SseEndpointHandler.ToSseItem(evt, options);
        Assert.Equal("my-default", item.EventType);
    }

    [Fact]
    public void ToSseItem_SetsEventId()
    {
        var evt = ProbahoSseEvent.Create("data", id: "evt-42");
        var item = SseEndpointHandler.ToSseItem(evt, DefaultOptions());
        Assert.Equal("evt-42", item.EventId);
    }

    [Fact]
    public void ToSseItem_NullId_EventIdIsNull()
    {
        var evt = new ProbahoSseEvent { Data = "data", Id = null };
        var item = SseEndpointHandler.ToSseItem(evt, DefaultOptions());
        Assert.Null(item.EventId);
    }
}

