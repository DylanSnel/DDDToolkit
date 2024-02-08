using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

public class SingleValueObjectTests
{
    [Fact]
    public void EqualityTests()
    {
        var email = EmailAddress.Create("test@example.com");
        var email2 = EmailAddress.Create("test@example.com");
        (email == email2).Should().BeTrue();
    }

    //[Fact]
    //public void ReferenceEqualityTests()
    //{
    //    var productId = ProductId.CreateUnique();
    //    var reference = new ProductIdReference(productId.Value);
    //    (productId == reference).Should().BeTrue();
    //}
}
