using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleApi.Domain.ProductAggregate.ValueObjects;

[EntityId<Guid>("PRDCT")]
public partial record ProductId
{
}

//[Owned]

//[PrimaryKey(nameof(ProductId))]
//public record ProductIdReference : ProductId
//{
//    public ProductIdReference(Guid value) : base(value)
//    {
//    }


//}

