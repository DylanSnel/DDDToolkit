using DDDToolkit.Abstractions.Attributes;
using FluentValidation;

namespace DDDToolkit.Tests.Domain;

/// <summary>
/// A multi-property value object. <see cref="Note"/> carries <c>[DontCompare]</c>, so it rides along
/// but never takes part in equality; <see cref="Label"/> is computed and so is excluded automatically.
/// </summary>
[ValueObject]
public partial record Address
{
    /// <summary>Creates an address.</summary>
    public Address(string street, string city, string? note = null)
    {
        Street = street;
        City = city;
        Note = note;
    }

    /// <summary>Street line. Part of the identity of the address.</summary>
    public string Street { get; protected init; }

    /// <summary>City. Part of the identity of the address.</summary>
    public string City { get; protected init; }

    /// <summary>A courier note. Two addresses with different notes are the same address.</summary>
    [DontCompare]
    public string? Note { get; protected init; }

    /// <summary>A computed property; get-only properties are never equality components.</summary>
    public string Label => $"{Street}, {City}";

    /// <summary>
    /// A copy with a different street. The properties have <c>protected init</c> setters — which is what
    /// the generator's diagnostics insist on — so a <c>with</c> expression is only writable from inside
    /// the value object. That is the whole "cloning restriction": it is enforced by accessibility.
    /// </summary>
    public Address WithStreet(string street) => this with { Street = street };

    /// <summary>A copy with a different city.</summary>
    public Address WithCity(string city) => this with { City = city };

    /// <summary>Rules picked up by the generated <c>Validate()</c>.</summary>
    partial class Validator
    {
        /// <summary>Street and city are required; city is capped so the rule count is observable.</summary>
        public Validator()
        {
            RuleFor(x => x.Street).NotEmpty();
            RuleFor(x => x.City).NotEmpty().MaximumLength(20);
        }
    }
}

/// <summary>A single value object whose rule is a regular expression, mirroring EmailAddress.</summary>
[SingleValueObject<string>]
public partial record Slug
{
    /// <summary>Wraps a value.</summary>
    public static Slug Create(string value) => new(value);

    /// <summary>Rules picked up by the generated <c>Validate()</c>.</summary>
    partial class Validator
    {
        /// <summary>Lower-case letters, digits and hyphens only.</summary>
        public Validator()
        {
            RuleFor(x => x.Value).Matches("^[a-z0-9-]+$");
        }
    }
}

/// <summary>
/// Counts how often a generated <c>Validate()</c> body actually ran. Only
/// <c>DDDToolkit.Tests.Validation.ValueObjectValidationTests</c> constructs <see cref="CountedValue"/>,
/// so the count is never shared with a test class running in parallel.
/// </summary>
public static class ValidationCounter
{
    private static int _runs;

    /// <summary>How many times a <see cref="CountedValue"/> has been validated.</summary>
    public static int Runs => Volatile.Read(ref _runs);

    /// <summary>Called from the validation rule.</summary>
    public static void Record() => Interlocked.Increment(ref _runs);
}

/// <summary>A single value object that records every validation run, so caching can be observed.</summary>
[SingleValueObject<string>]
public partial record CountedValue
{
    /// <summary>Wraps a value.</summary>
    public static CountedValue Create(string value) => new(value);

    /// <summary>Rules picked up by the generated <c>Validate()</c>.</summary>
    partial class Validator
    {
        /// <summary>Non-empty, and it tells <see cref="ValidationCounter"/> each time it is evaluated.</summary>
        public Validator()
        {
            RuleFor(x => x.Value).Must(value =>
            {
                ValidationCounter.Record();
                return !string.IsNullOrEmpty(value);
            });
        }
    }
}

/// <summary>A command-shaped DTO for exercising <c>MustBeValid()</c> inside a validator of your own.</summary>
public sealed record PlaceOrder(Slug? Product, Address? ShipTo, int Quantity);
