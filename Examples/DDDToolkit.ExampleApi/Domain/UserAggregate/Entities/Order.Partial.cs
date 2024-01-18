using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.ExampleApi.Domain.UserAggregate.Entities;
[Owned]
public partial class Order : Entity<OrderId>
{

}
