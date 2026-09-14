using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.ExampleLibrary.GraphQl;
using DDDToolkit.HotChocolate.Tests.Domain;
using DDDToolkit.HotChocolate.Tests.GraphQl;
using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace DDDToolkit.HotChocolate.Tests;

/// <summary>
/// The source generator emits one <c>Add{Module}GraphQlRuntimeBindings</c> extension per assembly
/// and a nested <c>ChangeTypeProvider</c> per id and single value object. These tests exercise both
/// on their own, away from the full schema.
/// </summary>
public class GeneratedRuntimeBindingsTests
{
    [Fact]
    public async Task Example_library_bindings_register_without_throwing()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddCommonGraphQlRuntimeBindings()
            .AddQueryType<PingQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        executor.Schema.ToString().Should().Contain("ping");
    }

    [Fact]
    public async Task Test_assembly_bindings_register_without_throwing()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddDDDToolkitTypes()
            .AddGraphQlTestsGraphQlRuntimeBindings()
            .AddQueryType<PingQuery>()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        executor.Schema.ToString().Should().Contain("ping");
    }

    [Fact]
    public void Generated_change_type_provider_converts_a_struct_id_both_ways()
    {
        var provider = new CatId.ChangeTypeProvider();

        provider.TryCreateConverter(typeof(CatId), typeof(Guid), Root, out var toValue).Should().BeTrue();
        toValue!(TestData.Cat).Should().Be(TestData.CatGuid);

        provider.TryCreateConverter(typeof(Guid), typeof(CatId), Root, out var fromValue).Should().BeTrue();
        fromValue!(TestData.CatGuid).Should().Be(TestData.Cat);
    }

    [Fact]
    public void Generated_change_type_provider_converts_a_reference_id_and_its_twin()
    {
        var provider = new PersonId.ChangeTypeProvider();

        provider.TryCreateConverter(typeof(PersonId), typeof(Guid), Root, out var toValue).Should().BeTrue();
        toValue!(TestData.Person).Should().Be(TestData.PersonGuid);

        provider.TryCreateConverter(typeof(ValidPersonId), typeof(Guid), Root, out var twinToValue).Should().BeTrue();
        twinToValue!(TestData.Person.ToValid()).Should().Be(TestData.PersonGuid);

        provider.TryCreateConverter(typeof(Guid), typeof(ValidPersonId), Root, out var twinFromValue).Should().BeTrue();
        twinFromValue!(TestData.PersonGuid).Should().Be(TestData.Person.ToValid());
    }

    [Fact]
    public void Generated_change_type_provider_declines_pairs_it_does_not_know()
    {
        var provider = new CatId.ChangeTypeProvider();

        provider.TryCreateConverter(typeof(CatId), typeof(string), Root, out var converter).Should().BeFalse();
        converter.Should().BeNull();
    }

    /// <summary>The root provider HotChocolate passes in; these converters never delegate to it.</summary>
    private static bool Root(Type source, Type target, [NotNullWhen(true)] out ChangeType? converter)
    {
        converter = null;
        return false;
    }

    /// <summary>A query root with nothing in it, so a test can build a schema around the bindings alone.</summary>
    public sealed class PingQuery
    {
        /// <summary>Something for the schema to hang on to.</summary>
        public string Ping() => "pong";
    }
}
