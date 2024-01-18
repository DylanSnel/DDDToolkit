using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

[EntityId<Guid>("PRS")]
public partial record PersonId
{
    public static PersonId Create(Guid value) => new(value);
}
