using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DTS.DDD.Validation;

/// <summary>
/// The checks behind the rules of a nested <c>Validator</c>. The generated <c>Validate()</c> of a value object calls
/// them. Each check returns <see langword="true"/> when the value passes, and none of them allocates.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RuleChecks
{
    /// <summary><c>NotNull()</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNull<TProperty>(TProperty value) => value is not null;

    /// <summary><c>NotEmpty()</c> for text: not null, not empty and not only white space.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// <c>NotEmpty()</c> for any other type: not null, not the default value of a struct, and not an empty collection.
    /// </summary>
    public static bool IsNotEmpty<TProperty>(TProperty value)
    {
        if (value is null)
        {
            return false;
        }

        if (typeof(TProperty).IsValueType)
        {
            return !EqualityComparer<TProperty>.Default.Equals(value, default!);
        }

        return value switch
        {
            string text => !string.IsNullOrWhiteSpace(text),
            ICollection collection => collection.Count > 0,
            IEnumerable sequence => HasAny(sequence),
            _ => true,
        };
    }

    /// <summary><c>Length(min, max)</c>, <c>Length(exact)</c>, <c>MinimumLength</c> and <c>MaximumLength</c>. Null passes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasLength(string? value, int min, int max)
        => value is null || (value.Length >= min && value.Length <= max);

    /// <summary><c>Matches</c>. Null passes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Matches(string? value, Regex regex) => value is null || regex.IsMatch(value);

    /// <summary>
    /// <c>EmailAddress()</c>: one <c>@</c>, not first and not last. FluentValidation's default check is the same. Null
    /// passes.
    /// </summary>
    public static bool IsEmailAddress(string? value)
    {
        if (value is null)
        {
            return true;
        }

        var at = value.IndexOf('@');
        return at > 0 && at != value.Length - 1 && at == value.LastIndexOf('@');
    }

    /// <summary><c>Equal</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEqual<TProperty>(TProperty value, TProperty other) => EqualityComparer<TProperty>.Default.Equals(value, other);

    /// <summary><c>GreaterThan</c>. Null passes.</summary>
    public static bool IsGreaterThan<TProperty>(TProperty value, TProperty limit)
        where TProperty : IComparable<TProperty>
        => value is null || value.CompareTo(limit) > 0;

    /// <summary><c>GreaterThanOrEqualTo</c>. Null passes.</summary>
    public static bool IsGreaterThanOrEqualTo<TProperty>(TProperty value, TProperty limit)
        where TProperty : IComparable<TProperty>
        => value is null || value.CompareTo(limit) >= 0;

    /// <summary><c>LessThan</c>. Null passes.</summary>
    public static bool IsLessThan<TProperty>(TProperty value, TProperty limit)
        where TProperty : IComparable<TProperty>
        => value is null || value.CompareTo(limit) < 0;

    /// <summary><c>LessThanOrEqualTo</c>. Null passes.</summary>
    public static bool IsLessThanOrEqualTo<TProperty>(TProperty value, TProperty limit)
        where TProperty : IComparable<TProperty>
        => value is null || value.CompareTo(limit) <= 0;

    /// <summary><c>InclusiveBetween</c>. Null passes.</summary>
    public static bool IsInclusiveBetween<TProperty>(TProperty value, TProperty from, TProperty to)
        where TProperty : IComparable<TProperty>
        => value is null || (value.CompareTo(from) >= 0 && value.CompareTo(to) <= 0);

    /// <summary><c>ExclusiveBetween</c>. Null passes.</summary>
    public static bool IsExclusiveBetween<TProperty>(TProperty value, TProperty from, TProperty to)
        where TProperty : IComparable<TProperty>
        => value is null || (value.CompareTo(from) > 0 && value.CompareTo(to) < 0);

    /// <inheritdoc cref="IsGreaterThan{TProperty}(TProperty, TProperty)"/>
    public static bool IsGreaterThan<TProperty>(TProperty? value, TProperty limit)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || present.CompareTo(limit) > 0;

    /// <inheritdoc cref="IsGreaterThanOrEqualTo{TProperty}(TProperty, TProperty)"/>
    public static bool IsGreaterThanOrEqualTo<TProperty>(TProperty? value, TProperty limit)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || present.CompareTo(limit) >= 0;

    /// <inheritdoc cref="IsLessThan{TProperty}(TProperty, TProperty)"/>
    public static bool IsLessThan<TProperty>(TProperty? value, TProperty limit)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || present.CompareTo(limit) < 0;

    /// <inheritdoc cref="IsLessThanOrEqualTo{TProperty}(TProperty, TProperty)"/>
    public static bool IsLessThanOrEqualTo<TProperty>(TProperty? value, TProperty limit)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || present.CompareTo(limit) <= 0;

    /// <inheritdoc cref="IsInclusiveBetween{TProperty}(TProperty, TProperty, TProperty)"/>
    public static bool IsInclusiveBetween<TProperty>(TProperty? value, TProperty from, TProperty to)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || (present.CompareTo(from) >= 0 && present.CompareTo(to) <= 0);

    /// <inheritdoc cref="IsExclusiveBetween{TProperty}(TProperty, TProperty, TProperty)"/>
    public static bool IsExclusiveBetween<TProperty>(TProperty? value, TProperty from, TProperty to)
        where TProperty : struct, IComparable<TProperty>
        => value is not { } present || (present.CompareTo(from) > 0 && present.CompareTo(to) < 0);

    /// <summary><c>IsInEnum()</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => Enum.IsDefined(value);

    /// <summary><c>IsInEnum()</c> for a nullable enum. Null passes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum
        => value is not { } present || Enum.IsDefined(present);

    private static bool HasAny(IEnumerable sequence)
    {
        var enumerator = sequence.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
