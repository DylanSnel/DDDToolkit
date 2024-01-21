using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.NugetApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("USER")]
public partial record UserId
{
}
