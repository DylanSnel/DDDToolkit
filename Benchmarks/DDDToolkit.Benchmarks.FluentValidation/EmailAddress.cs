using DDDToolkit.Abstractions.Attributes;
using FluentValidation;

namespace DDDToolkit.Benchmarks.FluentValidation;

/// <summary>A single value object with FluentValidation rules, as the documentation writes one.</summary>
[SingleValueObject<string>]
public partial record EmailAddress
{
    /// <summary>Wraps <paramref name="value"/> without validating it.</summary>
    public static EmailAddress Create(string value) => new(value);

    partial class Validator
    {
        /// <summary>The rules: a value that is an email address.</summary>
        public Validator() => RuleFor(x => x.Value).NotEmpty().EmailAddress();
    }
}
