using DDDToolkit.HotChocolate.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitTypes</c> registers one type: the <c>DomainEvent</c> interface. It carries the
/// members every event has, so a client can read them without knowing the concrete event.
/// </summary>
public class DomainEventInterfaceTypeTests
{
    [Fact]
    public async Task Interface_is_printed_with_the_members_of_IDomainEvent()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        sdl.Should().Contain("interface DomainEvent");

        var start = sdl.IndexOf("interface DomainEvent ", StringComparison.Ordinal);
        var block = sdl[sdl.IndexOf('{', start)..sdl.IndexOf('}', start)];

        block.Should().Contain("eventId: UUID!");
        block.Should().Contain("occurredAt: DateTime!");
        block.Should().Contain("eventType: String!");
    }

    [Fact]
    public async Task Concrete_event_implements_the_interface()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        sdl.Should().Contain("type TicketIssued implements DomainEvent");
    }

    [Fact]
    public async Task Event_members_resolve_against_a_concrete_event()
    {
        var data = await TestSchema.QueryDataAsync("{ latestEvent { eventId occurredAt eventType } }");

        var latest = data.GetProperty("latestEvent");
        latest.GetProperty("eventId").GetString().Should().Be(TestData.EventGuid.ToString());
        latest.GetProperty("eventType").GetString().Should().Be(nameof(TicketIssued));
        latest.GetProperty("occurredAt").GetString().Should().StartWith("2026-09-13T12:00:00");
    }

    [Fact]
    public async Task Event_can_be_queried_through_the_interface()
    {
        var data = await TestSchema.QueryDataAsync(
            "{ latestEvent { ... on DomainEvent { eventType } } }");

        data.GetProperty("latestEvent").GetProperty("eventType").GetString()
            .Should().Be(nameof(TicketIssued));
    }
}
