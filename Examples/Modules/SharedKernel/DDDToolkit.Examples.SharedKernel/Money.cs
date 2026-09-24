using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Validation;

namespace DDDToolkit.Examples.SharedKernel;

/// <summary>
/// An amount in a currency. The one type every module of the shop means the same thing by: Catalog sets
/// prices in it, Ordering adds them up, Payments charges it.
/// </summary>
/// <remarks>
/// A positional record, because it has little behaviour and two parts. The generator declares both
/// properties itself as <c>protected init</c>, which is what keeps <c>with</c> closed to callers; use the
/// generated <c>With(...)</c> instead. See docs/value-objects.md, "Positional records".
/// <para>
/// It lives in a shared kernel rather than in one module because a module that published it would own
/// it, and every other module would then depend on that module's contracts to count. Contracts still
/// carry the two parts as a <see langword="decimal"/> and a <see langword="string"/>: a message on the
/// wire should not depend on a type anyone may change.
/// </para>
/// </remarks>
[ValueObject]
public partial record Money(decimal Amount, string Currency)
{
    /// <summary>The currency this example sells in. One currency keeps totals honest without an exchange rate.</summary>
    public const string Euro = "EUR";

    /// <summary>Nothing, in <paramref name="currency"/>. What a sum of no lines comes to.</summary>
    public static Money Zero(string currency = Euro) => new(0m, currency);

    /// <summary>Adds two amounts. Adding euros to dollars is a bug, not a conversion, so it throws.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public Money Plus(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.");
        }

        return With(amount: Amount + other.Amount);
    }

    /// <summary>This amount, <paramref name="quantity"/> times.</summary>
    public Money Times(int quantity) => With(amount: Amount * quantity);

    public override string ToString() => $"{Amount:0.00} {Currency}";

    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (Amount < 0)
        {
            errors.Add("An amount cannot be negative.", nameof(Amount), "Negative", Amount);
        }

        if (Currency is not { Length: 3 } || !Currency.All(char.IsAsciiLetterUpper))
        {
            errors.Add("A currency is three capital letters, such as EUR.", nameof(Currency), "Malformed", Currency);
        }
    }
}
