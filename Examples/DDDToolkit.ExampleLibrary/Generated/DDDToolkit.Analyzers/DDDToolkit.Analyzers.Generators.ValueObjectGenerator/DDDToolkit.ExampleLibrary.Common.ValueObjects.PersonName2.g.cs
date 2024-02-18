﻿// <auto-generated/>
using DDDToolkit.BaseTypes;
using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Abstractions.Attributes;
#nullable enable
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;
partial record PersonName2 : ValueObject
{
    public override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return LastName;
    }

    public virtual bool Equals(PersonName2? other)
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
    protected PersonName2()
    {
    }

    public ValidPersonName2 ToValid() => new(this);
}

public partial record ValidPersonName2  : PersonName2, IAlwaysValid
{
    public ValidPersonName2(PersonName2 value)
    {
        value.EnsureValidated();
        this.FirstName = value.FirstName;
        this.MiddleNames = value.MiddleNames;
        this.LastName = value.LastName;
        _isValid = true;
    }

    [Internal]
    public override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return LastName;
    }

    public virtual bool Equals(ValidPersonName2? other)
    {
        if (other is null)
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
        => base.GetHashCode();

}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.