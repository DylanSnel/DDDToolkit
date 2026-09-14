using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Validation;
using FluentValidation;

namespace DDDToolkit.Tests.Validation;

/// <summary>
/// A value object that describes its own failures, with no FluentValidation in sight.
/// <para>
/// It derives from <see cref="ValueObject"/> by hand rather than carrying <c>[ValueObject]</c>, and that
/// is the point: this test project references DDDToolkit.FluentValidation, so every attributed type gets
/// the generated <c>Validate</c> overrides and an author could not write these. A hand-rolled subclass is
/// exactly what a project without that package compiles, so it exercises the same base-type code path a
/// FluentValidation-free consumer would.
/// </para>
/// </summary>
public record Temperature : ValueObject
{
    /// <summary>Wraps a reading in degrees Celsius, taken at a named place.</summary>
    public Temperature(int celsius, string label = "outside")
    {
        Celsius = celsius;
        Label = label;
    }

    /// <summary>The reading.</summary>
    public int Celsius { get; init; }

    /// <summary>Where the reading was taken.</summary>
    public string Label { get; init; }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Celsius;
        yield return Label;
    }

    /// <summary>Three independent rules, so a caller can be shown more than one failure at a time.</summary>
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (Celsius < -273)
        {
            errors.Add("A temperature cannot be below absolute zero.", nameof(Celsius), "BelowAbsoluteZero", Celsius);
        }

        if (Celsius > 1000)
        {
            errors.Add("This thermometer stops at 1000.", nameof(Celsius), "OutOfRange", Celsius);
        }

        if (Label.Length == 0)
        {
            errors.Add("A reading needs a place.", nameof(Label), "LabelRequired");
        }
    }
}

/// <summary>A value object that refuses without saying why: the old <c>bool Validate()</c> on its own.</summary>
public record Mystery : ValueObject
{
    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield break;
    }

    /// <inheritdoc />
    protected override bool Validate() => false;
}

/// <summary>
/// A value object that overrides both halves. The verdict is the two together, so a value the described
/// rules accept is still invalid while <c>Validate()</c> says no.
/// </summary>
public record Padlock : ValueObject
{
    /// <summary>Whether the bolt-on <c>bool Validate()</c> accepts this value.</summary>
    public bool BoltAccepts { get; init; }

    /// <summary>Whether the described rule accepts this value.</summary>
    public bool DescribedRuleAccepts { get; init; }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BoltAccepts;
        yield return DescribedRuleAccepts;
    }

    /// <inheritdoc />
    protected override bool Validate() => BoltAccepts;

    /// <inheritdoc />
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (!DescribedRuleAccepts)
        {
            errors.Add("The described rule said no.", nameof(DescribedRuleAccepts), "Described");
        }
    }
}

/// <summary>
/// Counts how often the rules of a <see cref="Counted"/> actually run, so caching can be observed
/// without borrowing the counter another test class uses.
/// </summary>
public static class FailureModelCounter
{
    private static int _runs;

    /// <summary>How many times a <see cref="Counted"/> has been validated.</summary>
    public static int Runs => Volatile.Read(ref _runs);

    /// <summary>Called from the rule.</summary>
    public static void Record() => Interlocked.Increment(ref _runs);
}

/// <summary>A value object that records every validation run.</summary>
public record Counted : ValueObject
{
    /// <summary>The wrapped text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Text;
    }

    /// <inheritdoc />
    protected override void Validate(ValidationErrorBuilder errors)
    {
        FailureModelCounter.Record();

        if (Text.Length == 0)
        {
            errors.Add("Text is required.", nameof(Text), "NotEmpty");
        }
    }
}

/// <summary>
/// A reference-type identifier with rules, so the non-throwing conversion can be shown for the third
/// family. <see cref="ExampleLibrary.Common.ValueObjects.PersonId"/> has no rules and is always valid.
/// </summary>
[EntityId<Guid>("TCK")]
public partial record TicketId
{
    /// <summary>Wraps a value.</summary>
    public static TicketId Create(Guid value) => new(value);

    /// <summary>Rules picked up by the generated <c>Validate()</c>.</summary>
    partial class Validator
    {
        /// <summary>The empty Guid identifies nothing.</summary>
        public Validator() => RuleFor(x => x.Value).NotEqual(Guid.Empty).WithErrorCode("NoSuchTicket");
    }
}
