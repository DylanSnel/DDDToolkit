using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Validation;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The value object the API boundary validates. These are the rules the endpoint turns into a 400.
/// </summary>
public class AddressTests
{
    [Fact]
    public void A_well_formed_address_converts_to_its_always_valid_twin()
    {
        var address = new Address("Oudegracht 1", "Utrecht", "3511 AA");

        address.TryToValid(out var valid, out var errors).Should().BeTrue();
        valid.Should().NotBeNull();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void A_bad_one_reports_every_field_at_once_and_throws_nothing()
    {
        var address = new Address(string.Empty, "Utrecht", "nope");

        address.TryToValid(out var valid, out var errors).Should().BeFalse();
        valid.Should().BeNull();

        // Branch on Code, never on the message: the message is for the user and may be rewritten.
        errors.Should().BeEquivalentTo(
            [
                new { PropertyName = "Street", Code = "Required" },
                new { PropertyName = "PostalCode", Code = "Malformed" },
            ],
            options => options.ExcludingMissingMembers());
    }

    [Fact]
    public void The_failures_shape_themselves_into_the_response_a_client_reads()
    {
        new Address(string.Empty, "Utrecht", "nope").TryToValid(out _, out var errors);

        var problem = errors.Prefixed("shipTo").ToErrorDictionary();

        problem.Should().ContainKey("shipTo.Street");
        problem.Should().ContainKey("shipTo.PostalCode");
    }

    [Fact]
    public void ToValid_throws_because_some_callers_want_it_to()
    {
        var address = new Address(string.Empty, string.Empty, string.Empty);

        Assert.Throws<InvalidValueObjectException>(() => address.ToValid());
    }

    [Fact]
    public void Two_addresses_with_the_same_contents_are_the_same_address()
    {
        new Address("Oudegracht 1", "Utrecht", "3511 AA")
            .Should().Be(new Address("Oudegracht 1", "Utrecht", "3511 AA"));
    }
}
