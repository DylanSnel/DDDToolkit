using DDDToolkit.BaseTypes;
using FluentValidation;

namespace DDDToolkit.FluentValidation;

/// <summary>
/// Composes DDDToolkit value objects into a FluentValidation validator you write yourself.
/// <para>
/// A value object already validates itself: <c>[ValueObject]</c> and <c>[SingleValueObject&lt;T&gt;]</c>
/// generate a nested <c>Validator</c>, an <c>Errors</c> collection and the <c>IsValid</c> that runs them.
/// That happens in-process with no registration, which is why this package has no DI entry point. What
/// it cannot do on its own is take part in a <i>containing</i> validator — the one you write for a
/// command or a request DTO. That is what <see cref="MustBeValid{T, TValueObject}"/> is for:
/// </para>
/// <code>
/// public sealed class PlaceOrderValidator : AbstractValidator&lt;PlaceOrder&gt;
/// {
///     public PlaceOrderValidator()
///     {
///         RuleFor(x =&gt; x.Email).NotNull().MustBeValid();
///         RuleFor(x =&gt; x.Quantity).GreaterThan(0);
///     }
/// }
/// </code>
/// <para>
/// The failure is reported against the containing property, so the caller gets one flat result rather
/// than having to catch <c>InvalidValueObjectException</c> per field. Use it when a refusal must become
/// a 400 rather than a 500.
/// </para>
/// </summary>
public static class ValueObjectRules
{
    /// <summary>The error code the generated failure carries, so callers can branch on it.</summary>
    public const string ErrorCode = "ValueObjectValidator";

    /// <summary>
    /// Fails when the value object does not satisfy its own generated validator. A <see langword="null"/>
    /// property passes — chain <c>NotNull()</c> before this when the value is required, exactly as with
    /// FluentValidation's own rules.
    /// </summary>
    /// <typeparam name="T">The type being validated by the containing validator.</typeparam>
    /// <typeparam name="TValueObject">The value object type held by the property.</typeparam>
    /// <param name="ruleBuilder">The rule builder returned by <c>RuleFor</c>.</param>
    /// <returns>The rule builder, so <c>WithMessage</c> and friends still chain.</returns>
    public static IRuleBuilderOptions<T, TValueObject> MustBeValid<T, TValueObject>(
        this IRuleBuilder<T, TValueObject> ruleBuilder)
        where TValueObject : ValueObject?
    {
        ArgumentNullException.ThrowIfNull(ruleBuilder);

        return ruleBuilder
            .Must(static value => value is null || value.IsValid)
            .WithErrorCode(ErrorCode)
            .WithMessage("'{PropertyName}' is not a valid " + typeof(TValueObject).Name + ".");
    }
}
