using DTS.DDD.Abstractions.Attributes;

namespace DTS.DDD.ExampleApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("USER")]
public partial record UserId
{
}
