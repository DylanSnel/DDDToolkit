using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;

namespace DDDToolkit.Tests.ValueObjects;

public class ValueObjectTests
{
    [Fact]
    public void EqualityTests()
    {
        var personname = new PersonName("John", "Doe");
        var personname2 = new PersonName("John", "Doe");
        var personname3 = new PersonName("John", "Someting", "Doe");
        var personname4 = new PersonName("Johny", "Someting", "Doe");
        (personname == personname2).Should().BeTrue();
        (personname == personname3).Should().BeTrue();
        (personname == personname4).Should().BeFalse();
    }

    [Fact]
    public void WithOverridesTests()
    {

        var personname = new PersonName("John", "Doe");
        var personname2 = new PersonName.Raw()
        {
            FirstName = "John",
            LastName = ""
        };

        var valid = personname2.IsValid;


        //var personname2 = personname with { MiddleNames = "something" };
        //var personname3 = personname with { FirstName = "" };
        ////(personname == personname2).Should().BeTrue();

        //Action act = () =>
        //{

        //    var personname3 = personname with { FirstName = "" };
        //};
        //act.Should().Throw<Exception>();
        //    //(personname == personname3).Should().BeFalse();
    }
}
