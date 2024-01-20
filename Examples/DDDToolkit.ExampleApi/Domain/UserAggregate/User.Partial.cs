using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate;

public partial class User : AggregateRoot<UserId>
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    protected User()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
    }
}
