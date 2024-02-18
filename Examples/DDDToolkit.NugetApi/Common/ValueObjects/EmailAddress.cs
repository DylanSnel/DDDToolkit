using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.NugetApi.Common.ValueObjects;


[SingleValueObject<string>(ColumnLength: MaxLength)]
public partial record EmailAddress
{
    public const int MaxLength = 255;
    public static EmailAddress Create(string value) => new(value);

}

