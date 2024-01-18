using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;


[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);

}

