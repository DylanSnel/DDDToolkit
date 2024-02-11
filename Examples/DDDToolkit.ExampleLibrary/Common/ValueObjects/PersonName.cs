using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

//[ValueObject]
public partial record PersonName
{
    public PersonName(string firstName, string middleNames, string lastName)
    {
        FirstName = firstName;
        MiddleNames = middleNames;
        LastName = lastName;
        EnsureValidated();
    }

    public PersonName(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
        EnsureValidated();
    }

    public static Raw Create(string firstName, string middleNames, string lastName) => new()
    {
        FirstName = firstName,
        MiddleNames = middleNames,
        LastName = lastName
    };

    public ReadOnlyCollection<string> Errors => _errors.AsReadOnly();

    private readonly List<string> _errors = [];

    public override bool Validate(IPersonName valueObject)
    {
        if (valueObject is null)
        {
            _errors.Add("Value object is null");
            return false;
        }

        if (string.IsNullOrWhiteSpace(valueObject.FirstName))
        {
            _errors.Add("First name is required");
            return false;
        }
        if (string.IsNullOrWhiteSpace(valueObject.LastName))
        {
            _errors.Add("Last name is required");
            return false;
        }

        return true;

    }

    public string FirstName { get; private set; }
    [DontCompare]
    public string? MiddleNames { get; private set; }

    public string LastName { get; private set; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
    [DontCompare]
    public string Initials => string.Join("", FirstName?[0], LastName?[0]).Trim();

    public override string ToString() => FullName;

}
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

public partial record PersonName : ValueObject<IPersonName>, IAlwaysValid, IPersonName
{
    [Internal]
    public override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return LastName;
    }

    public virtual bool Equals(PersonName? other)
    {
        if (other is null)
        {
            return false;
        }
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    [Internal]
    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    protected PersonName()
    {
    }

    public PersonName(Raw raw)
    {
        raw.EnsureValidated();
        _isValid = true;
        FirstName = raw.FirstName!;
        MiddleNames = raw.MiddleNames!;
        LastName = raw.LastName!;
    }

    public partial record Raw : ValueObject<IPersonName>, IRaw, IPersonName
    {
        public Raw()
        {

        }

        public string? FirstName { get; set; }
        public string? MiddleNames { get; set; }
        public string? LastName { get; set; }

        public override bool Validate(IPersonName valueObject)
        {
            var personName = new PersonName();
            return personName.Validate(valueObject);
        }

        public static implicit operator PersonName(Raw raw) => new(raw);

        [Internal]
        public override IEnumerable<object?> GetEqualityComponents()
        {
            yield return FirstName;
            yield return LastName;
        }

        public virtual bool Equals(Raw? other)
        {
            if (other is null)
            {
                return false;
            }
            return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
        }

        public override int GetHashCode()
            => GetEqualityComponents()
                .Select(x => x?.GetHashCode() ?? 0)
                .Aggregate((x, y) => x ^ y);
    }
}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

[ComplexType]
public partial record PersonName
{

}

public interface IPersonName : IValueObject<IPersonName>
{
    string? FirstName { get; }
    string? MiddleNames { get; }
    string? LastName { get; }
}
