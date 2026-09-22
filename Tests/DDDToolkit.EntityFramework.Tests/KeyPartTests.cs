using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// <c>[KeyPart]</c> and the <c>KeyPartConvention</c>: a composite primary key built from struct ids
/// and their generated value converters, carried into the foreign key of every owned type, and
/// nothing at all changed for a model that does not use it.
/// </summary>
public sealed class KeyPartTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public KeyPartTests() => _db.EnsureCreated(CreateContext);

    public void Dispose() => _db.Dispose();

    private KeyPartContext CreateContext() => new(_db.Options<KeyPartContext>());

    private static string[] Names(IReadOnlyKey? key) => key!.Properties.Select(static p => p.Name).ToArray();

    private static string[] Names(IReadOnlyList<IReadOnlyProperty> properties) => properties.Select(static p => p.Name).ToArray();

    private static IForeignKey Ownership(IModel model, Type ownedType)
        => model.GetEntityTypes().Single(e => e.ClrType == ownedType).GetForeignKeys().Single(static f => f.IsOwnership);

    [Fact]
    public void A_root_with_one_key_part_reports_the_composite_key()
    {
        using var context = CreateContext();
        var survey = context.Model.FindEntityType(typeof(Survey))!;

        Names(survey.FindPrimaryKey()).Should().Equal("RegionId", "Id");
        survey.FindProperty("RegionId")!.GetValueConverter().Should().NotBeNull("the struct id keeps its generated converter inside the key");
        survey.FindProperty("Id")!.GetValueConverter().Should().NotBeNull();
    }

    [Fact]
    public void An_owned_collection_carries_the_key_part_in_its_foreign_key_and_its_key()
    {
        using var context = CreateContext();
        var foreignKey = Ownership(context.Model, typeof(Question));

        Names(foreignKey.Properties).Should().Equal("RegionId", "SurveyId");
        Names(foreignKey.PrincipalKey).Should().Equal("RegionId", "Id");
        foreignKey.Properties[0].IsShadowProperty().Should().BeFalse("the part is the child's own property, not a copy");
        Names(foreignKey.DeclaringEntityType.FindPrimaryKey()).Should().Equal("RegionId", "SurveyId", "Id");
    }

    [Fact]
    public void An_owned_reference_carries_the_key_part_and_shares_the_owners_column()
    {
        using var context = CreateContext();
        var foreignKey = Ownership(context.Model, typeof(Cover));

        Names(foreignKey.Properties).Should().Equal("RegionId", "SurveyId");
        Names(foreignKey.DeclaringEntityType.FindPrimaryKey()).Should().Equal("RegionId", "SurveyId");

        var script = context.Database.GenerateCreateScript();
        script.Should().NotContain("Cover_RegionId", "the owned reference lives in its owner's row and shares its key columns");
    }

    [Fact]
    public void A_root_with_one_key_part_round_trips_with_its_owned_types()
    {
        var regionA = RegionId.CreateUnique();
        var regionB = RegionId.CreateUnique();
        var id = SurveyId.CreateUnique();
        QuestionId withdrawn;

        using (var context = CreateContext())
        {
            var inA = new Survey(regionA, id, "A");
            inA.Ask("Kept?");
            withdrawn = inA.Ask("Withdrawn?").Id;

            // The same id in another region is a different row: only possible because the key is composite.
            var inB = new Survey(regionB, id, "B");
            inB.Ask("Elsewhere?");

            context.Surveys.AddRange(inA, inB);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var inA = context.Surveys.Single(s => s.RegionId == regionA && s.Id == id);
            inA.Title.Should().Be("A");
            inA.Questions.Select(q => q.Text).Should().BeEquivalentTo("Kept?", "Withdrawn?");
            inA.Questions.Should().OnlyContain(q => q.RegionId == regionA);
            inA.Cover.Caption.Should().Be("Cover of A");
            inA.Cover.RegionId.Should().Be(regionA);

            var inB = context.Surveys.Single(s => s.RegionId == regionB && s.Id == id);
            inB.Questions.Should().ContainSingle().Which.Text.Should().Be("Elsewhere?");

            inA.Withdraw(withdrawn);
            inA.Ask("Added?");
            inA.Retitle("A2");
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var inA = context.Surveys.Single(s => s.RegionId == regionA && s.Id == id);
            inA.Title.Should().Be("A2");
            inA.Questions.Select(q => q.Text).Should().BeEquivalentTo("Kept?", "Added?");

            context.Surveys.Remove(inA);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Surveys.Should().ContainSingle().Which.RegionId.Should().Be(regionB);
            _db.CountRows("Question").Should().Be(1, "removing the survey in one region removes only its own questions");
        }
    }

    [Fact]
    public void Two_key_parts_keep_declaration_order()
    {
        using var context = CreateContext();

        Names(context.Model.FindEntityType(typeof(Census))!.FindPrimaryKey()).Should().Equal("Period", "RegionId", "Id");

        var foreignKey = Ownership(context.Model, typeof(Tally));
        Names(foreignKey.Properties).Should().Equal("Period", "RegionId", "CensusId");
        Names(foreignKey.DeclaringEntityType.FindPrimaryKey()).Should().Equal("Period", "RegionId", "CensusId", "Id");
    }

    [Fact]
    public void Two_key_parts_round_trip()
    {
        var region = RegionId.CreateUnique();
        var id = CensusId.CreateUnique();

        using (var context = CreateContext())
        {
            var first = new Census(region, 2025, id);
            first.Count(3);
            var second = new Census(region, 2026, id);
            second.Count(4);
            second.Count(5);
            context.Censuses.AddRange(first, second);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Censuses.Single(c => c.Period == 2025 && c.RegionId == region && c.Id == id).Tallies.Select(t => t.Value).Should().Equal(3);
            context.Censuses.Single(c => c.Period == 2026 && c.RegionId == region && c.Id == id).Tallies.Select(t => t.Value).Should().BeEquivalentTo([4, 5]);
        }
    }

    [Fact]
    public void A_child_whose_key_part_differs_from_its_owners_is_saved_under_the_owners_value()
    {
        // The domain never builds this; it takes reflection. What it pins down is Entity Framework's
        // foreign key fix-up: the child's key part *is* the foreign key, so the owner's value wins.
        var region = RegionId.CreateUnique();
        var survey = new Survey(region, SurveyId.CreateUnique(), "A");
        var question = survey.Ask("Elsewhere?");
        typeof(Question).GetField("<RegionId>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(question, RegionId.CreateUnique());

        using (var context = CreateContext())
        {
            context.Surveys.Add(survey);
            context.SaveChanges();
        }

        question.RegionId.Should().Be(region);
        using (var context = CreateContext())
        {
            context.Surveys.Single(s => s.Id == survey.Id).Questions.Should().ContainSingle().Which.RegionId.Should().Be(region);
        }
    }

    [Fact]
    public void Equality_ignores_key_parts_as_it_ignores_Version()
    {
        var id = SurveyId.CreateUnique();
        var inA = new Survey(RegionId.CreateUnique(), id, "A");
        var inB = new Survey(RegionId.CreateUnique(), id, "B");

        inA.Should().Be(inB, "identity is the id alone; a key part is a storage concern");
        (inA == inB).Should().BeTrue();
        inA.GetHashCode().Should().Be(inB.GetHashCode());
        inA.Should().NotBe(new Survey(inA.RegionId, SurveyId.CreateUnique(), "A"));
    }

    [Fact]
    public void An_owned_type_without_the_key_part_fails_when_the_model_is_built()
    {
        using var context = new MissingKeyPartContext(_db.Options<MissingKeyPartContext>());

        var build = () => context.Model;

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*'Archive' is keyed on 'RegionId'*'Folio'*has no such property*");
    }

    [Fact]
    public void An_explicit_key_in_OnModelCreating_wins_over_the_convention()
    {
        using var context = new ExplicitSurveyKeyContext(_db.Options<ExplicitSurveyKeyContext>());

        Names(context.Model.FindEntityType(typeof(Survey))!.FindPrimaryKey()).Should().Equal("Id");
        Names(Ownership(context.Model, typeof(Question)).Properties).Should().Equal("SurveyId");
    }

    [Fact]
    public void A_model_without_key_parts_is_mapped_exactly_as_before()
    {
        string with, without, withScript, withoutScript;

        using (var context = _db.CreateLibraryContext())
        {
            with = context.Model.ToDebugString(MetadataDebugStringOptions.LongDefault);
            withScript = context.Database.GenerateCreateScript();
        }

        using (var context = new LibraryContextWithoutKeyParts(_db.Options<LibraryContext>()))
        {
            without = context.Model.ToDebugString(MetadataDebugStringOptions.LongDefault);
            withoutScript = context.Database.GenerateCreateScript();
        }

        with.Should().Be(without);
        withScript.Should().Be(withoutScript);
        typeof(Shelf).GetInterfaces().Should().NotContain(typeof(DDDToolkit.Interfaces.IHasKeyParts), "the generator adds nothing to a type without key parts");
    }

    [Fact]
    public void Types_without_key_parts_are_mapped_exactly_as_before_next_to_types_with_them()
    {
        using var with = new MixedKeyPartContext(_db.Options<MixedKeyPartContext>());
        using var without = new MixedContextWithoutKeyParts(_db.Options<MixedKeyPartContext>());

        Names(with.Model.FindEntityType(typeof(Survey))!.FindPrimaryKey()).Should().Equal(["RegionId", "Id"], "the convention is active in this model");

        var unkeyed = with.Model.GetEntityTypes()
            .Where(static e => !typeof(DDDToolkit.Interfaces.IHasKeyParts).IsAssignableFrom(e.ClrType)
                && !typeof(DDDToolkit.Interfaces.IHasKeyParts).IsAssignableFrom(e.FindOwnership()?.PrincipalEntityType.ClrType ?? typeof(object)))
            .Select(static e => e.Name)
            .ToList();
        unkeyed.Should().Contain([typeof(Shelf).FullName!, typeof(Book).FullName!, typeof(Note).FullName!, typeof(Person).FullName!]);

        foreach (var name in unkeyed)
        {
            with.Model.FindEntityType(name)!.ToDebugString(MetadataDebugStringOptions.LongDefault)
                .Should().Be(without.Model.FindEntityType(name)!.ToDebugString(MetadataDebugStringOptions.LongDefault), name + " has no key parts");
        }
    }
}
