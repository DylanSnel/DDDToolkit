using DDDToolkit.Abstractions.Attributes;


namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

[ValueObject]
public sealed partial record PersonName
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

    //public override IEnumerable<object?> GetEqualityComponents()
    //{
    //    yield return FirstName;
    //    yield return LastName;
    //}

    //public virtual bool Equals(PersonName? other)
    //{
    //    if (other is null)
    //    {
    //        return false;
    //    }
    //    return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    //}

    //public override int GetHashCode()
    //=> GetEqualityComponents()
    //    .Select(x => x?.GetHashCode() ?? 0)
    //    .Aggregate((x, y) => x ^ y);


    public string FirstName { get; }
    [DontCompare]
    public string? MiddleNames { get; }
    public string LastName { get; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
    [DontCompare]
    public string Initials => string.Join("", FirstName[0], LastName[0]).Trim();

    public sealed override string ToString() => FullName;
}
