using System.Reflection;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.HotChocolate.Tests.Domain;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// <c>AddDDDToolkitEnumValues(...)</c> and <c>AddDDDToolkitErrors(spelling)</c>: the values of a schema's enums
/// are spelled the way the host chose, and a refusal's <c>kind</c> the same way wherever a client meets it.
/// </summary>
public sealed class EnumValueSpellingTests : IDisposable
{
    /// <summary>An XML documentation file for this assembly, written for the test that reads descriptions from one.</summary>
    private readonly string _documentation = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DDDToolkit.EnumValueSpellingTests.{Guid.NewGuid():N}.xml");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => File.Delete(_documentation);

    [Fact]
    public async Task Lower_snake_case_spells_not_permitted()
    {
        var executor = await BuildAsync(graphql => graphql.AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase));

        // In the schema: every enum, the toolkit's and the application's own.
        var sdl = executor.Schema.ToString();
        Values(sdl, "enum RefusalKind").Should().Equal("invalid", "not_permitted", "not_found", "conflict");
        Values(sdl, "enum Signal").Should().Equal("go", "slow_down", "STOP");

        // In an answer, in an argument and in a variable.
        var answer = await ExecuteAsync(executor,
            """
            query ($kind: RefusalKind!) {
              written: echo(kind: not_permitted)
              sent: echo(kind: $kind)
              signals
            }
            """,
            new Dictionary<string, object?> { ["kind"] = "not_found" });

        answer.TryGetProperty("errors", out var errors).Should().BeFalse("the query answered {0}", errors);
        answer.GetProperty("data").GetProperty("written").GetString().Should().Be("not_permitted");
        answer.GetProperty("data").GetProperty("sent").GetString().Should().Be("not_found");
        answer.GetProperty("data").GetProperty("signals").EnumerateArray().Select(signal => signal.GetString())
            .Should().Equal("go", "slow_down", "STOP");

        // And in a refused mutation's payload, where the kind is a field of the error.
        var refused = await ExecuteAsync(executor, """mutation { parcelRelabel(input: { id: 4, label: "" }) { errors { ... on RefusalError { kind } } } }""");
        refused.GetProperty("data").GetProperty("parcelRelabel").GetProperty("errors")[0].GetProperty("kind").GetString().Should().Be("invalid");

        // The spelling HotChocolate would have used is no longer a value.
        var upper = await ExecuteAsync(executor, "{ echo(kind: NOT_PERMITTED) }");
        upper.GetProperty("errors").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Upper_snake_case_is_hotchocolates_default()
    {
        var untouched = (await BuildAsync()).Schema.ToString();
        var upper = (await BuildAsync(graphql => graphql.AddDDDToolkitEnumValues(EnumValueSpelling.UpperSnakeCase))).Schema.ToString();

        Values(upper, "enum RefusalKind").Should().Equal("INVALID", "NOT_PERMITTED", "NOT_FOUND", "CONFLICT");
        Values(upper, "enum Signal").Should().Equal("GO", "SLOW_DOWN", "STOP");
        upper.Should().Be(untouched, "nothing else about the schema changes: its names, and the descriptions it reads from XML documentation");
    }

    [Fact]
    public async Task A_member_with_a_name_of_its_own_keeps_it()
    {
        var sdl = (await BuildAsync(graphql => graphql.AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase))).Schema.ToString();

        // [GraphQLName] is a decision about one value, and a spelling is the default for the rest.
        Values(sdl, "enum Signal").Should().Contain("STOP").And.NotContain("halt");
    }

    [Fact]
    public async Task A_member_that_would_be_spelled_as_a_literal_is_refused_when_the_schema_is_built()
    {
        // GraphQL reads true, false and null as literals, so no document could name such a value. In capitals
        // they are ordinary names, so the same enum builds with HotChocolate's spelling.
        var upper = (await BuildAsync(graphql => graphql
            .AddType<Answers>()
            .AddDDDToolkitEnumValues(EnumValueSpelling.UpperSnakeCase))).Schema.ToString();
        Values(upper, "enum Answer").Should().Equal("TRUE", "FALSE", "UNKNOWN");

        var lower = async () => await BuildAsync(graphql => graphql
            .AddType<Answers>()
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase));

        var refused = (await lower.Should().ThrowAsync<Exception>()).Which;
        refused.ToString().Should().Contain("Answer.True").And.Contain("'true'").And.Contain("[GraphQLName]");

        // With a name of its own the member is no longer the convention's to spell.
        var named = (await BuildAsync(graphql => graphql
            .AddType<NamedAnswers>()
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase))).Schema.ToString();
        Values(named, "enum NamedAnswer").Should().Equal("yes", "no", "unknown");
    }

    [Fact]
    public async Task HotChocolates_own_enums_keep_its_spelling()
    {
        // A source schema says how its scalars are serialized with a directive whose argument is an enum of
        // HotChocolate's own. The Fusion composer knows its values as HotChocolate writes them, so a schema that
        // respelled them would print "@serializeAs(type: string)" and no gateway would compose it.
        var sdl = (await BuildAsync(graphql => graphql
            .AddSourceSchemaDefaults()
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase))).Schema.ToString();

        Values(sdl, "enum ScalarSerializationType").Should().Contain("STRING").And.NotContain("string");
        sdl.Should().Contain("type: STRING").And.NotContain("type: string");

        // The application's own, and the toolkit's, are still spelled the host's way beside it.
        Values(sdl, "enum RefusalKind").Should().Contain("not_permitted");
    }

    [Fact]
    public async Task Xml_documentation_still_describes_the_schema()
    {
        // The spelling replaces the schema's naming conventions, which are also what reads descriptions from
        // XML documentation files. They still do, from the file the schema's options name.
        File.WriteAllText(_documentation,
            $"""
            <?xml version="1.0"?>
            <doc>
              <assembly><name>{typeof(Signal).Assembly.GetName().Name}</name></assembly>
              <members>
                <member name="T:{typeof(Signal).FullName!.Replace('+', '.')}">
                  <summary>What a driver is shown.</summary>
                </member>
                <member name="F:{typeof(Signal).FullName!.Replace('+', '.')}.{nameof(Signal.SlowDown)}">
                  <summary>Amber.</summary>
                </member>
              </members>
            </doc>
            """);

        var described = (await BuildAsync(graphql => graphql
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)
            .ModifyOptions(options => options.ResolveXmlDocumentationFileName = DocumentationOf))).Schema.ToString();

        described.Should().Contain("What a driver is shown.").And.Contain("Amber.");

        var silent = (await BuildAsync(graphql => graphql
            .AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)
            .ModifyOptions(options =>
            {
                options.ResolveXmlDocumentationFileName = DocumentationOf;
                options.UseXmlDocumentation = false;
            }))).Schema.ToString();

        silent.Should().NotContain("What a driver is shown.", "a schema that turned XML documentation off stays without it");
        Values(silent, "enum Signal").Should().Equal("go", "slow_down", "STOP");

        string DocumentationOf(Assembly assembly)
            => assembly == typeof(Signal).Assembly ? _documentation : assembly.GetName().Name + ".xml";
    }

    [Fact]
    public async Task The_error_filters_kind_follows_the_spelling_it_was_given()
    {
        (await KindAsync(graphql => graphql.AddDDDToolkitErrors())).Should().Be("NotPermitted", "without a spelling the kind is the member's name, as before");
        (await KindAsync(graphql => graphql.AddDDDToolkitErrors(EnumValueSpelling.LowerSnakeCase))).Should().Be("not_permitted");
        (await KindAsync(graphql => graphql.AddDDDToolkitErrors(EnumValueSpelling.UpperSnakeCase))).Should().Be("NOT_PERMITTED");

        // The same word as the schema's enum has, in both spellings: a client reads one RefusalKind.
        foreach (var spelling in Enum.GetValues<EnumValueSpelling>())
        {
            var executor = await BuildAsync(graphql => graphql.AddDDDToolkitEnumValues(spelling), errors: graphql => graphql.AddDDDToolkitErrors(spelling));
            var kinds = Values(executor.Schema.ToString(), "enum RefusalKind");

            foreach (var (kind, index) in Enum.GetValues<RefusalKind>().Select((kind, index) => (kind, index)))
            {
                var refused = await ExecuteAsync(executor, $"{{ refuse(kind: {kinds[index]}) }}");
                refused.GetProperty("errors")[0].GetProperty("extensions").GetProperty("kind").GetString()
                    .Should().Be(kinds[index], "{0} is spelled {1} in the schema", kind, kinds[index]);
            }
        }

        static async Task<string?> KindAsync(Action<IRequestExecutorBuilder> errors)
        {
            var refused = await ExecuteAsync(await BuildAsync(errors: errors), "{ refuse(kind: NOT_PERMITTED) }");
            return refused.GetProperty("errors")[0].GetProperty("extensions").GetProperty("kind").GetString();
        }
    }

    [Fact]
    public void A_spelling_that_is_not_one_of_the_two_is_refused_where_it_is_chosen()
    {
        var graphql = new ServiceCollection().AddGraphQL();

        graphql.Invoking(builder => builder.AddDDDToolkitEnumValues((EnumValueSpelling)7))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("spelling");
        graphql.Invoking(builder => builder.AddDDDToolkitErrors((EnumValueSpelling)7))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName("kindSpelling");
    }

    // ---------------------------------------------------------------- the schema under test

    private static ValueTask<IRequestExecutor> BuildAsync(
        Action<IRequestExecutorBuilder>? configure = null,
        Action<IRequestExecutorBuilder>? errors = null)
    {
        var graphql = new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddDDDToolkitMutationConventions()
            .AddQueryType<Spelled>()
            .AddMutationType<Parcels>();

        (errors ?? (builder => builder.AddDDDToolkitErrors()))(graphql);
        configure?.Invoke(graphql);
        return graphql.BuildRequestExecutorAsync(cancellationToken: Cancellation);
    }

    private static async Task<JsonElement> ExecuteAsync(IRequestExecutor executor, string document, IReadOnlyDictionary<string, object?>? variables = null)
    {
        var result = variables is null
            ? await executor.ExecuteAsync(document, Cancellation)
            : await executor.ExecuteAsync(document, variables, Cancellation);

        using var json = JsonDocument.Parse(result.ToJson());
        return json.RootElement.Clone();
    }

    /// <summary>The values of one enum in the printed schema, in the order it declares them, without descriptions.</summary>
    private static string[] Values(string sdl, string header)
    {
        var start = sdl.IndexOf(header + " ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the schema should declare '{0}'", header);

        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return [.. sdl[(open + 1)..close]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('"'))];
    }

    /// <summary>A query root with enums in every position: an answer, an argument, a list, and a refusal of each kind.</summary>
    public sealed class Spelled
    {
        public RefusalKind Echo(RefusalKind kind) => kind;

        public Signal[] Signals() => [Signal.Go, Signal.SlowDown, Signal.Halt];

        public string Refuse(RefusalKind kind) => throw new RefusalException("spelled.refused", kind, "Refused.");
    }

    /// <summary>An enum whose members, in lower case, are GraphQL's literals.</summary>
    public enum Answer
    {
        True,

        False,

        Unknown,
    }

    /// <summary>The same answers, with the two that cannot be spelled in lower case given names of their own.</summary>
    public enum NamedAnswer
    {
        [GraphQLName("yes")]
        True,

        [GraphQLName("no")]
        False,

        Unknown,
    }

    /// <summary>A type that brings <see cref="Answer"/> into a schema.</summary>
    [ExtendObjectType<Spelled>]
    public sealed class Answers
    {
        public Answer Answer() => EnumValueSpellingTests.Answer.Unknown;
    }

    /// <summary>A type that brings <see cref="NamedAnswer"/> into a schema.</summary>
    [ExtendObjectType<Spelled>]
    public sealed class NamedAnswers
    {
        public NamedAnswer NamedAnswer() => EnumValueSpellingTests.NamedAnswer.Unknown;
    }

    /// <summary>An enum of the application's own, one member of which was given a name.</summary>
    public enum Signal
    {
        Go,

        SlowDown,

        [GraphQLName("STOP")]
        Halt,
    }
}
