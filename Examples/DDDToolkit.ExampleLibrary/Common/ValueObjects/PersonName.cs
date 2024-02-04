using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

//[ValueObject]
public partial record PersonName
{

    public PersonName(string firstName, string middleNames, string lastName)
    {
        FirstName = firstName;
        MiddleNames = middleNames;
        LastName = lastName;
        Validate();
    }

    public PersonName(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
        Validate();
    }

    //public PersonName() { }

    public override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(LastName))
        {
            return false;
        }

        return base.Validate();
    }

    public string FirstName { get; init; }
    [DontCompare]
    public string? MiddleNames { get; init; }

    public string LastName { get; init; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
    [DontCompare]
    public string Initials => string.Join("", FirstName?[0], LastName?[0]).Trim();

    public override string ToString() => FullName;

}
