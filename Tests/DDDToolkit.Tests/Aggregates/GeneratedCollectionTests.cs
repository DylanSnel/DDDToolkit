using System.Collections.ObjectModel;
using System.Reflection;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// A get-only <c>partial IReadOnlyList&lt;T&gt;</c> on an entity becomes a private backing field plus a
/// read-only view. These pin the half of that contract callers depend on: the view tracks the field,
/// and it cannot be cast back to the mutable collection.
/// </summary>
public class GeneratedCollectionTests
{
    private static Basket NewBasket() => new(BasketId.CreateUnique(), "ada");

    private static BasketLine Line(int id, string sku = "A", int quantity = 1)
        => new(BasketLineId.Create(id), Sku.Create(sku), quantity);

    // ---------------------------------------------------------------- IReadOnlyList

    [Fact]
    public void TheViewTracksAdditionsAndRemovals()
    {
        var basket = NewBasket();
        var view = basket.Lines;

        view.Should().BeEmpty();

        basket.AddLine(Line(1));
        view.Should().ContainSingle("the view is a window onto the backing list, not a copy");

        basket.AddLine(Line(2));
        view.Select(l => l.Id).Should().Equal(BasketLineId.Create(1), BasketLineId.Create(2));

        basket.RemoveLine(BasketLineId.Create(1)).Should().BeTrue();
        view.Select(l => l.Id).Should().Equal(BasketLineId.Create(2));
    }

    [Fact]
    public void TheViewIsIndexableAndCounted()
    {
        var basket = NewBasket();
        basket.AddLine(Line(1, "A", 3));
        basket.AddLine(Line(2, "B", 5));

        basket.Lines.Count.Should().Be(2);
        basket.Lines[0].Quantity.Should().Be(3);
        basket.Lines[1].Sku.Should().Be(Sku.Create("B"));
    }

    [Fact]
    public void TheViewIsNotTheBackingList()
    {
        var basket = NewBasket();
        basket.AddLine(Line(1));

        basket.Lines.Should().BeOfType<ReadOnlyCollection<BasketLine>>();
        (basket.Lines as List<BasketLine>).Should().BeNull("casting the view back to a List would reopen the aggregate");
    }

    [Fact]
    public void MutatingThroughTheViewThrows()
    {
        var basket = NewBasket();

        var add = () => ((IList<BasketLine>)basket.Lines).Add(Line(9));
        var clear = () => ((IList<BasketLine>)basket.Lines).Clear();

        add.Should().Throw<NotSupportedException>();
        clear.Should().Throw<NotSupportedException>();
        basket.Lines.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- IReadOnlySet

    [Fact]
    public void TheSetVariantDeduplicates()
    {
        var basket = NewBasket();
        var tag = TagId.CreateUnique();
        var other = TagId.CreateUnique();

        basket.Tag(tag);
        basket.Tag(tag);
        basket.Tag(other);

        basket.Tags.Should().HaveCount(2);
        basket.Tags.Should().Contain(tag).And.Contain(other);
        basket.Tags.Contains(tag).Should().BeTrue("IReadOnlySet exposes the set's own lookup");
    }

    [Fact]
    public void TheSetVariantIsAReadOnlySetOverTheField()
    {
        var basket = NewBasket();
        var tag = TagId.CreateUnique();

        var view = basket.Tags;
        basket.Tag(tag);

        view.Should().ContainSingle("ReadOnlySet wraps the live HashSet");
        basket.Tags.Should().BeOfType<ReadOnlySet<TagId>>();
        (basket.Tags as HashSet<TagId>).Should().BeNull();

        var add = () => ((ISet<TagId>)basket.Tags).Add(TagId.CreateUnique());
        add.Should().Throw<NotSupportedException>();
    }

    // ---------------------------------------------------------------- IEnumerable and IReadOnlyCollection

    [Fact]
    public void TheEnumerableVariantEnumeratesInOrder()
    {
        var basket = NewBasket();

        basket.Note("first");
        basket.Note("second");
        basket.Note("first");

        basket.Notes.Should().Equal("first", "second", "first");
        (basket.Notes as List<string>).Should().BeNull();
        basket.Notes.Should().BeOfType<ReadOnlyCollection<string>>("an IEnumerable property is list-backed too");
    }

    [Fact]
    public void TheReadOnlyCollectionVariantCounts()
    {
        var basket = NewBasket();

        basket.Label("x");
        basket.Label("y");

        basket.Labels.Count.Should().Be(2);
        basket.Labels.Should().Equal("x", "y");
        (basket.Labels as List<string>).Should().BeNull();
    }

    // ---------------------------------------------------------------- child entities

    [Fact]
    public void ChildEntitiesGetTheSameTreatment()
    {
        // The generator runs for [Entity] exactly as for [AggregateRoot]; nothing about collections is
        // root-specific.
        var line = Line(1);
        var view = line.Adjustments;

        line.Adjust("price corrected");

        view.Should().ContainSingle();
        line.Adjustments.Should().BeOfType<ReadOnlyCollection<string>>();
        (line.Adjustments as List<string>).Should().BeNull();
    }

    // ---------------------------------------------------------------- the backing field

    [Theory]
    [InlineData("_lines", typeof(List<BasketLine>))]
    [InlineData("_tags", typeof(HashSet<TagId>))]
    [InlineData("_notes", typeof(List<string>))]
    [InlineData("_labels", typeof(List<string>))]
    public void TheBackingFieldIsPrivateReadonlyAndCorrectlyTyped(string fieldName, Type expectedType)
    {
        var field = typeof(Basket).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull($"the generator names the field after the property as {fieldName}");
        field!.IsPrivate.Should().BeTrue();
        field.IsInitOnly.Should().BeTrue("readonly: the entity mutates the collection, never replaces it");
        field.FieldType.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(nameof(Basket.Lines))]
    [InlineData(nameof(Basket.Tags))]
    [InlineData(nameof(Basket.Notes))]
    [InlineData(nameof(Basket.Labels))]
    public void TheCollectionPropertyHasNoSetter(string propertyName)
    {
        var property = typeof(Basket).GetProperty(propertyName);

        property.Should().NotBeNull();
        property!.CanWrite.Should().BeFalse("a collection property is a get-only view");
        property.CanRead.Should().BeTrue();
    }
}
