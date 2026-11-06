using DTS.DDD.Abstractions.Attributes;

namespace DTS.DDD.ExampleLibrary.Common.ValueObjects;

[EntityId<Guid>("PRS")]
public partial record PersonId
{
    public static PersonId Create(Guid value) => new(value);
}
