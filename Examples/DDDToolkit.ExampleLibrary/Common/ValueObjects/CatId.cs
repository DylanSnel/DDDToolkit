using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

/// <summary>
/// A struct id: no allocation, value equality, no always-valid twin. The generator supplies
/// Value, the constructor, Create() (a new id in time order, for ICreatableEntityId), CreateUnique/
/// CreateSequential, Parse/TryParse, comparison, explicit conversions and a System.Text.Json converter.
/// The Create(Guid) below is an overload of its own, which does not keep the generator's Create() out.
/// </summary>
[EntityId<Guid>]
public readonly partial record struct CatId
{
    public static CatId Create(Guid value) => new(value);
}
