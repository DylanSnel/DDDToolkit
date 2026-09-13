using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

/// <summary>
/// The allocation-free half of HANDOFF 2.2: <c>[EntityId&lt;T&gt;]</c> on a
/// <c>readonly partial record struct</c>. Covers <see cref="CatId"/> from the example library and the
/// three shapes declared in this project — a prefixed Guid, a prefixed string and a prefixed int.
/// </summary>
public class StructEntityIdTests
{
    /// <summary>Reaches the generated <c>IParsable&lt;T&gt;</c> implementation, which is explicit and so
    /// only callable through a constrained generic.</summary>
    private static T ParseThroughIParsable<T>(string text) where T : IParsable<T>
        => T.Parse(text, CultureInfo.InvariantCulture);

    private static bool TryParseThroughIParsable<T>(string? text, [MaybeNullWhen(false)] out T value)
        where T : IParsable<T>
        => T.TryParse(text, CultureInfo.InvariantCulture, out value);

    // ---------------------------------------------------------------- shape

    [Fact]
    public void StructIdsAreValueTypesThatCarryTheirValue()
    {
        typeof(CatId).IsValueType.Should().BeTrue("the point of a struct id is not allocating");
        typeof(BasketId).IsValueType.Should().BeTrue();

        var guid = Guid.NewGuid();
        IEntityId<Guid> id = BasketId.Create(guid);

        id.Value.Should().Be(guid);
        id.Should().BeAssignableTo<IEntityId>();
    }

    [Fact]
    public void StructIdsHaveNoAlwaysValidTwin()
    {
        // Structs cannot inherit, so there is no ValidCatId; validity is guaranteed by construction.
        typeof(CatId).Assembly.GetType("DDDToolkit.ExampleLibrary.Common.ValueObjects.ValidCatId")
            .Should().BeNull();
        typeof(CatId).GetMethod("ToValid").Should().BeNull();
    }

    // ---------------------------------------------------------------- ToString

    [Fact]
    public void UnprefixedIdsStringifyAsTheBareValue()
    {
        var catId = CatId.CreateUnique();

        CatId.IdPrefix.Should().BeEmpty();
        catId.ToString().Should().Be(catId.Value.ToString());

        var tag = TagId.CreateUnique();
        tag.ToString().Should().Be(tag.Value.ToString());
    }

    [Fact]
    public void PrefixedIdsStringifyAsPrefixUnderscoreValue()
    {
        var guid = Guid.Parse("0194f0a0-1111-7000-8000-000000000001");

        BasketId.IdPrefix.Should().Be("BSK");
        BasketId.Create(guid).ToString().Should().Be("BSK_0194f0a0-1111-7000-8000-000000000001");
        BasketLineId.Create(42).ToString().Should().Be("LINE_42");
        Sku.Create("COFFEE").ToString().Should().Be("SKU_COFFEE");
    }

    [Fact]
    public void StringificationIsCultureInvariant()
    {
        // ValueToString goes through InvariantCulture, so an id reads the same on every machine.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            BasketLineId.Create(-1234).ToString().Should().Be("LINE_-1234");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ---------------------------------------------------------------- Parse and TryParse

    [Fact]
    public void ParseRoundTripsItsOwnToString()
    {
        var basketId = BasketId.CreateUnique();
        var lineId = BasketLineId.Create(7);
        var sku = Sku.Create("COFFEE");
        var catId = CatId.CreateUnique();

        BasketId.Parse(basketId.ToString()).Should().Be(basketId);
        BasketLineId.Parse(lineId.ToString()).Should().Be(lineId);
        Sku.Parse(sku.ToString()).Should().Be(sku);
        CatId.Parse(catId.ToString()).Should().Be(catId);
    }

    [Fact]
    public void ThePrefixIsOptionalOnTheWayIn()
    {
        var guid = Guid.NewGuid();

        BasketId.Parse(guid.ToString()).Should().Be(BasketId.Create(guid));
        BasketId.Parse("BSK_" + guid).Should().Be(BasketId.Create(guid));
        BasketLineId.Parse("42").Should().Be(BasketLineId.Create(42));
        BasketLineId.Parse("LINE_42").Should().Be(BasketLineId.Create(42));
    }

    [Fact]
    public void TryParseReportsFailureInsteadOfThrowing()
    {
        BasketId.TryParse("not a guid", out var basketId).Should().BeFalse();
        basketId.Should().Be(BasketId.Empty);

        BasketLineId.TryParse("LINE_not-a-number", out var lineId).Should().BeFalse();
        lineId.Should().Be(BasketLineId.Empty);

        BasketId.TryParse(null, out var fromNull).Should().BeFalse();
        fromNull.Should().Be(default(BasketId));
    }

    [Fact]
    public void ParseThrowsFormatExceptionNamingTheType()
    {
        var act = () => BasketId.Parse("not a guid");

        act.Should().Throw<FormatException>()
            .WithMessage("*not a guid*")
            .WithMessage("*BasketId*");
    }

    [Fact]
    public void AWrongPrefixOnANumericIdIsRejected()
    {
        // DECISION (pinned): TryParse strips only its own prefix, then hands the rest to the value
        // type's parser. For Guid/int ids an unexpected prefix therefore fails the parse, which is the
        // behaviour you want: a PersonId string must not silently become a BasketId.
        var guid = Guid.NewGuid();

        BasketId.TryParse("XXX_" + guid, out _).Should().BeFalse("XXX is not this id's prefix");
        BasketId.TryParse("LINE_" + guid, out _).Should().BeFalse("another id's prefix is not stripped either");
        BasketLineId.TryParse("BSK_42", out _).Should().BeFalse();
    }

    [Fact]
    public void PrefixMatchingIsCaseSensitive()
    {
        var guid = Guid.NewGuid();

        BasketId.TryParse("BSK_" + guid, out _).Should().BeTrue();
        BasketId.TryParse("bsk_" + guid, out _).Should().BeFalse("the prefix is compared ordinally");
    }

    [Fact]
    public void AWrongPrefixOnAStringIdBecomesPartOfTheValue()
    {
        // DECISION (pinned, and the sharp edge of string ids): every string is a legal value, so
        // TryParse cannot reject anything. An unexpected prefix is simply kept, and garbage parses
        // successfully. Only the id's own prefix is stripped.
        Sku.Parse("SKU_COFFEE").Value.Should().Be("COFFEE");
        Sku.Parse("XXX_COFFEE").Value.Should().Be("XXX_COFFEE", "an unknown prefix is not a prefix, it is data");
        Sku.Parse("anything at all").Value.Should().Be("anything at all");

        Sku.TryParse("whatever", out var parsed).Should().BeTrue();
        parsed.Value.Should().Be("whatever");

        Sku.TryParse(null, out var fromNull).Should().BeFalse("null is the only input a string id rejects");
        fromNull.Should().Be(Sku.Empty);
    }

    [Fact]
    public void AStringIdStripsOnlyOneLeadingPrefix()
    {
        // Consequence of the above worth knowing: the round trip is still stable, because ToString
        // adds exactly the one prefix that Parse removes.
        var doubled = Sku.Create("SKU_COFFEE");

        doubled.ToString().Should().Be("SKU_SKU_COFFEE");
        Sku.Parse(doubled.ToString()).Should().Be(doubled);
    }

    // ---------------------------------------------------------------- IParsable

    [Fact]
    public void IParsableIsImplementedForEveryParseableId()
    {
        var guid = Guid.NewGuid();

        ParseThroughIParsable<BasketId>("BSK_" + guid).Should().Be(BasketId.Create(guid));
        ParseThroughIParsable<CatId>(guid.ToString()).Should().Be(CatId.Create(guid));
        ParseThroughIParsable<BasketLineId>("LINE_9").Should().Be(BasketLineId.Create(9));
        ParseThroughIParsable<Sku>("SKU_TEA").Should().Be(Sku.Create("TEA"));

        TryParseThroughIParsable<BasketLineId>("LINE_9", out var lineId).Should().BeTrue();
        lineId.Should().Be(BasketLineId.Create(9));

        TryParseThroughIParsable<BasketId>("rubbish", out _).Should().BeFalse();
    }

    [Fact]
    public void IParsableIsExplicitSoItDoesNotCrowdTheType()
    {
        // The public Parse(string) stays the obvious entry point; the two-argument IParsable members
        // are explicit implementations.
        typeof(BasketId).GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            .Should().NotBeNull();
        typeof(BasketId).GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)])
            .Should().BeNull();
    }

    // ---------------------------------------------------------------- Empty and default

    [Fact]
    public void EmptyIsTheDefaultValue()
    {
        BasketId.Empty.Should().Be(default(BasketId));
        BasketId.Empty.IsEmpty.Should().BeTrue();
        BasketId.Empty.Value.Should().Be(Guid.Empty);

        BasketLineId.Empty.IsEmpty.Should().BeTrue();
        BasketLineId.Empty.Value.Should().Be(0);
        BasketLineId.Create(0).IsEmpty.Should().BeTrue("zero is the default int, so it reads as empty");

        CatId.Empty.IsEmpty.Should().BeTrue();
        CatId.CreateUnique().IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ADefaultStringIdHasANullValue()
    {
        // DECISION (pinned): a default string id holds null, and stringifies to just the prefix. Treat
        // `default` as "absent" and check IsEmpty rather than relying on the text.
        var empty = Sku.Empty;

        empty.IsEmpty.Should().BeTrue();
        empty.Value.Should().BeNull();
        empty.ToString().Should().Be("SKU_");
        Sku.Create("COFFEE").IsEmpty.Should().BeFalse();
    }

    // ---------------------------------------------------------------- factories

    [Fact]
    public void CreateUniqueProducesDistinctIds()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => CatId.CreateUnique()).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(CatId.Empty);
    }

    [Fact]
    public void CreateSequentialProducesVersion7Guids()
    {
        var id = CatId.CreateSequential();

        id.Value.Version.Should().Be(7, "time-ordered ids keep clustered indexes happy");
        id.IsEmpty.Should().BeFalse();
        BasketId.CreateSequential().Value.Version.Should().Be(7);
    }

    [Fact]
    public void NonGuidIdsGetNoFactories()
    {
        typeof(BasketLineId).GetMethod("CreateUnique", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull("there is no meaningful random int id");
        typeof(Sku).GetMethod("CreateUnique", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();
        typeof(CatId).GetMethod("CreateUnique", BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull();
    }

    // ---------------------------------------------------------------- equality, ordering, hashing

    [Fact]
    public void EqualityIsByValue()
    {
        var guid = Guid.NewGuid();

        (BasketId.Create(guid) == BasketId.Create(guid)).Should().BeTrue();
        (BasketId.Create(guid) != BasketId.CreateUnique()).Should().BeTrue();
        BasketId.Create(guid).GetHashCode().Should().Be(BasketId.Create(guid).GetHashCode());
    }

    [Fact]
    public void CompareToOrdersByTheUnderlyingValue()
    {
        BasketLineId.Create(1).CompareTo(BasketLineId.Create(2)).Should().BeNegative();
        BasketLineId.Create(2).CompareTo(BasketLineId.Create(1)).Should().BePositive();
        BasketLineId.Create(2).CompareTo(BasketLineId.Create(2)).Should().Be(0);

        var left = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var right = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Math.Sign(CatId.Create(left).CompareTo(CatId.Create(right)))
            .Should().Be(Math.Sign(Comparer<Guid>.Default.Compare(left, right)));
    }

    [Fact]
    public void OrderByUsesTheGeneratedComparison()
    {
        var ids = new[] { BasketLineId.Create(3), BasketLineId.Create(1), BasketLineId.Create(2) };

        ids.OrderBy(id => id).Should().Equal(
            BasketLineId.Create(1),
            BasketLineId.Create(2),
            BasketLineId.Create(3));

        ids.OrderByDescending(id => id).Select(id => id.Value).Should().Equal(3, 2, 1);
    }

    [Fact]
    public void IdsWorkAsHashSetMembersAndDictionaryKeys()
    {
        var guid = Guid.NewGuid();
        var id = BasketId.Create(guid);

        var set = new HashSet<BasketId> { id, BasketId.Create(guid), BasketId.CreateUnique() };
        set.Should().HaveCount(2, "two of the three are the same id");
        set.Contains(BasketId.Create(guid)).Should().BeTrue();

        var byId = new Dictionary<BasketId, string> { [id] = "basket" };
        byId[BasketId.Create(guid)].Should().Be("basket");
        byId.ContainsKey(BasketId.CreateUnique()).Should().BeFalse();

        var bySku = new Dictionary<Sku, int> { [Sku.Create("COFFEE")] = 3 };
        bySku[Sku.Create("COFFEE")].Should().Be(3);
    }

    // ---------------------------------------------------------------- conversions

    [Fact]
    public void ExplicitConversionsGoBothWays()
    {
        var guid = Guid.NewGuid();

        ((Guid)BasketId.Create(guid)).Should().Be(guid);
        ((BasketId)guid).Should().Be(BasketId.Create(guid));

        ((int)BasketLineId.Create(11)).Should().Be(11);
        ((BasketLineId)11).Should().Be(BasketLineId.Create(11));

        ((string)Sku.Create("TEA")).Should().Be("TEA");
        ((Sku)"TEA").Should().Be(Sku.Create("TEA"));

        ((Guid)CatId.Create(guid)).Should().Be(guid);
    }

    [Fact]
    public void ConversionsAreExplicitOnPurpose()
    {
        // An implicit conversion would let a raw Guid slip into a method expecting a typed id, which is
        // exactly what the typed id exists to prevent.
        typeof(BasketId).GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull();
        typeof(BasketId).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "op_Explicit")
            .Should().HaveCount(2);
    }
}
