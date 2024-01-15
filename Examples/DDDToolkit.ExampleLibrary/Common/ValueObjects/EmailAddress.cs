using DDDToolkit.Abstractions.Attributes;
using System.ComponentModel.DataAnnotations;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;


[SingleValueObject<string>]
public partial record EmailAddress
{

    [MaxLength(255)]
    public new string Value { get; private set; } = string.Empty;
}

