using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;


namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

[ValueObject]
public class PersonName : ValueObject
{
    public PersonName(string firstName, string middleNames, string lastName)
    {
        FirstName = firstName;
        MiddleNames = middleNames;
        LastName = lastName;
    }

    public PersonName(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public string FirstName { get; }
    public string? MiddleNames { get; }
    public string LastName { get; }

    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();

    public string Initials => string.Join("", FirstName[0], LastName[0]).Trim();

    public override string ToString() => FullName;

    public override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return MiddleNames;
        yield return LastName;
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private PersonName()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {

    }
}
