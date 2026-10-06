using DDDToolkit.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Composition;

/// <summary>
/// The parts packages bring to a builder a host finishes with one call: each registration brings its own, once
/// however often it is called; the one call applies them by position, then as registered, passes over a part that
/// does not belong on the builder at hand, and hands on what a part throws as it is.
/// </summary>
public sealed class ContextPartTests
{
    [Fact]
    public void Parts_are_applied_by_position_then_as_registered_and_a_second_part_of_a_name_brings_nothing()
    {
        var services = new ServiceCollection()
            .AddContextPart(Part("tests.tenancy", 200))
            .AddContextPart(Part("tests.audit", 300))
            .AddContextPart(Part("tests.row-level-security", 100))
            .AddContextPart(Part("tests.history", 300))
            .AddContextPart(Part("tests.tenancy", 50));

        var parts = services.GetContextParts<Recipe>();
        parts.InOrder.Select(part => part.ToString()).Should().Equal(
            "tests.row-level-security (100)", "tests.tenancy (200)", "tests.audit (300)", "tests.history (300)");

        var recipe = new Recipe();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ContextParts<Recipe>>().Should().BeSameAs(parts, "the one call resolves the instance the registrations added to");
        parts.ApplyTo(recipe, provider).Select(part => part.Name).Should().Equal(recipe.Steps);
        recipe.Steps.Should().Equal("tests.row-level-security", "tests.tenancy", "tests.audit", "tests.history");
    }

    [Fact]
    public void A_part_that_does_not_belong_on_the_builder_is_passed_over_and_what_a_part_throws_comes_out_as_it_is()
    {
        var services = new ServiceCollection()
            .AddContextPart(Part("tests.everywhere", 100))
            .AddContextPart(Part("tests.postgres-only", 200, recipe => recipe.Kitchen == "postgres"));
        using var provider = services.BuildServiceProvider();
        var parts = services.GetContextParts<Recipe>();

        var onSqlite = new Recipe { Kitchen = "sqlite" };
        parts.ApplyTo(onSqlite, provider).Select(part => part.Name).Should().Equal("tests.everywhere");

        var onPostgres = new Recipe { Kitchen = "postgres" };
        parts.ApplyTo(onPostgres, provider).Select(part => part.Name).Should().Equal("tests.everywhere", "tests.postgres-only");

        // What a part throws comes out of the call as it is, so its message is what the developer reads.
        services.AddContextPart(Part(
            "tests.asks-the-kitchen",
            300,
            recipe => recipe.Kitchen is { } kitchen ? kitchen == "postgres" : throw new InvalidOperationException("No kitchen yet: say which first.")));
        FluentActions.Invoking(() => parts.ApplyTo(new Recipe(), provider)).Should().Throw<InvalidOperationException>().WithMessage("No kitchen yet: say which first.");

        FluentActions.Invoking(() => parts.ApplyTo(null!, provider)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => parts.ApplyTo(new Recipe(), null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_part_has_a_name_of_one_word_and_something_to_apply()
    {
        FluentActions.Invoking(() => new ContextPart<Recipe>("tests.two words", 100, (_, _) => { })).Should().Throw<ArgumentException>()
            .WithMessage("A part's name holds no white space, and 'tests.two words' does: it is written in code and in logs as one word.*");
        FluentActions.Invoking(() => new ContextPart<Recipe>(" ", 100, (_, _) => { })).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ContextPart<Recipe>("tests.nothing", 100, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ((IServiceCollection)null!).AddContextPart(Part("tests.any", 1))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection().AddContextPart<Recipe>(null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>A part that writes its own name into the recipe, on every recipe or on those <paramref name="appliesTo"/> says.</summary>
    private static ContextPart<Recipe> Part(string name, int position, Func<Recipe, bool>? appliesTo = null)
        => new(name, position, (recipe, _) => recipe.Steps.Add(name)) { AppliesTo = appliesTo };

    /// <summary>What the parts are applied to here: a builder that remembers what was added to it, and where it is.</summary>
    private sealed class Recipe
    {
        public string? Kitchen { get; init; }

        public List<string> Steps { get; } = [];
    }
}
