﻿// <auto-generated/>
using DDDToolkit.BaseTypes;
using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Abstractions.Attributes;
#nullable enable
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;
partial record DateOfBirth : SingleValueObject<DateOnly>
{
    public virtual bool Equals(DateOfBirth? other)
    {
        if (other is null)
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
        => base.GetHashCode();

    protected DateOfBirth(DateOnly value): base(value)
    {
    }

    [JsonConstructor]
    protected DateOfBirth()
    {
    }

    public ValidDateOfBirth ToValid() => new(this);
}

public partial record ValidDateOfBirth  : DateOfBirth, IAlwaysValid
{
    public ValidDateOfBirth(DateOfBirth value)
    {
        value.EnsureValidated();
        this.Value = value.Value;
        _isValid = true;
    }

    public ValidDateOfBirth(DateOnly value) : base(value)
    {
        EnsureValidated();
    }

    public virtual bool Equals(ValidDateOfBirth? other)
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