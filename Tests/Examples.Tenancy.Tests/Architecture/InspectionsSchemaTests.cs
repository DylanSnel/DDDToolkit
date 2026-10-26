using System.Reflection;
using FluentAssertions;
using HotChocolate.Types.Composite;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// What is Inspections' own about its GraphQL schema: what it has to say about a project is a field of the
/// project, so its schema has no root field for a seat. How it declares its types, its loaders and its paged
/// list is held with the other modules', by <see cref="GraphQLDeclarationTests"/>.
/// </summary>
/// <remarks>
/// A root field that took a project's id was written once and worked. So nothing but a test keeps it from coming
/// back.
/// </remarks>
public sealed class InspectionsSchemaTests
{
    private static readonly ModuleProject Api = SampleLayout.Project("Inspections", Layer.Api);

    [Fact]
    public void The_module_has_no_root_field_but_the_gateways_way_in_and_the_operators()
    {
        // What Inspections says about a project is a field of the project, which the gateway reaches through the
        // lookup. A root field for a seat would be a second way to the same list, and a join of two modules. The
        // application's own staff hold no seat and reach no project that way, so the Operators feature has a
        // root field for them, which the host lets through for an operator alone.
        const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var queries = TypeScan.TypesOf(Api.Anchor.Assembly).SelectMany(type => type.GetMethods(Declared)).Where(method => method.IsDefined(typeof(global::HotChocolate.QueryAttribute), inherit: false)).ToList();
        var forOperators = queries.Where(method => method.DeclaringType!.Namespace == Api.Name + ".Operators." + SampleLayout.GraphQL).ToList();

        queries.Should().NotBeEmpty("the gateway enters the schema through a lookup");
        queries.Except(forOperators).Where(method => !method.IsDefined(typeof(LookupAttribute), inherit: false) || !method.IsDefined(typeof(InternalAttribute), inherit: false))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Should().BeEmpty("a seat asks a project for its inspections; no root field of this schema is a seat's");
        forOperators.Select(method => method.Name).Should().Equal("GetTenantProjectInspectionsAsync");
    }
}
