using System.Text.Json;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Validation;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>The shared kernel's one type. Every module adds, multiplies and stores it.</summary>
public class MoneyTests
{
    [Fact]
    public void Amounts_in_one_currency_add_up()
        => new Money(2.50m, "EUR").Times(3).Plus(new Money(1m, "EUR")).Should().Be(new Money(8.50m, "EUR"));

    [Fact]
    public void Adding_another_currency_is_a_bug_and_throws()
    {
        var mixing = () => new Money(1m, "EUR").Plus(new Money(1m, "USD"));

        mixing.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_negative_amount_and_a_made_up_currency_are_both_reported()
    {
        new Money(-1m, "euro").TryToValid(out _, out var errors).Should().BeFalse();

        errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Amount", "Currency");
    }

    /// <summary>
    /// Domain events carrying Money are stored in the outbox as JSON and read back before they are
    /// published, so a Money that did not survive the round trip would publish a price of nothing.
    /// </summary>
    [Fact]
    public void It_survives_the_outbox_round_trip()
    {
        var price = new Money(12.50m, "EUR");

        var read = JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(price));

        read.Should().Be(price);
    }
}
