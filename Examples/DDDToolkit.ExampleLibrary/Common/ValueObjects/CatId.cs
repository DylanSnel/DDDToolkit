using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

/// <summary>
/// A struct id: no allocation, value equality, no always-valid twin. The generator supplies
/// Value, the constructor, CreateUnique/CreateSequential, Parse/TryParse, comparison, explicit
/// conversions and a System.Text.Json converter.
/// </summary>
[EntityId<Guid>]
public readonly partial record struct CatId
{
    public static CatId Create(Guid value) => new(value);
}
