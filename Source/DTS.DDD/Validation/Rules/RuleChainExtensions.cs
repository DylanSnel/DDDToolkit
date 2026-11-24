using System.Text.RegularExpressions;

namespace DTS.DDD.Validation;

/// <summary>
/// The rules of a nested <c>Validator</c>, as extension methods on <see cref="IRuleChain{T, TProperty}"/>. The generator
/// reads the calls. The methods do nothing at run time.
/// <para>
/// The text, comparison and enum rules let null pass, as in FluentValidation. Put <c>NotNull()</c> or <c>NotEmpty()</c> before them
/// when the value is required.
/// </para>
/// </summary>
public static class RuleChainExtensions
{
    /// <summary>
    /// Runs the rules before this call only when <paramref name="condition"/> is true. A second <c>When</c> later in
    /// the chain applies to every rule before it, this one included.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="condition">A lambda over the value object, such as <c>x =&gt; x.Country == "NL"</c>.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> When<T, TProperty>(this IRuleChain<T, TProperty> chain, Func<T, bool> condition) => chain;

    /// <summary>Runs the rules before this call only when <paramref name="condition"/> is false.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="condition">A lambda over the value object.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Unless<T, TProperty>(this IRuleChain<T, TProperty> chain, Func<T, bool> condition) => chain;

    /// <summary>
    /// Replaces the failure of the rule before this call with your own error record. The failure takes the code,
    /// the message and the arguments of the record. Its property name stays the one of <c>RuleFor</c>.
    /// <code>
    /// RuleFor(x =&gt; x.PostalCode).NotEmpty().WithError(x =&gt; new PostalCodeRequired(x.Country));
    /// </code>
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <typeparam name="TError">An error record with <c>[ErrorCode]</c> that derives from <see cref="Results.InvalidError"/>.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="error">A lambda that creates the error. It runs only when the rule fails.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> WithError<T, TProperty, TError>(this IRuleChain<T, TProperty> chain, Func<T, TError> error)
        where TError : Results.InvalidError
        => chain;

    /// <summary>Fails when <paramref name="predicate"/> returns false. The code is <c>ddd.must</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="predicate">A lambda over the value of the property.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Must<T, TProperty>(this IRuleChain<T, TProperty> chain, Func<TProperty, bool> predicate) => chain;

    /// <summary>Fails when <paramref name="predicate"/> returns false. The lambda gets the value object and the value.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="predicate">A lambda over the value object and the value of the property.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Must<T, TProperty>(this IRuleChain<T, TProperty> chain, Func<T, TProperty, bool> predicate) => chain;

    /// <summary>Fails when the value is null. The code is <c>ddd.not-null</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> NotNull<T, TProperty>(this IRuleChain<T, TProperty> chain) => chain;

    /// <summary>Fails when the value is not null. The code is <c>ddd.null</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Null<T, TProperty>(this IRuleChain<T, TProperty> chain) => chain;

    /// <summary>
    /// Fails when the value is null, an empty or white-space string, an empty collection or the default value of a
    /// struct. The code is <c>ddd.not-empty</c>.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> NotEmpty<T, TProperty>(this IRuleChain<T, TProperty> chain) => chain;

    /// <summary>Fails when <c>NotEmpty()</c> would pass. The code is <c>ddd.empty</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Empty<T, TProperty>(this IRuleChain<T, TProperty> chain) => chain;

    /// <summary>Fails when the value differs from <paramref name="value"/>. The code is <c>ddd.equal</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> Equal<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value) => chain;

    /// <summary>Fails when the value equals <paramref name="value"/>. The code is <c>ddd.not-equal</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> NotEqual<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value) => chain;

    /// <summary>Fails when the text has fewer than <paramref name="min"/> or more than <paramref name="max"/> characters. The code is <c>ddd.length</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="min">The lowest length.</param>
    /// <param name="max">The highest length.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> Length<T>(this IRuleChain<T, string?> chain, int min, int max) => chain;

    /// <summary>Fails when the text does not have <paramref name="exact"/> characters. The code is <c>ddd.exact-length</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="exact">The length.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> Length<T>(this IRuleChain<T, string?> chain, int exact) => chain;

    /// <summary>Fails when the text has fewer than <paramref name="min"/> characters. The code is <c>ddd.minimum-length</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="min">The lowest length.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> MinimumLength<T>(this IRuleChain<T, string?> chain, int min) => chain;

    /// <summary>Fails when the text has more than <paramref name="max"/> characters. The code is <c>ddd.maximum-length</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="max">The highest length.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> MaximumLength<T>(this IRuleChain<T, string?> chain, int max) => chain;

    /// <summary>
    /// Fails when the text does not match <paramref name="pattern"/>. The code is <c>ddd.matches</c>. The pattern must
    /// be a constant. The generator compiles it once into a static <see cref="Regex"/>.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="pattern">A constant regular expression.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> Matches<T>(this IRuleChain<T, string?> chain, string pattern) => chain;

    /// <summary>
    /// Fails when the text does not match <paramref name="regex"/>. The code is <c>ddd.matches</c>. Pass a
    /// <c>[GeneratedRegex]</c> method, such as <c>Matches(PostalCodePattern())</c>, or a static field.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="regex">The regular expression.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> Matches<T>(this IRuleChain<T, string?> chain, Regex regex) => chain;

    /// <summary>
    /// Fails when the text has no <c>@</c>, has more than one, or has it first or last. The code is
    /// <c>ddd.email-address</c>. FluentValidation checks the same.
    /// </summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, string?> EmailAddress<T>(this IRuleChain<T, string?> chain) => chain;

    /// <summary>Fails unless the value is greater than <paramref name="value"/>. The code is <c>ddd.greater-than</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> GreaterThan<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value)
        where TProperty : IComparable<TProperty> => chain;

    /// <summary>Fails unless the value is greater than or equal to <paramref name="value"/>. The code is <c>ddd.greater-than-or-equal</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> GreaterThanOrEqualTo<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value)
        where TProperty : IComparable<TProperty> => chain;

    /// <summary>Fails unless the value is less than <paramref name="value"/>. The code is <c>ddd.less-than</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> LessThan<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value)
        where TProperty : IComparable<TProperty> => chain;

    /// <summary>Fails unless the value is less than or equal to <paramref name="value"/>. The code is <c>ddd.less-than-or-equal</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The value to compare with: a constant or a static member.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> LessThanOrEqualTo<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty value)
        where TProperty : IComparable<TProperty> => chain;

    /// <summary>Fails unless <paramref name="from"/> &lt;= value &lt;= <paramref name="to"/>. The code is <c>ddd.inclusive-between</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="from">The lowest value.</param>
    /// <param name="to">The highest value.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> InclusiveBetween<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty from, TProperty to)
        where TProperty : IComparable<TProperty> => chain;

    /// <summary>Fails unless <paramref name="from"/> &lt; value &lt; <paramref name="to"/>. The code is <c>ddd.exclusive-between</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TProperty">The type of the property.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <param name="from">The value just below the lowest allowed value.</param>
    /// <param name="to">The value just above the highest allowed value.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TProperty> ExclusiveBetween<T, TProperty>(this IRuleChain<T, TProperty> chain, TProperty from, TProperty to)
        where TProperty : IComparable<TProperty> => chain;

    /// <inheritdoc cref="GreaterThan{T, TProperty}(RuleChain{T, TProperty}, TProperty)"/>
    public static IRuleChain<T, TProperty?> GreaterThan<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty value)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <inheritdoc cref="GreaterThanOrEqualTo{T, TProperty}(RuleChain{T, TProperty}, TProperty)"/>
    public static IRuleChain<T, TProperty?> GreaterThanOrEqualTo<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty value)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <inheritdoc cref="LessThan{T, TProperty}(RuleChain{T, TProperty}, TProperty)"/>
    public static IRuleChain<T, TProperty?> LessThan<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty value)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <inheritdoc cref="LessThanOrEqualTo{T, TProperty}(RuleChain{T, TProperty}, TProperty)"/>
    public static IRuleChain<T, TProperty?> LessThanOrEqualTo<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty value)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <inheritdoc cref="InclusiveBetween{T, TProperty}(RuleChain{T, TProperty}, TProperty, TProperty)"/>
    public static IRuleChain<T, TProperty?> InclusiveBetween<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty from, TProperty to)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <inheritdoc cref="ExclusiveBetween{T, TProperty}(RuleChain{T, TProperty}, TProperty, TProperty)"/>
    public static IRuleChain<T, TProperty?> ExclusiveBetween<T, TProperty>(this IRuleChain<T, TProperty?> chain, TProperty from, TProperty to)
        where TProperty : struct, IComparable<TProperty> => chain;

    /// <summary>Fails when the value is no named member of its enum. The code is <c>ddd.enum</c>.</summary>
    /// <typeparam name="T">The value object.</typeparam>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="chain">The chain.</param>
    /// <returns>The same chain.</returns>
    public static IRuleChain<T, TEnum> IsInEnum<T, TEnum>(this IRuleChain<T, TEnum> chain)
        where TEnum : struct, Enum => chain;

    /// <inheritdoc cref="IsInEnum{T, TEnum}(RuleChain{T, TEnum})"/>
    public static IRuleChain<T, TEnum?> IsInEnum<T, TEnum>(this IRuleChain<T, TEnum?> chain)
        where TEnum : struct, Enum => chain;
}
