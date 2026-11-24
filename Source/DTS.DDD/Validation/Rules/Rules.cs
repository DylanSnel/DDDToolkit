namespace DTS.DDD.Validation;

/// <summary>
/// The base class of the nested <c>Validator</c> of a value object. You write the rules in its constructor:
/// <code>
/// [SingleValueObject&lt;string&gt;]
/// public partial record EmailAddress
/// {
///     partial class Validator
///     {
///         public Validator() =&gt; RuleFor(x =&gt; x.Value).NotEmpty().EmailAddress();
///     }
/// }
/// </code>
/// <para>
/// The generator reads the constructor during the build and writes each rule as an <c>if</c> statement in the
/// <c>Validate()</c> overrides of the value object. No code creates a <c>Validator</c> at run time, so the
/// members of this class do nothing. They exist so that the rules compile and the IDE completes them.
/// </para>
/// <para>
/// The names follow FluentValidation: <c>RuleFor</c>, <c>RuleForEach</c>, <c>When</c>, <c>Unless</c> and the
/// rules in <see cref="RuleChainExtensions"/>.
/// </para>
/// </summary>
/// <typeparam name="T">The value object the rules are about.</typeparam>
public abstract class Rules<T>
{
    /// <summary>For the generated part of a nested <c>Validator</c>.</summary>
    protected Rules()
    {
    }

    /// <summary>Starts the rules for one property, such as <c>RuleFor(x =&gt; x.Street)</c>.</summary>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="property">A lambda that reads the property from the value object.</param>
    /// <returns>A chain to add rules to.</returns>
    protected IRuleChain<T, TProperty> RuleFor<TProperty>(Func<T, TProperty> property) => null!;

    /// <summary>
    /// Starts the rules for each item of a collection, such as <c>RuleForEach(x =&gt; x.Lines)</c>. A failure names
    /// the item by its index, such as <c>Lines[2]</c>.
    /// </summary>
    /// <typeparam name="TItem">The type of an item.</typeparam>
    /// <param name="collection">A lambda that reads the collection from the value object.</param>
    /// <returns>A chain to add rules to, which each item runs through.</returns>
    protected IRuleChain<T, TItem> RuleForEach<TItem>(Func<T, IEnumerable<TItem>?> collection) => null!;

    /// <summary>Runs the rules in <paramref name="rules"/> only when <paramref name="condition"/> is true.</summary>
    /// <param name="condition">A lambda over the value object, such as <c>x =&gt; x.IsBusiness</c>.</param>
    /// <param name="rules">A lambda with the <c>RuleFor</c> calls of the group.</param>
    protected void When(Func<T, bool> condition, Action rules)
    {
    }

    /// <summary>Runs the rules in <paramref name="rules"/> only when <paramref name="condition"/> is false.</summary>
    /// <param name="condition">A lambda over the value object.</param>
    /// <param name="rules">A lambda with the <c>RuleFor</c> calls of the group.</param>
    protected void Unless(Func<T, bool> condition, Action rules)
    {
    }
}

/// <summary>
/// The rules of one property, as <see cref="Rules{T}.RuleFor{TProperty}"/> returns them. The rules are extension
/// methods in <see cref="RuleChainExtensions"/>. The generator reads the calls, and no code calls them at run time.
/// <para>
/// <typeparamref name="TProperty"/> is covariant, so a chain over <c>string</c> takes the text rules, which are
/// declared over <c>string?</c>, without a nullability warning.
/// </para>
/// </summary>
/// <typeparam name="T">The value object the rules are about.</typeparam>
/// <typeparam name="TProperty">The type of the property.</typeparam>
public interface IRuleChain<T, out TProperty>
{
}
