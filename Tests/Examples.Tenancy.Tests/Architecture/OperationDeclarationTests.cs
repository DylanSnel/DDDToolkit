using System.Reflection;
using FluentAssertions;
using HotChocolate.Types;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// How the sample declares a GraphQL operation: a static method that says what it is, with <c>[Query]</c>,
/// <c>[Mutation]</c> or <c>[Subscription]</c>, in a static class. HotChocolate's generator finds the methods and
/// writes the registration, so a schema names no class of fields.
/// </summary>
/// <remarks>
/// The older way, a class that extends <c>Query</c> or <c>Mutation</c> with <c>[ExtendObjectType]</c> or is marked
/// <c>[QueryType]</c>, still works in HotChocolate, and a class written that way would be registered beside the
/// methods without a word. So it is refused here, with one exception: a class that holds a paged field is marked
/// <c>[QueryType]</c>, because HotChocolate writes the connection type of a paged field only for a class it
/// generates the type of. Which classes of which module use the exception is held by
/// <see cref="GraphQLDeclarationTests"/>, with the other rules about how a module declares its schema.
/// </remarks>
public sealed class OperationDeclarationTests
{
    private static readonly string[] OperationTypes = [OperationTypeNames.Query, OperationTypeNames.Mutation, OperationTypeNames.Subscription];

    private static readonly IReadOnlyList<Type> SampleTypes =
    [
        .. SampleLayout.Projects.Select(project => project.Anchor.Assembly).Append(typeof(Program).Assembly).Distinct().SelectMany(TypeScan.TypesOf),
    ];

    [Fact]
    public void No_type_of_the_sample_extends_an_operation_type_by_an_attribute_on_the_class()
    {
        SampleTypes
            .Where(type => type.GetCustomAttributes<ExtendObjectTypeAttribute>(inherit: false).Any(extension => OperationTypes.Contains(extension.Name))
                || (type.IsDefined(typeof(QueryTypeAttribute), inherit: false) && !SampleLayout.IsAClassOfPagedFields(type))
                || type.IsDefined(typeof(MutationTypeAttribute), inherit: false)
                || type.IsDefined(typeof(SubscriptionTypeAttribute), inherit: false))
            .Select(type => type.FullName)
            .Should().BeEmpty("an operation is a static method marked [Query], [Mutation] or [Subscription]; no class says it is a part of an operation type, but one that holds a paged field");
    }

    [Fact]
    public void An_operation_is_a_static_method_of_a_static_class()
    {
        const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var operations = SampleTypes.SelectMany(type => type.GetMethods(Declared)).Where(SampleLayout.IsOperation).ToList();

        // Every module's API project declares some, or the rule above would hold of a sample with no GraphQL at all.
        operations.Select(operation => operation.DeclaringType!.Assembly).Distinct()
            .Should().BeEquivalentTo(SampleLayout.Projects.Where(project => project.Layer == Layer.Api).Select(project => project.Anchor.Assembly));

        operations.Where(operation => !operation.IsStatic || !(operation.DeclaringType!.IsAbstract && operation.DeclaringType.IsSealed))
            .Select(operation => $"{operation.DeclaringType!.Name}.{operation.Name}")
            .Should().BeEmpty("a field of an operation type needs no instance: what it uses it takes as parameters");
    }
}
