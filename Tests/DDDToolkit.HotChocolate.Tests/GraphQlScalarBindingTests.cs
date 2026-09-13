using DDDToolkit.HotChocolate.Tests.Domain;
using FluentAssertions;
using System.Text.Json;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// What a strongly typed id or single value object looks like on the wire. The generated runtime
/// bindings say which scalar each type maps to; these tests assert the serialized result.
/// </summary>
public class GraphQlScalarBindingTests
{
    [Fact]
    public async Task Struct_id_serializes_as_its_guid()
    {
        var data = await TestSchema.QueryDataAsync("{ cat }");

        data.GetProperty("cat").GetString().Should().Be(TestData.CatGuid.ToString());
    }

    [Fact]
    public async Task Nullable_struct_id_serializes_as_its_guid_when_present()
    {
        var data = await TestSchema.QueryDataAsync("{ nullableCat }");

        data.GetProperty("nullableCat").GetString().Should().Be(TestData.CatGuid.ToString());
    }

    [Fact]
    public async Task Nullable_struct_id_serializes_as_null_when_absent()
    {
        var data = await TestSchema.QueryDataAsync("{ missingCat }");

        data.GetProperty("missingCat").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task List_of_struct_ids_serializes_as_a_list_of_guids()
    {
        var data = await TestSchema.QueryDataAsync("{ cats }");

        data.GetProperty("cats")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Equal(TestData.CatGuid.ToString(), TestData.OtherCatGuid.ToString());
    }

    [Fact]
    public async Task Reference_id_and_its_always_valid_twin_serialize_as_the_same_guid()
    {
        var data = await TestSchema.QueryDataAsync("{ person validPerson }");

        data.GetProperty("person").GetString().Should().Be(TestData.PersonGuid.ToString());
        data.GetProperty("validPerson").GetString().Should().Be(TestData.PersonGuid.ToString());
    }

    [Fact]
    public async Task Single_value_object_uses_the_scalar_named_by_its_GraphQLType_attribute()
    {
        var data = await TestSchema.QueryDataAsync("{ email }");

        data.GetProperty("email").GetString().Should().Be(TestData.HolderEmail);

        var sdl = await TestSchema.PrintSchemaAsync();
        sdl.Should().Contain("email: EmailAddress!");
    }

    [Fact]
    public async Task Prefixed_struct_id_serializes_without_its_prefix()
    {
        var data = await TestSchema.QueryDataAsync("{ prefixedId }");

        // The prefix belongs to ToString()/Parse, not to the wire format.
        TestData.Ticket.ToString().Should().Be("TST_" + TestData.TicketGuid);
        data.GetProperty("prefixedId").GetString().Should().Be(TestData.TicketGuid.ToString());
    }

    [Fact]
    public async Task Int_valued_struct_id_serializes_as_a_number()
    {
        var data = await TestSchema.QueryDataAsync("{ seat }");

        var seat = data.GetProperty("seat");
        seat.ValueKind.Should().Be(JsonValueKind.Number);
        seat.GetInt32().Should().Be(TestData.SeatValue);
    }

    [Fact]
    public async Task Struct_id_honours_the_schema_type_named_by_its_GraphQLType_attribute()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        // A string-valued id binds to String unless the attribute says otherwise.
        sdl.Should().Contain("login: EmailAddress!");

        var data = await TestSchema.QueryDataAsync("{ login }");
        data.GetProperty("login").GetString().Should().Be(TestData.HolderEmail);
    }

    [Fact]
    public async Task Ids_are_bound_to_the_scalar_their_value_type_implies()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        sdl.Should().Contain("cat: UUID!");
        sdl.Should().Contain("nullableCat: UUID");
        sdl.Should().Contain("cats: [UUID!]!");
        sdl.Should().Contain("person: UUID!");
        sdl.Should().Contain("validPerson: UUID!");
        sdl.Should().Contain("prefixedId: UUID!");
        sdl.Should().Contain("seat: Int!");
    }
}
