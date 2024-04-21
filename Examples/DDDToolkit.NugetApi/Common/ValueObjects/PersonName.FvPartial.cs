using DDDToolkit.Abstractions.Attributes;
using FluentValidation;
using FluentValidation.Results;
using System.Collections.ObjectModel;

namespace DDDToolkit.NugetApi.Common.ValueObjects;

partial record PersonName
{
    [Internal]
    public ReadOnlyCollection<ValidationFailure> Errors => _errors.AsReadOnly();

    private List<ValidationFailure> _errors = [];

    protected override bool Validate()
    {
        var validator = new Validator();
        var result = validator.Validate(this);
        _errors = result.Errors;
        return result.IsValid;
    }

    partial class Validator : AbstractValidator<PersonName>
    {

    }
}

