using System.Reflection;
using DDDToolkit.Exceptions;
using DDDToolkit.Interfaces;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

/// <summary>
/// <c>ISingleValue&lt;TSelf, TValue&gt;</c>: every generated id, single value object and always-valid twin names its
/// value and the way back from it, so code that does not know the type, such as Entity Framework's
/// <c>SingleValueConverter</c> in a project that does not declare it, can store it and read it back.
/// <para>
/// Each test goes through <see cref="RoundTrip{T, TValue}"/>, which knows nothing of the type but the interface,
/// exactly as that code does. <c>FromValue</c> is implemented explicitly, so it adds nothing to what a caller
/// sees on the type.
/// </para>
/// </summary>
public class SingleValueTests
{
    private static T RoundTrip<T, TValue>(T single)
        where T : ISingleValue<T, TValue>
        => T.FromValue(single.Value);

    private static T Read<T, TValue>(TValue value)
        where T : ISingleValue<T, TValue>
        => T.FromValue(value);

    [Fact]
    public void An_id_implements_ISingleValue_explicitly_and_round_trips()
    {
        var id = BasketId.CreateUnique();

        typeof(ISingleValue<BasketId, Guid>).IsAssignableFrom(id.GetType()).Should().BeTrue();
        RoundTrip<BasketId, Guid>(id).Should().Be(id);
        Read<BasketId, Guid>(id.Value).Value.Should().Be(id.Value);

        typeof(BasketId).GetMethod("FromValue", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull("FromValue is for reconstitution, so it is implemented explicitly and adds nothing to the id's members");
    }

    [Fact]
    public void Ids_of_every_value_round_trip()
    {
        var line = BasketLineId.Create(42);
        var sku = Sku.Create("SKU-1");

        RoundTrip<BasketLineId, int>(line).Should().Be(line);
        RoundTrip<Sku, string>(sku).Should().Be(sku);
    }

    [Fact]
    public void A_class_id_implements_ISingleValue_explicitly_and_round_trips()
    {
        var id = LedgerId.CreateUnique();

        typeof(ISingleValue<LedgerId, Guid>).IsAssignableFrom(id.GetType()).Should().BeTrue();
        var read = RoundTrip<LedgerId, Guid>(id);
        read.Should().Be(id);
        read.Should().BeOfType<LedgerId>("the plain type is read back as itself, not as its twin");

        typeof(LedgerId).GetMethod("FromValue", BindingFlags.Public | BindingFlags.Static).Should().BeNull();
    }

    [Fact]
    public void A_class_ids_twin_implements_ISingleValue_of_itself_and_round_trips()
    {
        var twin = LedgerId.CreateUnique().ToValid();

        typeof(ISingleValue<ValidLedgerId, Guid>).IsAssignableFrom(twin.GetType()).Should().BeTrue();
        var read = RoundTrip<ValidLedgerId, Guid>(twin);
        read.Should().Be(twin);
        read.Should().BeOfType<ValidLedgerId>();
    }

    [Fact]
    public void A_single_value_object_implements_ISingleValue_explicitly_and_round_trips()
    {
        var slug = Slug.Create("summer-sale");

        typeof(ISingleValue<Slug, string>).IsAssignableFrom(slug.GetType()).Should().BeTrue();
        RoundTrip<Slug, string>(slug).Should().Be(slug);
        typeof(Slug).GetMethod("FromValue", BindingFlags.Public | BindingFlags.Static).Should().BeNull();
    }

    [Fact]
    public void The_twin_of_a_single_value_object_validates_what_it_reads_back_and_the_plain_type_does_not()
    {
        var twin = Slug.Create("summer-sale").ToValid();
        RoundTrip<ValidSlug, string>(twin).Should().Be(twin);

        // The plain type is "not yet checked" by design, as a value read from a column always was; the twin is
        // always valid, so reading an invalid value back into it refuses, whoever calls FromValue.
        Read<Slug, string>("Not A Slug").Value.Should().Be("Not A Slug");
        FluentActions.Invoking(() => Read<ValidSlug, string>("Not A Slug"))
            .Should().Throw<InvalidValueObjectException>("the twin reads back through its public constructor, which validates");
    }
}
