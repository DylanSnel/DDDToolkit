using System.Globalization;
using DDDToolkit.Exceptions;
using DDDToolkit.Invariants;
using DDDToolkit.Localization;
using DDDToolkit.Validation;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// What the error filter and the typed errors of a mutation's payload share, so a failure reads the same
/// whichever of the two carries it: which failures an exception stands for, how an argument is written, and
/// who phrases a message.
/// </summary>
internal static class FailureValues
{
    /// <summary>
    /// The failures behind the exception. One constructed without detail still gets the toolkit's own
    /// "is not valid" failure, so the client is never handed an error with no code.
    /// </summary>
    public static IReadOnlyList<ValidationError> Failures(InvalidValueObjectException invalid)
        => invalid.Errors.Count > 0
            ? invalid.Errors
            : [new ValidationError(invalid.Message, code: ValidationError.UnspecifiedCode)
                .With(ValidationError.ValueObjectArgument, invalid.ObjectType.Name)];

    /// <summary>The violations behind the exception, or one carrying its message when it was built from a message alone.</summary>
    public static IReadOnlyList<InvariantViolation> Violations(InvariantViolationException broken)
        => broken.InvariantViolations.Count > 0
            ? broken.InvariantViolations
            : [new InvariantViolation(InvariantViolation.SeamCode, broken.Message, broken.AggregateType, broken.AggregateId)];

    /// <summary>
    /// The arguments as values any GraphQL serializer can write: strings, booleans and numbers as they
    /// are, anything else (an id, a type, a value object) as its text.
    /// </summary>
    public static Dictionary<string, object?> AsExtension(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            result[name] = Plain(value);
        }

        return result;
    }

    /// <summary>
    /// The same arguments as a list a GraphQL type can declare, each value as text, in the order of their
    /// names, so the same failure always lists them the same way.
    /// </summary>
    public static IReadOnlyList<FailureArgument> AsList(IReadOnlyDictionary<string, object?> arguments)
    {
        if (arguments.Count == 0)
        {
            return [];
        }

        var result = new FailureArgument[arguments.Count];
        var index = 0;
        foreach (var (name, value) in arguments)
        {
            result[index++] = new FailureArgument(name, Text(value));
        }

        Array.Sort(result, static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return result;
    }

    /// <summary>One argument as text, or <see langword="null"/> when the failure does not carry it.</summary>
    public static string? TextOf(IReadOnlyDictionary<string, object?> arguments, string name)
        => arguments.TryGetValue(name, out var value) ? Text(value) : null;

    /// <summary>The application's localizer, or <see langword="null"/> when it registered none.</summary>
    public static IFailureLocalizer? Localizer(IResolverContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Services.GetService<IFailureLocalizer>();
    }

    /// <summary>
    /// The text the application keeps under <paramref name="code"/>, or <paramref name="own"/> when it keeps
    /// none. A failure that is neither a refusal, a validation failure nor a violation has no entry of its own
    /// in <see cref="IFailureLocalizer"/>, so it is looked up the way a refusal is: by its code.
    /// </summary>
    public static string UnderCode(IResolverContext context, string code, RefusalKind kind, string own)
        => Localizer(context) is { } localizer
            ? localizer.Localize(new RefusalException(code, kind, own))
            : own;

    private static object? Plain(object? value)
        => value switch
        {
            null or string or bool => value,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            Type type => type.Name,
            _ => value.ToString(),
        };

    /// <summary>The value as JSON would write it, without the quotes: no culture decides how a number looks.</summary>
    private static string? Text(object? value)
        => Plain(value) switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
}
