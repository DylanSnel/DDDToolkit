using DDDToolkit.BaseTypes;
using DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;

namespace DDDToolkit.ExampleApi.Domain.ProductAggregate;

public partial class Product : AggregateRoot<ProductId, Guid>
{

}
