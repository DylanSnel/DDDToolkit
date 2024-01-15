using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;

namespace DDDToolkit.Tests;

public class EntityIdObjectTests
{
    [Fact]
    public void EqualityTests()
    {
        var catId1 = CatId.CreateUnique();
        var catId2 = CatId.Create(catId1.Value).Value;
        var personId1 = PersonId.Create(catId1.Value).Value;
        var personId2 = PersonId.CreateUnique();
        (catId1 == catId2).Should().BeTrue();
        (catId1 == personId1).Should().BeFalse();
        (personId1 == personId2).Should().BeFalse();
    }

    [Fact]
    public void ToStringTests()
    {
        var catId1 = CatId.CreateUnique();
        var personId1 = PersonId.Create(catId1.Value).Value;
        catId1.ToString().Should().Be(catId1.Value.ToString());
        personId1.ToString().Should().Be("PRS_" + personId1.Value.ToString());
    }
}
