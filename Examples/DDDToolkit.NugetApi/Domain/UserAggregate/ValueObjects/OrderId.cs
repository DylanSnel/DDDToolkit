using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.NugetApi.Domain.UserAggregate.ValueObjects;

[EntityId<Guid>("ORD")]
public partial record OrderId
{
}

