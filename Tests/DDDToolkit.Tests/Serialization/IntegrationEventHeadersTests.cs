using DDDToolkit.BaseTypes;
using FluentAssertions;

namespace DDDToolkit.Tests.Serialization;

/// <summary>
/// The envelope on the wire. Every transport writes these headers and every consumer reads them back,
/// so a message has to survive the trip whole: the same id, name, version and time on the far side.
/// </summary>
public class IntegrationEventHeadersTests
{
    private static readonly IntegrationEventMessage Sent = new()
    {
        MessageId = Guid.CreateVersion7(),
        Name = "ordering.order-placed",
        Version = 2,
        Payload = """{"orderId":"ORD_1"}""",
        ContentType = "application/json",
        OccurredAt = new DateTimeOffset(2026, 9, 23, 12, 30, 15, TimeSpan.FromHours(2)),
        AggregateType = "Order",
        AggregateId = "ORD_1",
        Body = new object(),
    };

    [Fact]
    public void A_message_survives_the_trip_through_its_headers()
    {
        var received = IntegrationEventHeaders.ToMessage(IntegrationEventHeaders.From(Sent), Sent.Payload);

        // Body stays behind: the far side reads the contract from the payload.
        received.Should().Be(Sent with { Body = null });
        received.OccurredAt.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [InlineData(IntegrationEventHeaders.MessageId)]
    [InlineData(IntegrationEventHeaders.Name)]
    [InlineData(IntegrationEventHeaders.OccurredAt)]
    public void A_message_missing_what_it_cannot_be_rebuilt_without_is_refused(string header)
    {
        var headers = new Dictionary<string, string?>(IntegrationEventHeaders.From(Sent));
        headers.Remove(header);

        var rebuild = () => IntegrationEventHeaders.ToMessage(headers, Sent.Payload);

        rebuild.Should().Throw<FormatException>().WithMessage($"*{header}*");
    }

    [Fact]
    public void A_missing_version_and_content_type_fall_back_to_the_defaults()
    {
        var headers = new Dictionary<string, string?>(IntegrationEventHeaders.From(Sent));
        headers.Remove(IntegrationEventHeaders.Version);
        headers.Remove(IntegrationEventHeaders.ContentType);

        var received = IntegrationEventHeaders.ToMessage(headers, Sent.Payload);

        received.Version.Should().Be(1);
        received.ContentType.Should().Be("application/json");
    }
}
