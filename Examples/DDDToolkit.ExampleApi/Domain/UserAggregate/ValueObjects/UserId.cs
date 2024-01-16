using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("USER")]
public partial record UserId
{
}
