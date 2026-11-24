using System.ComponentModel;
using System.Globalization;
using DTS.DDD.Results;

namespace DTS.DDD.Validation;

/// <summary>
/// The failures of the rules of a nested <c>Validator</c>, each with its standard code, its English message and its
/// arguments. The generated <c>Validate(ValidationErrorBuilder)</c> of a value object calls these only for a rule
/// that failed. <c>DTS.DDD.Localization</c> translates a failure by its code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RuleFailures
{
    /// <summary>The code of <c>NotNull()</c>.</summary>
    public const string NotNullCode = "ddd.not-null";

    /// <summary>The code of <c>Null()</c>.</summary>
    public const string NullCode = "ddd.null";

    /// <summary>The code of <c>NotEmpty()</c>.</summary>
    public const string NotEmptyCode = "ddd.not-empty";

    /// <summary>The code of <c>Empty()</c>.</summary>
    public const string EmptyCode = "ddd.empty";

    /// <summary>The code of <c>Length(min, max)</c>.</summary>
    public const string LengthCode = "ddd.length";

    /// <summary>The code of <c>Length(exact)</c>.</summary>
    public const string ExactLengthCode = "ddd.exact-length";

    /// <summary>The code of <c>MinimumLength</c>.</summary>
    public const string MinimumLengthCode = "ddd.minimum-length";

    /// <summary>The code of <c>MaximumLength</c>.</summary>
    public const string MaximumLengthCode = "ddd.maximum-length";

    /// <summary>The code of <c>Matches</c>.</summary>
    public const string MatchesCode = "ddd.matches";

    /// <summary>The code of <c>EmailAddress()</c>.</summary>
    public const string EmailAddressCode = "ddd.email-address";

    /// <summary>The code of <c>Equal</c>.</summary>
    public const string EqualCode = "ddd.equal";

    /// <summary>The code of <c>NotEqual</c>.</summary>
    public const string NotEqualCode = "ddd.not-equal";

    /// <summary>The code of <c>GreaterThan</c>.</summary>
    public const string GreaterThanCode = "ddd.greater-than";

    /// <summary>The code of <c>GreaterThanOrEqualTo</c>.</summary>
    public const string GreaterThanOrEqualCode = "ddd.greater-than-or-equal";

    /// <summary>The code of <c>LessThan</c>.</summary>
    public const string LessThanCode = "ddd.less-than";

    /// <summary>The code of <c>LessThanOrEqualTo</c>.</summary>
    public const string LessThanOrEqualCode = "ddd.less-than-or-equal";

    /// <summary>The code of <c>InclusiveBetween</c>.</summary>
    public const string InclusiveBetweenCode = "ddd.inclusive-between";

    /// <summary>The code of <c>ExclusiveBetween</c>.</summary>
    public const string ExclusiveBetweenCode = "ddd.exclusive-between";

    /// <summary>The code of <c>IsInEnum()</c>.</summary>
    public const string EnumCode = "ddd.enum";

    /// <summary>The code of <c>Must</c>.</summary>
    public const string MustCode = "ddd.must";

    /// <summary><c>NotNull()</c> failed.</summary>
    public static ValidationError NotNull(string propertyName)
        => new($"'{propertyName}' must not be null.", propertyName, NotNullCode);

    /// <summary><c>Null()</c> failed.</summary>
    public static ValidationError Null(string propertyName, object? value)
        => new($"'{propertyName}' must be null.", propertyName, NullCode, value);

    /// <summary><c>NotEmpty()</c> failed.</summary>
    public static ValidationError NotEmpty(string propertyName, object? value)
        => new($"'{propertyName}' must not be empty.", propertyName, NotEmptyCode, value);

    /// <summary><c>Empty()</c> failed.</summary>
    public static ValidationError Empty(string propertyName, object? value)
        => new($"'{propertyName}' must be empty.", propertyName, EmptyCode, value);

    /// <summary><c>Length(min, max)</c> failed.</summary>
    public static ValidationError Length(string propertyName, string value, int min, int max)
        => new(
            Invariant($"'{propertyName}' must be between {min} and {max} characters. You entered {value.Length} characters."),
            propertyName,
            LengthCode,
            value,
            [new("MinLength", min), new("MaxLength", max), new("TotalLength", value.Length)]);

    /// <summary><c>Length(exact)</c> failed.</summary>
    public static ValidationError ExactLength(string propertyName, string value, int exact)
        => new(
            Invariant($"'{propertyName}' must be {exact} characters long. You entered {value.Length} characters."),
            propertyName,
            ExactLengthCode,
            value,
            [new("MinLength", exact), new("MaxLength", exact), new("TotalLength", value.Length)]);

    /// <summary><c>MinimumLength</c> failed.</summary>
    public static ValidationError MinimumLength(string propertyName, string value, int min)
        => new(
            Invariant($"'{propertyName}' must be at least {min} characters long. You entered {value.Length} characters."),
            propertyName,
            MinimumLengthCode,
            value,
            [new("MinLength", min), new("TotalLength", value.Length)]);

    /// <summary><c>MaximumLength</c> failed.</summary>
    public static ValidationError MaximumLength(string propertyName, string value, int max)
        => new(
            Invariant($"'{propertyName}' must be at most {max} characters long. You entered {value.Length} characters."),
            propertyName,
            MaximumLengthCode,
            value,
            [new("MaxLength", max), new("TotalLength", value.Length)]);

    /// <summary><c>Matches</c> failed.</summary>
    public static ValidationError Matches(string propertyName, string value, string pattern)
        => new($"'{propertyName}' is not in the correct format.", propertyName, MatchesCode, value, [new("RegularExpression", pattern)]);

    /// <summary><c>EmailAddress()</c> failed.</summary>
    public static ValidationError EmailAddress(string propertyName, string value)
        => new($"'{propertyName}' is not a valid email address.", propertyName, EmailAddressCode, value);

    /// <summary><c>Equal</c> failed.</summary>
    public static ValidationError Equal(string propertyName, object? value, object? comparisonValue)
        => Comparison(EqualCode, $"'{propertyName}' must be equal to '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>NotEqual</c> failed.</summary>
    public static ValidationError NotEqual(string propertyName, object? value, object? comparisonValue)
        => Comparison(NotEqualCode, $"'{propertyName}' must not be equal to '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>GreaterThan</c> failed.</summary>
    public static ValidationError GreaterThan(string propertyName, object? value, object? comparisonValue)
        => Comparison(GreaterThanCode, $"'{propertyName}' must be greater than '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>GreaterThanOrEqualTo</c> failed.</summary>
    public static ValidationError GreaterThanOrEqualTo(string propertyName, object? value, object? comparisonValue)
        => Comparison(GreaterThanOrEqualCode, $"'{propertyName}' must be greater than or equal to '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>LessThan</c> failed.</summary>
    public static ValidationError LessThan(string propertyName, object? value, object? comparisonValue)
        => Comparison(LessThanCode, $"'{propertyName}' must be less than '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>LessThanOrEqualTo</c> failed.</summary>
    public static ValidationError LessThanOrEqualTo(string propertyName, object? value, object? comparisonValue)
        => Comparison(LessThanOrEqualCode, $"'{propertyName}' must be less than or equal to '{Text(comparisonValue)}'.", propertyName, value, comparisonValue);

    /// <summary><c>InclusiveBetween</c> failed.</summary>
    public static ValidationError InclusiveBetween(string propertyName, object? value, object? from, object? to)
        => new(
            $"'{propertyName}' must be between {Text(from)} and {Text(to)}. You entered {Text(value)}.",
            propertyName,
            InclusiveBetweenCode,
            value,
            [new("From", from), new("To", to)]);

    /// <summary><c>ExclusiveBetween</c> failed.</summary>
    public static ValidationError ExclusiveBetween(string propertyName, object? value, object? from, object? to)
        => new(
            $"'{propertyName}' must be between {Text(from)} and {Text(to)} (exclusive). You entered {Text(value)}.",
            propertyName,
            ExclusiveBetweenCode,
            value,
            [new("From", from), new("To", to)]);

    /// <summary><c>IsInEnum()</c> failed.</summary>
    public static ValidationError IsInEnum(string propertyName, object? value)
        => new($"'{propertyName}' has a range of values which does not include '{Text(value)}'.", propertyName, EnumCode, value);

    /// <summary><c>Must</c> failed.</summary>
    public static ValidationError Must(string propertyName, object? value)
        => new($"The specified condition was not met for '{propertyName}'.", propertyName, MustCode, value);

    /// <summary>
    /// A rule with <c>.WithError(...)</c> failed. The failure takes the code, the message and the arguments of
    /// <paramref name="error"/>, and the property name of the rule.
    /// </summary>
    public static ValidationError From(InvalidError error, string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new ValidationError(error.Message, propertyName, error.Code, value, error.Arguments);
    }

    /// <summary>The name of an item of <c>RuleForEach</c>, such as <c>Lines[2]</c>.</summary>
    public static string Item(string propertyName, int index) => Invariant($"{propertyName}[{index}]");

    /// <summary>The name of a failure of a nested value object, such as <c>Address.PostalCode</c>.</summary>
    public static string Nested(string propertyName, string? innerName)
        => string.IsNullOrEmpty(innerName) ? propertyName : propertyName + "." + innerName;

    private static ValidationError Comparison(string code, string message, string propertyName, object? value, object? comparisonValue)
        => new(message, propertyName, code, value, [new("ComparisonValue", comparisonValue)]);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}
