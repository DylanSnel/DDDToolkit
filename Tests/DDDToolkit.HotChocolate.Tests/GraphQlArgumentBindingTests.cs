using DDDToolkit.HotChocolate.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// The other direction: a scalar coming in from a client has to arrive at the resolver as the
/// strongly typed id, whether it was written as a literal or supplied through a variable.
/// </summary>
public class GraphQlArgumentBindingTests
{
    [Fact]
    public async Task Struct_id_argument_round_trips_from_a_literal()
    {
        var data = await TestSchema.QueryDataAsync(
            $$"""{ echoCatId(id: "{{TestData.CatGuid}}") }""");

        data.GetProperty("echoCatId").GetString().Should().Be(TestData.CatGuid.ToString());
    }

    [Fact]
    public async Task Struct_id_argument_round_trips_from_a_variable()
    {
        var data = await TestSchema.QueryDataAsync(
            "query Echo($id: UUID!) { echoCatId(id: $id) }",
            new Dictionary<string, object?> { ["id"] = TestData.CatGuid.ToString() });

        data.GetProperty("echoCatId").GetString().Should().Be(TestData.CatGuid.ToString());
    }

    [Fact]
    public async Task Resolver_receives_the_id_type_and_not_a_string()
    {
        var data = await TestSchema.QueryDataAsync(
            $$"""{ describeTicketId(id: "{{TestData.TicketGuid}}") }""");

        // Only a real TicketId prints the "TST_" prefix.
        data.GetProperty("describeTicketId").GetString().Should().Be("TST_" + TestData.TicketGuid);
    }

    [Fact]
    public async Task Input_object_with_struct_ids_is_read_from_a_literal()
    {
        var data = await TestSchema.QueryDataAsync(
            $$"""{ describeReservation(reservation: { ticket: "{{TestData.TicketGuid}}", seat: 7 }) }""");

        data.GetProperty("describeReservation").GetString().Should().Be($"TST_{TestData.TicketGuid}/7");
    }

    [Fact]
    public async Task Input_object_with_struct_ids_is_read_from_variables()
    {
        var variables = new Dictionary<string, object?>
        {
            ["reservation"] = new Dictionary<string, object?>
            {
                ["ticket"] = TestData.TicketGuid.ToString(),
                ["seat"] = 7,
            },
        };

        var data = await TestSchema.QueryDataAsync(
            "query Describe($reservation: SeatReservationInput!) { describeReservation(reservation: $reservation) }",
            variables);

        data.GetProperty("describeReservation").GetString().Should().Be($"TST_{TestData.TicketGuid}/7");
    }

    [Fact]
    public async Task Input_object_fields_are_bound_to_the_id_scalars()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        sdl.Should().Contain("input SeatReservationInput");
        sdl.Should().Contain("ticket: UUID!");
        sdl.Should().Contain("seat: Int!");
    }
}
