using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate;

public partial class User : AggregateRoot<UserId>
{
}
