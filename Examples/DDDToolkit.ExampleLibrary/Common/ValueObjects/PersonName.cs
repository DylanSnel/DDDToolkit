using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;


//User code
[ValueObject]
public partial record PersonName
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

    [Internal]
    public ReadOnlyCollection<string> Errors => _errors.AsReadOnly();

    private readonly List<string> _errors = [];

    protected override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            _errors.Add("First name is required");
            return false;
        }
        if (string.IsNullOrWhiteSpace(LastName))
        {
            _errors.Add("Last name is required");
            return false;
        }

        return true;

    }

    [JsonInclude]
    public string FirstName { get; protected init; }

    [DontCompare]
    [JsonInclude]
    public string? MiddleNames { get; protected init; }
    [JsonInclude]
    public string LastName { get; protected init; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
    [DontCompare]
    public string Initials => string.Join("", FirstName?[0], LastName?[0]).Trim();

    public override string ToString() => FullName;

}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
