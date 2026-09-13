using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>IgnoreInternalFieldsInterceptor</c> keeps the toolkit's plumbing out of the schema. What is
/// plumbing and what is API is decided by <c>[Internal]</c>, not by a name list here.
/// </summary>
public class InternalMembersAreHiddenTests
{
    [Fact]
    public async Task Value_object_validation_members_are_not_in_the_schema()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        var personName = TypeBlock(sdl, "type PersonName");

        personName.Should().NotContain("isValid");
        personName.Should().NotContain("isValidated");
        personName.Should().NotContain("errors");
        personName.Should().NotContain("toValid");
        personName.Should().NotContain("ensureValidated");
    }

    [Fact]
    public async Task Value_object_data_members_are_in_the_schema()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        var personName = TypeBlock(sdl, "type PersonName");

        personName.Should().Contain("firstName");
        personName.Should().Contain("lastName");
        personName.Should().Contain("fullName");
    }

    [Fact]
    public async Task Aggregate_root_shows_id_and_version_but_not_its_pending_events()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        var ticket = TypeBlock(sdl, "type Ticket");

        ticket.Should().Contain("id: UUID!");
        ticket.Should().Contain("version: Long!");
        ticket.Should().Contain("holder: EmailAddress!");
        ticket.Should().Contain("seat: Int!");
        ticket.Should().NotContain("domainEvents");
    }

    [Fact]
    public async Task Querying_an_internal_member_is_rejected()
    {
        var response = await TestSchema.QueryRawAsync("{ issuedTicket { domainEvents { eventId } } }");

        response.TryGetProperty("errors", out var errors).Should().BeTrue(
            "the field does not exist, so the query must not validate");
        errors.ToString().Should().Contain("domainEvents");
    }

    [Fact]
    public async Task Types_reachable_only_through_an_internal_member_are_not_in_the_schema()
    {
        var sdl = await TestSchema.PrintSchemaAsync();

        // Errors is [Internal]; without it FluentValidation's result types would be public schema.
        sdl.Should().NotContain("ValidationFailure");
        sdl.Should().NotContain("Severity");

        // ToValid() is [Internal] too, so the always-valid twin is not a schema type.
        sdl.Should().NotContain("ValidPersonName");
    }

    [Fact]
    public async Task Without_the_toolkit_conventions_those_members_would_be_published()
    {
        // The same value object, in a schema that does not call AddDDDToolkitTypes.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddQueryType<UnmanagedQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        var sdl = executor.Schema.ToString();

        sdl.Should().Contain("isValid");
        sdl.Should().Contain("isValidated");
        sdl.Should().Contain("errors");
    }

    /// <summary>A query root used without the toolkit's conventions, to show what they remove.</summary>
    public sealed class UnmanagedQuery
    {
        /// <summary>A value object, warts and all.</summary>
        public PersonName Name() => new("Ada", "Lovelace");
    }

    /// <summary>The body of one SDL type declaration, so an assertion cannot match a different type.</summary>
    private static string TypeBlock(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }
}
