using System.Text.Json;
using DDDToolkit.HotChocolate.Tests.Library.Api.GraphQl;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Library.Api;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// A class marked <c>[GraphQLSchema(name, operation)]</c> is in the schema of that name and in no other, while every
/// other class of its project is in every schema. Proven on a real project, <c>DDDToolkit.HotChocolate.Tests.Library.Api</c>,
/// compiled by HotChocolate's generator and the toolkit's: three schemas, each built from the same two calls, the
/// toolkit's bindings, <c>AddLibraryGraphQlRuntimeBindings()</c>, and HotChocolate's <c>AddLibraryTypes()</c>, and
/// printed.
/// </summary>
public sealed class OneSchemaPerClassTests
{
    /// <summary>
    /// A schema built from the same two calls under a name no class is marked with: what HotChocolate's registration
    /// of the project gives every schema, with the toolkit's ids.
    /// </summary>
    private const string HotChocolateAlone = "hotchocolate-alone";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_field_of_a_class_marked_for_the_staff_is_absent_from_the_members_schema()
    {
        var members = await PrintAsync(LibrarySchemas.Members);
        var staff = await PrintAsync(LibrarySchemas.Staff);

        staff.Should().Contain("loansOf(member: UUID!): [Loan!]!").And.Contain("overdueCount: Int!");
        members.Should().NotContain("loansOf(").And.NotContain("overdueCount", "the members' schema is offered nothing of the staff's");
    }

    [Fact]
    public async Task HotChocolates_own_registration_of_the_project_has_none_of_the_marked_classes()
    {
        // HotChocolate's generator registers what it finds, in every schema: so it must find nothing of the staff's.
        var alone = await PrintAsync(HotChocolateAlone);

        alone.Should().Contain("loansOfMine: [Loan!]!");
        alone.Should().NotContain("loansOf(").And.NotContain("overdueCount").And.NotContain("loanExtend").And.NotContain("secretCount").And.NotContain("onLoanOverdue");
        alone.Should().Be(await PrintAsync(LibrarySchemas.Members), "a schema whose name no class is marked with gets nothing more than HotChocolate's registration gives it");
    }

    [Fact]
    public async Task An_unmarked_class_is_in_every_schema()
    {
        foreach (var name in new[] { LibrarySchemas.Members, LibrarySchemas.Staff, LibrarySchemas.HelpDesk })
        {
            var schema = await PrintAsync(name);
            schema.Should().Contain("loansOfMine: [Loan!]!", "the members' own field has no mark, so {0} has it", name);
            schema.Should().Contain("type Loan {", "and so has the type class, which HotChocolate's generator registers");
            schema.Should().Contain("overdue: Boolean!");
        }
    }

    [Fact]
    public async Task A_class_marked_for_two_schemas_is_in_both_and_in_no_other()
    {
        (await PrintAsync(LibrarySchemas.HelpDesk)).Should().Contain("overdueCount: Int!").And.NotContain("loansOf(", "the help desk shares one class with the staff, and only that one");
        (await PrintAsync(LibrarySchemas.Staff)).Should().Contain("overdueCount: Int!");
        (await PrintAsync(LibrarySchemas.Members)).Should().NotContain("overdueCount");
    }

    [Fact]
    public async Task A_schema_whose_only_mutations_are_another_schemas_has_no_mutation_type()
    {
        (await ExecutorAsync(LibrarySchemas.Staff)).Schema.MutationType.Should().NotBeNull();
        (await PrintAsync(LibrarySchemas.Staff)).Should().Contain("loanExtend(member: UUID!, title: String!, days: Int!): Loan!");

        (await ExecutorAsync(LibrarySchemas.Members)).Schema.MutationType.Should().BeNull("every mutation of the project is the staff's");
        (await ExecutorAsync(LibrarySchemas.HelpDesk)).Schema.MutationType.Should().BeNull();
    }

    [Fact]
    public async Task A_subscription_of_one_schema_is_in_that_schema_alone_and_the_stream_it_names_is_no_field()
    {
        var staff = await PrintAsync(LibrarySchemas.Staff);
        staff.Should().Contain("onLoanOverdue: Loan!").And.NotContain("subscribeToOverdueLoans", "the stream a field subscribes to is no field of its own");

        (await ExecutorAsync(LibrarySchemas.Members)).Schema.SubscriptionType.Should().BeNull("every subscription of the project is the staff's");
        (await PrintAsync(LibrarySchemas.HelpDesk)).Should().NotContain("onLoanOverdue");
    }

    [Fact]
    public async Task A_subscription_of_one_schema_answers_from_the_stream_it_names()
    {
        var executor = await ExecutorAsync(LibrarySchemas.Staff);

        await using var result = await executor.ExecuteAsync("subscription { onLoanOverdue { member title overdue } }", Cancellation);
        var stream = result.Should().BeAssignableTo<IResponseStream>("a subscription answers with a stream").Subject;

        var titles = new List<string?>();
        await foreach (var answer in stream.ReadResultsAsync().WithCancellation(Cancellation))
        {
            var loan = Data(answer).GetProperty("onLoanOverdue");
            loan.GetProperty("member").GetString().Should().Be(LibraryDesk.Ben.Value.ToString());
            loan.GetProperty("overdue").GetBoolean().Should().BeTrue();
            titles.Add(loan.GetProperty("title").GetString());
        }

        titles.Should().Equal("Bleak House");
    }

    [Fact]
    public async Task A_method_marked_GraphQLIgnore_is_no_field()
        => (await PrintAsync(LibrarySchemas.Staff)).Should().NotContain("secretCount");

    [Fact]
    public async Task A_field_of_one_schema_runs_with_its_arguments_its_ids_and_its_services()
    {
        var executor = await ExecutorAsync(LibrarySchemas.Staff);

        var loans = Data(await executor.ExecuteAsync(
            $$"""{ loansOf(member: "{{LibraryDesk.Ben.Value}}") { member title overdue } }""",
            Cancellation)).GetProperty("loansOf").EnumerateArray().ToList();

        loans.Select(loan => loan.GetProperty("title").GetString()).Should().Equal("Bleak House", "Persuasion");
        loans.Select(loan => loan.GetProperty("member").GetString()).Should().AllBe(LibraryDesk.Ben.Value.ToString());
        loans.Select(loan => loan.GetProperty("overdue").GetBoolean()).Should().Equal(true, false);

        var extended = Data(await executor.ExecuteAsync(
            $$"""mutation { loanExtend(member: "{{LibraryDesk.Ben.Value}}", title: "Bleak House", days: 7) { dueInDays overdue } }""",
            Cancellation)).GetProperty("loanExtend");

        extended.GetProperty("dueInDays").GetInt32().Should().Be(4);
        extended.GetProperty("overdue").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_document_that_asks_the_members_schema_for_a_staff_field_is_refused_before_anything_runs()
    {
        var answer = Json(await (await ExecutorAsync(LibrarySchemas.Members)).ExecuteAsync(
            $$"""{ loansOf(member: "{{LibraryDesk.Ben.Value}}") { title } }""",
            Cancellation));

        answer.TryGetProperty("data", out _).Should().BeFalse("a document that names a field the schema has not is not run at all");
        answer.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("loansOf");
    }

    /// <summary>
    /// The library's schemas, each registered the way a host registers them: the toolkit's conventions, the toolkit's
    /// bindings, which register the project's marked classes for the schema's name, and HotChocolate's registration.
    /// And one more of the same, under a name no class is marked with.
    /// </summary>
    private static async Task<IRequestExecutor> ExecutorAsync(string name)
    {
        var services = new ServiceCollection().AddSingleton<LibraryDesk>();

        foreach (var schema in new[] { LibrarySchemas.Members, LibrarySchemas.Staff, LibrarySchemas.HelpDesk, HotChocolateAlone })
        {
            services.AddGraphQLServer(schema).AddDDDToolkitTypes().AddLibraryGraphQlRuntimeBindings().AddLibraryTypes();
        }

        var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync(name, Cancellation);
    }

    private static async Task<string> PrintAsync(string name) => (await ExecutorAsync(name)).Schema.ToString();

    private static JsonElement Data(IExecutionResult result)
    {
        var json = Json(result);
        json.TryGetProperty("errors", out var errors).Should().BeFalse("the request answered {0}", errors.ToString());
        return json.GetProperty("data");
    }

    private static JsonElement Json(IExecutionResult result)
    {
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }
}
