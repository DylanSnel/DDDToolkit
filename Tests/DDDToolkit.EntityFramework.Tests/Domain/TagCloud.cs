using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.EntityFramework.Tests.Domain;

[EntityId<Guid>]
public readonly partial record struct TagCloudId;

/// <summary>
/// Deliberately unmappable: EF Core cannot store a primitive collection in a HashSet, so the
/// generated <c>IReadOnlySet&lt;string&gt;</c> must produce a clear model-building error.
/// </summary>
[AggregateRoot<TagCloudId>]
public partial class TagCloud
{
    public TagCloud(TagCloudId id) : base(id)
    {
    }

    public partial IReadOnlySet<string> Keywords { get; }

    public bool Add(string keyword) => _keywords.Add(keyword);
}
