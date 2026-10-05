using System.Globalization;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Errors;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using DDDToolkit.HotChocolate.Tests.InternalTypes;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Configuration;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitMutationConventions()</c>: every mutation gets an input object and a payload, and what its
/// use case throws is a typed error in the payload's <c>errors</c>, with a code, a message and arguments.
/// </summary>
public class MutationConventionTests
{
    /// <summary>What a client asks of any error in a payload, whichever it is.</summary>
    private const string Errors =
        """
        errors {
          __typename
          ... on CodedError { code message arguments { name value } }
          ... on RefusalError { kind field }
          ... on InvalidValuesError { failures { code message field arguments { name value } } }
          ... on BrokenRulesError { violations { code message entity entityId arguments { name value } } }
        }
        """;

    /// <summary>The types the conventions put in a schema, by their names there.</summary>
    private static readonly string[] ToolkitTypes =
    [
        "CodedError", "RefusalError", "InvalidValuesError", "ValueFailure", "BrokenRulesError", "RuleViolation",
        "ConcurrencyConflictError", "FailureArgument", "RefusalKind",
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_smallest_schema_with_the_conventions_builds()
    {
        // One mutation and the conventions: the smallest schema that needs every public piece of HotChocolate
        // the conventions are made of, on the HotChocolate this run has.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitMutationConventions()
            .AddQueryType<Failing>()
            .AddMutationType<Parcels>()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

        Record($"HotChocolate {typeof(IRequestExecutor).Assembly.GetName().Version}: the schema with the conventions builds.");

        var payload = await PayloadAsync(executor, "parcelRelabel", """{ id: 4, label: "" }""", "parcel { id }");

        payload.GetProperty("parcel").ValueKind.Should().Be(JsonValueKind.Null);
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(Parcels.LabelRequired);
    }

    [Fact]
    public async Task A_refusal_thrown_by_a_middleware_a_type_interceptor_adds_lands_where_it_lands()
    {
        // A gate in front of a mutation is a field middleware a type interceptor adds, and where its refusal
        // arrives depends on whether the gate is inside or outside the middleware the conventions wrap the field
        // in. Both places an interceptor can add one are pinned here, so a gate is written for what it gets.

        // Added while the mutation's fields are handed to the interceptors, before the conventions wrap them:
        // the conventions' middleware is outside the gate, and the refusal is a typed error in the payload.
        var before = await ExecuteAsync(
            await BuildAsync(graphql => graphql.TryAddTypeInterceptor<GateOnEachMutationField>()),
            Mutation("parcelRelabel", """{ id: 4, label: "Books" }""", "parcel { id }"));

        before.TryGetProperty("errors", out _).Should().BeFalse("the gate is inside the conventions, got {0}", before);
        var typed = before.GetProperty("data").GetProperty("parcelRelabel").GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        typed.GetProperty("__typename").GetString().Should().Be("RefusalError");
        typed.GetProperty("code").GetString().Should().Be(Gate.Code);
        Record("A middleware added in OnBeforeCompleteMutationField: the refusal is a RefusalError in the payload.");

        // Put first on the completed mutation type's fields, after the conventions wrapped them: the gate is
        // outside, nothing catches its refusal, and it is a top-level coded error with no data.
        var after = await ExecuteAsync(
            await BuildAsync(graphql => graphql.TryAddTypeInterceptor<GateOnTheCompletedMutationType>()),
            Mutation("parcelRelabel", """{ id: 4, label: "Books" }""", "parcel { id }"));

        after.TryGetProperty("errors", out var topLevel).Should().BeTrue("the gate is outside the conventions, got {0}", after);
        var top = topLevel.EnumerateArray().Should().ContainSingle().Which;
        top.TryGetProperty("extensions", out _).Should().BeTrue("got {0}", after);
        top.GetProperty("extensions").GetProperty("code").GetString().Should().Be(Gate.Code);
        top.GetProperty("extensions").GetProperty("kind").GetString().Should().Be(nameof(RefusalKind.NotPermitted));
        top.GetProperty("path")[0].GetString().Should().Be("parcelRelabel");
        Record("A middleware inserted first in OnBeforeCompleteType of the mutation type: the refusal is a top-level coded error.");
    }

    [Fact]
    public async Task A_refusal_is_a_refusal_error_in_the_payload_with_code_kind_field_and_arguments()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelRelabel", """{ id: 4, label: " " }""", "parcel { id label }");

        payload.GetProperty("parcel").ValueKind.Should().Be(JsonValueKind.Null, "a refused command answers no parcel");
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(Parcels.LabelRequired);
        error.GetProperty("kind").GetString().Should().Be("INVALID");
        error.GetProperty("field").GetString().Should().Be("label", "the refusal names its input with the Field argument");
        error.GetProperty("message").GetString().Should().Be("A parcel needs a label of at most 40 characters.");

        // By name, each value as text: a number as JSON writes it whatever the culture, a boolean in lower
        // case, and a value that was null still null.
        Arguments(error).Should().Equal(
            ("Depot", null),
            ("Field", "label"),
            ("Fragile", "true"),
            ("MaxLength", "40"),
            ("Weight", "1.5"));
    }

    [Fact]
    public async Task A_command_that_goes_through_answers_its_result_and_no_errors()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelRelabel", """{ id: 4, label: "Books" }""", "parcel { id label }");

        payload.GetProperty("parcel").GetProperty("label").GetString().Should().Be("Books");
        payload.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invalid_values_are_one_error_listing_every_failure()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelAddress", """{ id: 4, street: "x" }""", "parcel { id }");

        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("InvalidValuesError");
        error.GetProperty("code").GetString().Should().Be(InvalidValuesError.InvalidValue).And.Be("invalid-value");
        error.GetProperty("message").GetString().Should().StartWith("The value object Parcel is invalid.");
        Arguments(error).Should().BeEmpty("each failure has its own");

        var failures = error.GetProperty("failures").EnumerateArray().ToArray();
        failures.Should().HaveCount(2);
        failures[0].GetProperty("code").GetString().Should().Be("Street.TooLong");
        failures[0].GetProperty("field").GetString().Should().Be("Street");
        failures[0].GetProperty("message").GetString().Should().Be("A street is at most 40 characters.");
        Arguments(failures[0]).Should().Equal(("MaxLength", "40"));
        failures[1].GetProperty("code").GetString().Should().Be("City.Required");
        failures[1].GetProperty("field").GetString().Should().Be("City");
        Arguments(failures[1]).Should().BeEmpty();

        payload.ToString().Should().NotContain(Parcels.RejectedStreet, "the rejected value is never sent back");
    }

    [Fact]
    public async Task An_invalid_value_without_detail_still_lists_one_failure_with_a_code()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelReaddress", "{ id: 4 }", "parcel { id }");

        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("code").GetString().Should().Be("invalid-value");
        var failure = error.GetProperty("failures").EnumerateArray().Should().ContainSingle().Which;
        failure.GetProperty("code").GetString().Should().Be(ValidationError.UnspecifiedCode);
        failure.GetProperty("field").ValueKind.Should().Be(JsonValueKind.Null);
        Arguments(failure).Should().Equal((ValidationError.ValueObjectArgument, nameof(Parcel)));
    }

    [Fact]
    public async Task Broken_rules_are_one_error_listing_every_violation()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelSeal", "{ id: 4 }", "parcel { id }");

        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("BrokenRulesError");
        error.GetProperty("code").GetString().Should().Be("Parcel.OverWeight", "the error reads as its first violation");
        error.GetProperty("message").GetString().Should().Be("A parcel weighs at most 30 kilograms.");
        Arguments(error).Should().Equal(("MaxWeight", "30"));

        var violations = error.GetProperty("violations").EnumerateArray().ToArray();
        violations.Should().HaveCount(2);
        violations[0].GetProperty("code").GetString().Should().Be("Parcel.OverWeight");
        violations[0].GetProperty("entity").GetString().Should().Be(nameof(Parcel));
        violations[0].GetProperty("entityId").GetString().Should().Be("4");
        Arguments(violations[0]).Should().Equal(("MaxWeight", "30"));
        violations[1].GetProperty("code").GetString().Should().Be("Stamp.Missing");
        violations[1].GetProperty("message").GetString().Should().Be("A sealed parcel carries a stamp.");
        violations[1].GetProperty("entity").GetString().Should().Be(nameof(Stamp), "a violation in a child says which child");
        violations[1].GetProperty("entityId").GetString().Should().Be("STM_7");
    }

    [Fact]
    public async Task A_broken_rule_written_as_a_sentence_still_lists_one_violation_with_a_code()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelReseal", "{ id: 4 }", "parcel { id }");

        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("code").GetString().Should().Be(InvariantViolation.SeamCode);
        error.GetProperty("message").GetString().Should().Be("A parcel is sealed once.");
        var violation = error.GetProperty("violations").EnumerateArray().Should().ContainSingle().Which;
        violation.GetProperty("entity").ValueKind.Should().Be(JsonValueKind.Null);
        violation.GetProperty("entityId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_a_concurrency_conflict_error()
    {
        var payload = await PayloadAsync(await BuildAsync(), "parcelMove", "{ id: 4 }", "parcel { id }");

        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("ConcurrencyConflictError");
        error.GetProperty("code").GetString().Should().Be(ConcurrencyConflictError.ConcurrencyConflict).And.Be("concurrency-conflict");
        error.GetProperty("message").GetString().Should().Be("The Parcel '4' was modified by another operation since it was loaded.");
        Arguments(error).Should().BeEmpty();
    }

    [Fact]
    public async Task The_message_is_in_the_readers_language_when_a_localizer_is_registered()
    {
        var executor = await BuildAsync(localizer: new ByLanguage());

        // The language is the one current where the request is executed, as request localization sets it.
        var dutch = await InCultureAsync("nl-NL", async () => new[]
        {
            await PayloadAsync(executor, "parcelRelabel", """{ id: 4, label: "" }""", "parcel { id }"),
            await PayloadAsync(executor, "parcelAddress", """{ id: 4, street: "x" }""", "parcel { id }"),
            await PayloadAsync(executor, "parcelSeal", "{ id: 4 }", "parcel { id }"),
            await PayloadAsync(executor, "parcelMove", "{ id: 4 }", "parcel { id }"),
        });

        Message(dutch[0]).Should().Be("nl " + Parcels.LabelRequired);
        Message(dutch[1]).Should().Be("nl invalid-value", "an invalid value is looked up under its code, as a refusal is");
        dutch[1].GetProperty("errors")[0].GetProperty("failures").EnumerateArray().Select(failure => failure.GetProperty("message").GetString())
            .Should().Equal("nl Street.TooLong", "nl City.Required");
        Message(dutch[2]).Should().Be("nl Parcel.OverWeight");
        dutch[2].GetProperty("errors")[0].GetProperty("violations").EnumerateArray().Select(violation => violation.GetProperty("message").GetString())
            .Should().Equal("nl Parcel.OverWeight", "nl Stamp.Missing");
        Message(dutch[3]).Should().Be("nl concurrency-conflict", "a lost race is looked up under its code, as a refusal is");

        // The same schema, another reader.
        var english = await InCultureAsync("en-US", () => PayloadAsync(executor, "parcelRelabel", """{ id: 4, label: "" }""", "parcel { id }"));
        Message(english).Should().Be("en " + Parcels.LabelRequired);

        static string? Message(JsonElement payload) => payload.GetProperty("errors")[0].GetProperty("message").GetString();
    }

    [Fact]
    public async Task A_refusal_subclass_is_a_top_level_coded_error_instead()
    {
        // HotChocolate matches an error type by the exception's exact type, so a class derived from the refusal
        // passes the conventions by and reaches the error filter.
        var response = await ExecuteAsync(await BuildAsync(), Mutation("parcelReturn", "{ id: 4 }", "parcel { id }"));

        var error = response.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("Returns are closed for this parcel.");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("parcels.return-closed");
        error.GetProperty("extensions").GetProperty("kind").GetString().Should().Be(nameof(RefusalKind.Conflict));
        error.GetProperty("path")[0].GetString().Should().Be("parcelReturn");
    }

    [Fact]
    public async Task Any_other_exception_is_still_an_unexpected_error()
    {
        var response = await ExecuteAsync(await BuildAsync(), Mutation("parcelLose", "{ id: 4 }", "parcel { id }"));

        var error = response.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("Unexpected Execution Error");
    }

    [Fact]
    public async Task A_query_refusal_is_still_a_top_level_coded_error()
    {
        var response = await ExecuteAsync(await BuildAsync(), "{ refused }");

        var error = response.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("message").GetString().Should().Be("The gold plan is closed.");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("subscription.plan-closed");
        error.GetProperty("extensions").GetProperty("kind").GetString().Should().Be(nameof(RefusalKind.Conflict));
        error.GetProperty("path")[0].GetString().Should().Be("refused");
    }

    [Fact]
    public async Task Every_mutation_payload_has_the_errors_list()
    {
        var executor = await BuildAsync();

        var schema = await ExecuteAsync(executor,
            """
            {
              __schema {
                mutationType {
                  fields {
                    name
                    args { name type { kind ofType { name } } }
                    type { kind ofType { name } }
                  }
                }
              }
            }
            """);

        var fields = schema.GetProperty("data").GetProperty("__schema").GetProperty("mutationType").GetProperty("fields").EnumerateArray().ToArray();
        fields.Select(field => field.GetProperty("name").GetString()).Should().BeEquivalentTo(
            "parcelRelabel", "parcelAddress", "parcelReaddress", "parcelSeal", "parcelReseal", "parcelMove", "parcelReturn", "parcelLose", "parcelCount", "parcelDeliver");

        foreach (var field in fields)
        {
            var name = field.GetProperty("name").GetString()!;
            var pascal = char.ToUpperInvariant(name[0]) + name[1..];

            // The payload, never null: a command always answers what became of it.
            field.GetProperty("type").GetProperty("kind").GetString().Should().Be("NON_NULL");
            field.GetProperty("type").GetProperty("ofType").GetProperty("name").GetString().Should().Be(pascal + "Payload");

            // One input object, when the command takes anything.
            var arguments = field.GetProperty("args").EnumerateArray().ToArray();
            if (name == "parcelCount")
            {
                arguments.Should().BeEmpty();
            }
            else
            {
                var input = arguments.Should().ContainSingle().Which;
                input.GetProperty("name").GetString().Should().Be("input");
                input.GetProperty("type").GetProperty("ofType").GetProperty("name").GetString().Should().Be(pascal + "Input");
            }

            // errors: [XError!], null when the command went through, and XError a union of the toolkit's four.
            var types = await ExecuteAsync(executor,
                $$"""
                {
                  payload: __type(name: "{{pascal}}Payload") { fields { name type { kind ofType { kind ofType { name } } } } }
                  errors: __type(name: "{{pascal}}Error") { kind possibleTypes { name } }
                }
                """);

            var errors = types.GetProperty("data").GetProperty("payload").GetProperty("fields").EnumerateArray()
                .Should().ContainSingle(payloadField => payloadField.GetProperty("name").GetString() == "errors", "{0} answers its errors", name).Which;
            errors.GetProperty("type").GetProperty("kind").GetString().Should().Be("LIST");
            errors.GetProperty("type").GetProperty("ofType").GetProperty("kind").GetString().Should().Be("NON_NULL");
            errors.GetProperty("type").GetProperty("ofType").GetProperty("ofType").GetProperty("name").GetString().Should().Be(pascal + "Error");

            var union = types.GetProperty("data").GetProperty("errors");
            union.GetProperty("kind").GetString().Should().Be("UNION");
            union.GetProperty("possibleTypes").EnumerateArray().Select(type => type.GetProperty("name").GetString())
                .Should().BeEquivalentTo(name == "parcelDeliver"
                    ? ["RefusalError", "InvalidValuesError", "BrokenRulesError", "ConcurrencyConflictError", "OutOfReachError"]
                    : new[] { "RefusalError", "InvalidValuesError", "BrokenRulesError", "ConcurrencyConflictError" });
        }
    }

    [Fact]
    public async Task An_error_type_of_the_applications_own_joins_the_list_beside_the_four()
    {
        var executor = await BuildAsync();

        // The field that declares it answers it, read through the same interface as the toolkit's errors.
        var payload = await PayloadAsync(executor, "parcelDeliver", "{ id: 4 }", "parcel { id }");
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("OutOfReachError");
        error.GetProperty("code").GetString().Should().Be("parcels.out-of-reach");
        error.GetProperty("message").GetString().Should().Be("No courier reaches the far shore.");
        Arguments(error).Should().Equal(("Place", "the far shore"));

        Fields(executor.Schema.ToString(), "type OutOfReachError implements CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!");
    }

    [Fact]
    public async Task An_error_type_that_is_not_a_coded_error_cannot_be_answered()
    {
        // CodedError is the interface of every error type of the schema. HotChocolate gives a type that lacks
        // the interface's fields those fields all the same, so the schema builds, and the error cannot be read
        // when it is answered: its code and arguments are asked of something that has none. An error type of
        // the application's own therefore implements ICodedError.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitMutationConventions()
            .AddQueryType<Failing>()
            .AddMutationType<Uncoded>()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

        var response = await ExecuteAsync(executor, Mutation("parcelDeliver", "{ id: 4 }", "parcel { id }"));

        response.GetProperty("data").GetProperty("parcelDeliver").GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        response.GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("extensions").GetProperty("coordinate").GetString())
            .Should().Contain("OutOfReachError.code").And.Contain("OutOfReachError.arguments");
    }

    [Fact]
    public async Task The_error_types_read_as_one_interface()
    {
        var sdl = (await BuildAsync()).Schema.ToString();

        // What every error has, declared once.
        Fields(sdl, "interface CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!");

        Fields(sdl, "type RefusalError implements CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!", "kind: RefusalKind!", "field: String");
        Fields(sdl, "type InvalidValuesError implements CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!", "failures: [ValueFailure!]!");
        Fields(sdl, "type ValueFailure").Should().BeEquivalentTo(
            "code: String!", "message: String!", "field: String", "arguments: [FailureArgument!]!");
        Fields(sdl, "type BrokenRulesError implements CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!", "violations: [RuleViolation!]!");
        Fields(sdl, "type RuleViolation").Should().BeEquivalentTo(
            "code: String!", "message: String!", "entity: String", "entityId: String", "arguments: [FailureArgument!]!");
        Fields(sdl, "type ConcurrencyConflictError implements CodedError").Should().BeEquivalentTo(
            "code: String!", "message: String!", "arguments: [FailureArgument!]!");
        Fields(sdl, "type FailureArgument").Should().BeEquivalentTo("name: String!", "value: String");

        // The kind is the only enum the errors bring, with HotChocolate's spelling unless the host chose another.
        Fields(sdl, "enum RefusalKind").Should().Equal("INVALID", "NOT_PERMITTED", "NOT_FOUND", "CONFLICT");
        sdl.Should().NotContain("enum TypeCode", "an enum in an error type does not drag the members of System.Enum into the schema");
    }

    [Fact]
    public async Task The_error_types_are_described_by_the_toolkit_whatever_the_schema_reads()
    {
        // HotChocolate describes a type from the XML documentation beside its assembly. The toolkit's is written for
        // the C# reader, and whether it lies beside the assembly is the application's build to decide: here it does,
        // in an application built from the packages it does not unless the build copies it. What a client reads of
        // the toolkit's types is the toolkit's own, so a schema that reads XML documentation and one that does not
        // describe them alike, and a gateway composing several source schemas meets one description of each.
        SchemaDescriptions.ToolkitXmlDocumentationIsBeside().Should().BeTrue("the schema that reads XML documentation has the toolkit's to read");
        var read = (await BuildAsync()).Schema;
        var unread = (await BuildAsync(graphql => graphql.ModifyOptions(options => options.UseXmlDocumentation = false))).Schema;

        foreach (var type in ToolkitTypes)
        {
            var described = SchemaDescriptions.Of(read, type);
            described.Should().Equal(SchemaDescriptions.Of(unread, type), "{0} is described by the toolkit, whatever the schema reads", type);
            described.Should().NotContain(line => line.EndsWith(": ", StringComparison.Ordinal), "the type and every field or value of {0} is described", type);
        }

        // Written for a client: none of the C# the package's documentation is about, and no type's declaration.
        var sdl = read.ToString();
        sdl.Should().NotContain("IFailureLocalizer").And.NotContain("HotChocolate");
        sdl.Split("interface CodedError ").Should().HaveCount(2, "the interface is declared once and quoted nowhere");
        sdl.Split("type RefusalError ").Should().HaveCount(2, "the type is declared once and quoted nowhere");
    }

    [Fact]
    public async Task A_schema_with_the_conventions_and_no_mutation_declares_what_the_errors_are_made_of()
    {
        // The docs say it: the conventions alone bring the kind and the three types an error's lists are made of,
        // before the schema has a mutation whose payload would name them.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitMutationConventions()
            .AddQueryType<Failing>()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);
        var sdl = executor.Schema.ToString();

        sdl.Should().NotContain("type Mutation", "the schema under test has no mutation");

        Fields(sdl, "enum RefusalKind").Should().Equal("INVALID", "NOT_PERMITTED", "NOT_FOUND", "CONFLICT");
        Fields(sdl, "type FailureArgument").Should().BeEquivalentTo("name: String!", "value: String");
        Fields(sdl, "type ValueFailure").Should().BeEquivalentTo(
            "code: String!", "message: String!", "field: String", "arguments: [FailureArgument!]!");
        Fields(sdl, "type RuleViolation").Should().BeEquivalentTo(
            "code: String!", "message: String!", "entity: String", "entityId: String", "arguments: [FailureArgument!]!");
    }

    [Fact]
    public async Task Calling_it_twice_builds_the_same_schema()
    {
        var once = (await BuildAsync()).Schema.ToString();
        var twice = (await BuildAsync(graphql => graphql.AddDDDToolkitMutationConventions())).Schema.ToString();

        twice.Should().Be(once);
    }

    [Fact]
    public async Task The_error_types_are_shareable_in_a_source_schema_only()
    {
        var served = (await BuildAsync()).Schema.ToString();
        var source = (await BuildAsync(graphql => graphql.AddSourceSchemaDefaults())).Schema.ToString();

        served.Should().NotContain("@shareable", "a schema served to clients directly has no use for the directive");

        // A second source schema with the conventions declares the same seven types, and to the gateway a type
        // two schemas return has to say that it is shared.
        string[] shared =
        [
            "type RefusalError implements CodedError",
            "type InvalidValuesError implements CodedError",
            "type ValueFailure",
            "type BrokenRulesError implements CodedError",
            "type RuleViolation",
            "type ConcurrencyConflictError implements CodedError",
            "type FailureArgument",
        ];

        foreach (var type in shared)
        {
            source.Should().Contain(type + " @shareable {");
        }

        // What belongs to one mutation of one schema is nobody else's.
        source.Should().Contain("type ParcelRelabelPayload {").And.Contain("type Parcel {");
    }

    [Fact]
    public async Task A_source_schema_of_internal_types_builds_with_the_conventions()
    {
        // A module that exports one type, its entry, keeps everything of its GraphQL internal: the type
        // extensions, the records, the data loader and the registration. HotChocolate has to build a source
        // schema of them, with a lookup, a key and the conventions, and answer through them.
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDepotSchema()
            .BuildRequestExecutorAsync(cancellationToken: Cancellation);

        Record($"HotChocolate {typeof(IRequestExecutor).Assembly.GetName().Version}: a source schema of internal types builds.");

        typeof(DepotQueries).IsNotPublic.Should().BeTrue();
        typeof(DepotMutations).IsNotPublic.Should().BeTrue();
        typeof(DepotOutput).IsNotPublic.Should().BeTrue();
        typeof(DepotLoader).IsNotPublic.Should().BeTrue();

        var sdl = executor.Schema.ToString();
        sdl.Should().Contain("depot(id: Int!): Depot").And.Contain("@lookup");
        sdl.Should().Contain("type Depot").And.Contain("@key(fields: \"id\")");

        // A lookup, through the internal data loader: one that is there, and one that is not.
        var found = await ExecuteAsync(executor, "{ north: depot(id: 1) { id name } nowhere: depot(id: 9) { id } }");
        found.TryGetProperty("errors", out _).Should().BeFalse("the lookups answered {0}", found);
        found.GetProperty("data").GetProperty("north").GetProperty("name").GetString().Should().Be("North");
        found.GetProperty("data").GetProperty("nowhere").ValueKind.Should().Be(JsonValueKind.Null);

        // A mutation that goes through, and one that is refused.
        var renamed = await PayloadAsync(executor, "depotRename", """{ id: 1, name: "Harbor" }""", "depot { id name }");
        renamed.GetProperty("depot").GetProperty("name").GetString().Should().Be("Harbor");
        renamed.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);

        var refused = await PayloadAsync(executor, "depotRename", """{ id: 1, name: "" }""", "depot { id }");
        var error = refused.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(DepotSchema.NameRequired);
        error.GetProperty("field").GetString().Should().Be("name");
    }

    // ---------------------------------------------------------------- the schema under test

    private static ValueTask<IRequestExecutor> BuildAsync(Action<IRequestExecutorBuilder>? configure = null, IFailureLocalizer? localizer = null)
    {
        var services = new ServiceCollection();
        if (localizer is not null)
        {
            services.AddSingleton(localizer);
        }

        var graphql = services
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddDDDToolkitErrors()
            .AddDDDToolkitMutationConventions()
            .AddQueryType<Failing>()
            .AddMutationType<Parcels>();

        configure?.Invoke(graphql);
        return graphql.BuildRequestExecutorAsync(cancellationToken: Cancellation);
    }

    private static string Mutation(string field, string input, string selection)
        => $$"""mutation { {{field}}(input: {{input}}) { {{selection}} {{Errors}} } }""";

    /// <summary>Runs one mutation and returns its payload, failing the test when the response has top-level errors.</summary>
    private static async Task<JsonElement> PayloadAsync(IRequestExecutor executor, string field, string input, string selection)
    {
        var response = await ExecuteAsync(executor, Mutation(field, input, selection));
        response.TryGetProperty("errors", out var errors).Should().BeFalse("the mutation answered {0}", errors);
        return response.GetProperty("data").GetProperty(field);
    }

    private static async Task<JsonElement> ExecuteAsync(IRequestExecutor executor, string document)
    {
        var result = await executor.ExecuteAsync(document, Cancellation);
        using var json = JsonDocument.Parse(result.ToJson());
        return json.RootElement.Clone();
    }

    private static (string Name, string? Value)[] Arguments(JsonElement error)
        => [.. error.GetProperty("arguments").EnumerateArray().Select(argument => (argument.GetProperty("name").GetString()!, argument.GetProperty("value").GetString()))];

    /// <summary>The field lines of one type in the printed schema, without descriptions and directives.</summary>
    private static string[] Fields(string sdl, string header)
    {
        sdl = SchemaDescriptions.RemovedFrom(sdl);
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return [.. sdl[(open + 1)..close].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>Runs <paramref name="action"/> with <paramref name="culture"/> as the reader's language, and puts the old one back.</summary>
    private static async Task<T> InCultureAsync<T>(string culture, Func<Task<T>> action)
    {
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return await action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    /// <summary>Writes what a probe found to the test's output, where a run's log keeps it.</summary>
    private static void Record(string finding) => TestContext.Current.TestOutputHelper?.WriteLine(finding);

    /// <summary>A mutation root whose field declares an exception as its error, which has a message and nothing else.</summary>
    public sealed class Uncoded
    {
        [Error<OutOfReachException>]
        public Parcel ParcelDeliver(int id) => throw new OutOfReachException("the far shore");
    }

    /// <summary>Stands in for a real localizer: it answers the reader's language and the code it was asked for.</summary>
    private sealed class ByLanguage : IFailureLocalizer
    {
        private static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        public string Localize(ValidationError error) => Language + " " + error.Code;

        public string Localize(InvariantViolation violation) => Language + " " + violation.Code;

        public string Localize(RefusalException refusal) => Language + " " + refusal.Code;
    }

    /// <summary>The refusal both gates throw: the caller may not do this, whatever the command.</summary>
    private static class Gate
    {
        public const string Code = "gate.closed";

        public static readonly FieldMiddlewareConfiguration Middleware = new(
            _ => _ => throw new RefusalException(Code, RefusalKind.NotPermitted, "The gate is closed."));
    }

    /// <summary>A gate added to each mutation field when the interceptors are handed it, before the conventions wrap it.</summary>
    private sealed class GateOnEachMutationField : TypeInterceptor
    {
        public override void OnBeforeCompleteMutationField(ITypeCompletionContext completionContext, ObjectFieldConfiguration mutationField)
            => mutationField.MiddlewareConfigurations.Insert(0, Gate.Middleware);
    }

    /// <summary>A gate put first on the fields of the mutation type as it is completed, after the conventions wrapped them.</summary>
    private sealed class GateOnTheCompletedMutationType : TypeInterceptor
    {
        private ObjectTypeConfiguration? _mutation;

#pragma warning disable HC8001 // The one public hook that says which type is the mutation type, whatever it is called.
        public override void OnAfterResolveRootType(ITypeCompletionContext completionContext, ObjectTypeConfiguration configuration, OperationType operationType)
        {
            if (operationType == OperationType.Mutation)
            {
                _mutation = configuration;
            }
        }
#pragma warning restore HC8001

        public override void OnBeforeCompleteType(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
        {
            if (_mutation is not null && ReferenceEquals(configuration, _mutation))
            {
                foreach (var field in _mutation.Fields)
                {
                    field.MiddlewareConfigurations.Insert(0, Gate.Middleware);
                }
            }
        }
    }
}
