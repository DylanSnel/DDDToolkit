namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The names of the generated files. Visual Studio places each one at
/// <c>{project}\Generated\{generator assembly}\{generator type}\{hint name}</c> and has to expand that
/// path, so a hint name that repeats a long namespace pushed a module a few folders deep past 260
/// characters and the project would not load. The names must stay short and still be unique.
/// </summary>
public class HintNameTests
{
    [Fact]
    public void A_hint_name_is_the_type_name_and_a_hash_not_the_namespace()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Company.Product.Module.Domain.Aggregates.SomeFeature;

            [EntityId<System.Guid>]
            public readonly partial record struct OptionId;

            [Entity<OptionId>]
            public partial class ProjectAttributeOption
            {
            }
            """).WithEntityFramework().RunCoreAnd(GeneratorTestHost.EntityFrameworkGenerators());

        result.ShouldCompile();
        var hintNames = result.GeneratedSources.Select(static source => source.HintName).ToList();

        hintNames.Should().Contain(Hint.Of("Company.Product.Module.Domain.Aggregates.SomeFeature.ProjectAttributeOption", ".EntityFramework"));
        hintNames.Should().OnlyContain(name => !name.Contains("Company.Product", StringComparison.Ordinal));
        hintNames.Should().Contain(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^ProjectAttributeOption\.EntityFramework\.[0-9a-f]{8}\.g\.cs$"));
    }

    [Fact]
    public void Types_with_the_same_name_in_different_namespaces_get_different_hint_names()
    {
        var result = GeneratorTestHost.Create(
            """
            using DDDToolkit.Abstractions.Attributes;

            namespace Sales
            {
                [EntityId<System.Guid>]
                public readonly partial record struct OrderId;
            }

            namespace Billing
            {
                [EntityId<System.Guid>]
                public readonly partial record struct OrderId;
            }
            """).RunCore();

        result.ShouldCompile();
        result.GeneratedSources.Select(static source => source.HintName).Should()
            .Contain([Hint.Of("Sales.OrderId"), Hint.Of("Billing.OrderId")])
            .And.OnlyHaveUniqueItems();
    }
}
