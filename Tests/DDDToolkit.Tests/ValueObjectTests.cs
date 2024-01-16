using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;

namespace DDDToolkit.Tests;

public class ValueObjectTests
{
    [Fact]
    public void EqualityTests()
    {
        var personname = new PersonName("John", "Doe");
        var personname2 = new PersonName("John", "Someting", "Doe");
        var personname3 = new PersonName("Johny", "Someting", "Doe");
        (personname == personname2).Should().BeTrue();
        (personname == personname3).Should().BeFalse();
    }

    [Fact]
    public void WithOverridesTests()
    {
        //var personname = new PersonName("John", "Doe");
        //var personname2 = personname with { MiddleNames = "Something" };
        //(personname == personname2).Should().BeTrue();
        //(personname == personname3).Should().BeFalse();
    }
}
