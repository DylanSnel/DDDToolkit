using DDDToolkit.HotChocolate.Fusion.InMemory;
using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// The GraphQL schemas of the sample's host, held to committed files: the schema a client is offered, which the
/// gateway composes, and each module's source schema with the directives the gateway composes by.
/// </summary>
/// <remarks>
/// A change to what clients see, or to a key or a lookup, shows in a review as a change to a file. Run the tests
/// with <c>TENANCY_WRITE_SCHEMA=1</c> to write the files anew after a change that is meant: a test that wrote its
/// file fails, saying so, since it compared nothing, and passes on the next run without the variable. A build
/// server never writes, whatever its environment holds.
/// </remarks>
public sealed class GraphQLSchemaTests(SampleWithoutDatabase sample) : IClassFixture<SampleWithoutDatabase>
{
    /// <summary>The environment variable that makes the snapshot tests write the files instead of comparing with them.</summary>
    private const string WriteSchema = "TENANCY_WRITE_SCHEMA";

    /// <summary>The types more than one module declares, each with the module that owns it.</summary>
    private static readonly IReadOnlyDictionary<string, string> Owners = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Seat"] = "tenants",
        ["OrganizationUnit"] = "tenants",
        ["Role"] = "tenants",
        ["Project"] = "projects",
    };

    /// <summary>
    /// What a module adds to a type another module owns: its own fields, which it answers itself. Everything
    /// else of the type is the owner's.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string Schema, string Type), string[]> Contributed = new Dictionary<(string, string), string[]>
    {
        [("inspections", "Project")] = ["inspections"],
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> SourceSchemas => new("inspections", "projects", "tenants");

    [Fact]
    public async Task The_gateway_schema_is_the_committed_snapshot()
    {
        var printed = await sample.Services.GetRequiredService<InMemoryFusionSchemas>().PrintGatewayAsync(Cancellation);

        Compare(printed, Path.Combine(Path.GetDirectoryName(SampleLayout.HostProjectFile())!, "schema.graphql"));

        // The lookups that are there for the gateway alone are no fields of what a client is offered.
        printed.Should().Contain("project(id: ID!): Project").And.NotContain("organizationUnit(id:").And.NotContain("@internal");
    }

    [Theory]
    [MemberData(nameof(SourceSchemas))]
    public async Task Each_modules_source_schema_is_its_committed_snapshot(string name)
    {
        var schemas = sample.Services.GetRequiredService<InMemoryFusionSchemas>();

        schemas.SourceSchemaNames.Should().Equal(SourceSchemas.Select(row => row.Data), "every module registers a source schema, and no schema is registered that is not listed here");
        Compare(await schemas.PrintSourceAsync(name, Cancellation), SnapshotOf(name));
    }

    [Fact]
    public async Task The_administration_schema_is_the_committed_snapshot()
    {
        var printed = (await sample.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync(TenantsModule.AdministrationSchema, Cancellation)).Schema.ToString();

        Compare(printed, Path.Combine(SampleLayout.DirectoryOf(SampleLayout.Project("Tenants", Layer.Api)), "GraphQL", "admin.graphql"));
    }

    [Fact]
    public async Task The_administration_schema_offers_what_the_gateway_offers_of_Tenancy_and_another_persons_roles_besides()
    {
        var executors = sample.Services.GetRequiredService<IRequestExecutorProvider>();
        var administration = (await executors.GetExecutorAsync(TenantsModule.AdministrationSchema, Cancellation)).Schema;
        var tenancy = (await executors.GetExecutorAsync("tenants", Cancellation)).Schema;
        var schemas = sample.Services.GetRequiredService<InMemoryFusionSchemas>();
        var gateway = Utf8GraphQLParser.Parse(await schemas.PrintGatewayAsync(Cancellation));

        // What a seat is offered of Tenancy at /graphql: its source schema's fields that the gateway has. Not the
        // lookups, which are the gateway's alone, and whose class is marked for the source schema.
        var offered = FieldsOf(tenancy.QueryType).Intersect(FieldsOf(gateway, "Query")).ToList();
        FieldsOf(tenancy.QueryType).Except(offered).Should().BeEquivalentTo(["seat", "organizationUnit", "role"], "the lookups are all the gateway keeps to itself");

        // The administration has all of that, and another person's roles besides: the one class marked for it.
        FieldsOf(administration.QueryType).Should().BeEquivalentTo([.. offered, "seatGrants"], "the administration reads another person's roles, and that is all it adds");
        FieldsOf(administration.MutationType!).Should().BeEquivalentTo(FieldsOf(tenancy.MutationType!), "the administration changes nothing a seat could not ask to change at /graphql");
        FieldsOf(tenancy.MutationType!).Should().BeSubsetOf(FieldsOf(gateway, "Mutation"));
        administration.ToString().Should().Contain("seatGrants(seatId: UUID!): [SeatGrant!]!").And.NotContain("@lookup").And.NotContain("@internal");

        // And what a client of the gateway is offered has nothing of it: not the field, and not the type it answers.
        gateway.ToString().Should().NotContain("seatGrants").And.NotContain("SeatGrant");
        schemas.SourceSchemaNames.Should().NotContain(TenantsModule.AdministrationSchema, "the gateway leaves the administration's schema out");
    }

    private static IReadOnlyList<string> FieldsOf(IObjectTypeDefinition type)
        => [.. type.Fields.Where(field => !field.IsIntrospectionField).Select(field => field.Name)];

    /// <summary>The fields of a type of a printed schema, by its name.</summary>
    private static IReadOnlyList<string> FieldsOf(DocumentNode schema, string type)
        => [.. schema.Definitions.OfType<ObjectTypeDefinitionNode>().Where(definition => definition.Name.Value == type).SelectMany(definition => definition.Fields).Select(field => field.Name.Value)];

    [Fact]
    public async Task A_type_another_module_names_is_its_key_and_what_that_module_adds_and_requires_only_its_key_at_home()
    {
        var executors = sample.Services.GetRequiredService<IRequestExecutorProvider>();

        foreach (var name in SourceSchemas.Select(row => row.Data))
        {
            var schema = (await executors.GetExecutorAsync(name, Cancellation)).Schema;

            foreach (var (type, owner) in Owners)
            {
                if (!schema.Types.TryGetType<IObjectTypeDefinition>(type, out var declared))
                {
                    continue;
                }

                var fields = declared.Fields.Where(field => !field.IsIntrospectionField).ToList();
                if (name == owner)
                {
                    // A reference whose owner answers nothing is its key with the owner's fields null. A field the
                    // owner declared as never null would turn that into an error, so it declares none but the key.
                    fields.Where(field => field.Name != "id" && field.Type.IsNonNullType()).Select(field => $"{type}.{field.Name}")
                        .Should().BeEmpty("every field of {0} but its key may be null in {1}, which owns it", type, name);
                    fields.Should().Contain(field => field.Name == "id" && field.Type.IsNonNullType(), "the key is always there");
                    fields.Count.Should().BeGreaterThan(1, "the owner gives the rest");
                }
                else
                {
                    // Elsewhere the type is its key, and what that module itself has to say about it: fields it
                    // answers from its own data, listed here so one more is a decision. None is the owner's, so the
                    // gateway never has two modules to ask for one field; and each may be null, since a module
                    // answers nothing about an entity that is out of the caller's reach.
                    var added = Contributed.GetValueOrDefault((name, type), []);
                    fields.Select(field => field.Name).Should().BeEquivalentTo(["id", .. added], "{0} names a {1} by its key, and adds only what is listed as its own", name, type);
                    fields.Where(field => added.Contains(field.Name) && field.Type.IsNonNullType()).Select(field => field.Name)
                        .Should().BeEmpty("what {0} adds to {1} is nothing for an entity out of reach, not an error", name, type);

                    var owned = (await executors.GetExecutorAsync(owner, Cancellation)).Schema.Types.GetType<IObjectTypeDefinition>(type);
                    owned.Fields.Select(field => field.Name).Intersect(added).Should().BeEmpty("a field has one module that answers it");
                }
            }
        }
    }

    private static string SnapshotOf(string sourceSchema)
    {
        var module = SampleLayout.Modules.Single(listed => string.Equals(listed, sourceSchema, StringComparison.OrdinalIgnoreCase));
        return Path.Combine(SampleLayout.DirectoryOf(SampleLayout.Project(module, Layer.Api)), "GraphQL", "schema.graphql");
    }

    /// <summary>
    /// Whether this run writes the files: a developer asked for it, and it is not a build server's run, which sets
    /// <c>CI</c> or <c>GITHUB_ACTIONS</c> and compares whatever else is set.
    /// </summary>
    private static bool Writes
        => Environment.GetEnvironmentVariable(WriteSchema) == "1"
           && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
           && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    /// <summary>
    /// Compares a printed schema with its committed file, line endings aside. Asked to write, it writes the file
    /// and fails: a run that wrote compared nothing, and a shell that keeps the variable must not stay green.
    /// </summary>
    private static void Compare(string printed, string file)
    {
        var text = Normalized(printed);

        if (Writes)
        {
            File.WriteAllText(file, text);
            Assert.Fail($"{file} was written, and compared with nothing. Read the change, then run the tests again without {WriteSchema}.");
        }

        File.Exists(file).Should().BeTrue("{0} is committed; run the tests with {1}=1 to write it", file, WriteSchema);
        text.Should().Be(Normalized(File.ReadAllText(file)), "{0} is what the schema was when it was last reviewed; run the tests with {1}=1 after a change that is meant", file, WriteSchema);
    }

    private static string Normalized(string schema) => schema.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
}
