using DTS.DDD.Abstractions.Attributes;
using DTS.DDD.Validation;

namespace DTS.DDD.Benchmarks.Validation;

/// <summary>
/// The value object of the FluentValidation benchmark, with the same two rules in a nested Validator of the rule
/// generator.
/// </summary>
[SingleValueObject<string>]
public partial record RuleEmailAddress
{
    /// <summary>Wraps <paramref name="value"/> without validating it.</summary>
    public static RuleEmailAddress Create(string value) => new(value);

    partial class Validator
    {
        /// <summary>The rules: a value that is an email address.</summary>
        public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
    }
}
