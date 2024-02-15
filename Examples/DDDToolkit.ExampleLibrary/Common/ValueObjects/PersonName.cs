using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;


//User code
[ValueObject]
//[AllowInvalid]
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


// Generated code

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.



//partial record PersonName : ValueObject
//{
//    [Internal]
//    public override IEnumerable<object?> GetEqualityComponents()
//    {
//        yield return FirstName;
//        yield return LastName;
//    }

//    public virtual bool Equals(PersonName? other)
//    {
//        if (other is null)
//        {
//            return false;
//        }
//        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
//    }

//    [Internal]
//    public override int GetHashCode()
//        => GetEqualityComponents()
//            .Select(x => x?.GetHashCode() ?? 0)
//            .Aggregate((x, y) => x ^ y);

//    public PersonName()
//    {
//    }

//    public ValidPersonName ToValid() => new(this);

//}

//public partial record ValidPersonName : PersonName, IAlwaysValid
//{
//    public ValidPersonName(PersonName value)
//    {
//        value.EnsureValidated();
//        FirstName = value.FirstName;
//        MiddleNames = value.MiddleNames;
//        LastName = value.LastName;
//        _isValid = true;
//    }

//    public virtual bool Equals(ValidPersonName? other)
//    {
//        if (other is null)
//        {
//            return false;
//        }
//        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
//    }

//    public override int GetHashCode()
//        => GetEqualityComponents()
//            .Select(x => x?.GetHashCode() ?? 0)
//            .Aggregate((x, y) => x ^ y);
//}




//[ComplexType]
//partial record PersonName
//{

//}

//[ComplexType]
//partial record ValidPersonName
//{

//}


#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
