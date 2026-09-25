using DDDToolkit.Abstractions.Attributes;

namespace Shop;

[ValueObject]
public partial record Address(string Street, string City)
{
    protected override bool Validate() => Street.Length > 0 && City.Length > 0;
}
