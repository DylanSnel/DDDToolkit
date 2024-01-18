using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

[EntityId<Guid>]
public partial record CatId
{
    public static CatId Create(Guid value) => new(value);
}
