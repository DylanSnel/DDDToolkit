using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

//[ValueObject]
public partial record PersonName
{

    private new readonly List<string> Errors = [];

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

    //public PersonName() { }

    protected override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            Errors.Add("First name is required");
            return false;
        }
        if (string.IsNullOrWhiteSpace(LastName))
        {
            Errors.Add("Last name is required");
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

public partial record PersonName : ValueObject
{

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

    public override int GetHashCode()
        => GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);

    [JsonConstructor]
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

    public PersonName(PersonName personName) : base(personName)
    {
        FirstName = personName.FirstName;
        MiddleNames = personName.MiddleNames;
        LastName = personName.LastName;
        EnsureValidated();
    }


    public record Raw : ValueObject, IRaw//., IPersonName
    {
        public Raw()
        {

        }

        public string? FirstName { get; set; }
        public string? MiddleNames { get; set; }
        public string? LastName { get; set; }

        protected override bool Validate()
        {
            if (string.IsNullOrWhiteSpace(FirstName))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(LastName))
            {
                return false;
            }

            return true;

        }


        public static implicit operator PersonName(Raw raw) => new(raw);

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
public partial record PersonName : ValueObject
{
}
