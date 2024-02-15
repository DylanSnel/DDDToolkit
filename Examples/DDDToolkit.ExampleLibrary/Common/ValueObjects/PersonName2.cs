using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using System.Collections.ObjectModel;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

[ValueObject]
public partial record PersonName2
{
    public PersonName2(string firstName, string middleNames, string lastName)
    {
        FirstName = firstName;
        MiddleNames = middleNames;
        LastName = lastName;

    }

    public PersonName2(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }


    [Internal]
    public ReadOnlyCollection<string> Errors => _errors.AsReadOnly();

    private readonly List<string> _errors = [];

    public string FirstName { get; protected set; }
    [DontCompare]
    public string? MiddleNames { get; protected set; }

    public string LastName { get; protected set; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
    [DontCompare]
    public string Initials => string.Join("", FirstName?[0], LastName?[0]).Trim();

    public override string ToString() => FullName;

}
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

