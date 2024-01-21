using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.NugetApi.Domain.ProductAggregate.ValueObjects;

[EntityId<Guid>("PRDCT")]
public partial record ProductId
{
    internal static ProductId CreateUnique()
    {
        throw new NotImplementedException();
    }
}

//[Owned]

//[PrimaryKey(nameof(ProductId))]
//public record ProductIdReference : ProductId
//{
//    public ProductIdReference(Guid value) : base(value)
//    {
//    }


//}

