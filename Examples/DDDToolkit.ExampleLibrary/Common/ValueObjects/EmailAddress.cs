using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

//UserCode
//[SingleValueObject<string>(ColumnLength: MaxLength)] //Comment out the attribute to allow for partial classes
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public const string EmailRegex = @"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$";

    public static EmailAddress Create(string value) => new(value);

    public override bool Validate(IEmailAddress var)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            return false;
        }
        if (!ValidEmail().Match(Value).Success)
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(EmailRegex)]
    private static partial Regex ValidEmail();

}


//Everythin below this line is generated code. Any changes will be lost.

//DDDToolkit.Analyzers generates this
public partial record EmailAddress : SingleValueObject<string, IEmailAddress>, IEmailAddress
{
    protected EmailAddress(string value) : base(value)
    {
        EnsureValidated();
    }

    [JsonConstructor]
    protected EmailAddress()
    {
    }

    public override int GetHashCode()
        => base.GetHashCode();

    public EmailAddress(Raw raw)
    {
        raw.EnsureValidated();
        _isValid = true;
        Value = raw.Value!;
    }

    public virtual bool Equals(EmailAddress? other)
    {
        if (other is null)
        {
            return false;
        }
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }


    public partial record Raw : SingleValueObject<string, IEmailAddress>, IRaw, IEmailAddress
    {

        [JsonConstructor]
        public Raw()
        {
        }

        public override bool Validate(IEmailAddress email)
        {
            EmailAddress emailAddress = new EmailAddress();
            return emailAddress.Validate(this);
        }

        public static implicit operator EmailAddress(Raw raw) => new(raw);

        public override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
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

public interface IEmailAddress : IValueObject<IEmailAddress>
{
    string? Value { get; }
}

// DDDToolkit.EntityFramework
public partial record EmailAddress
{
    public class Converter : ValueConverter<EmailAddress, string>
    {
        public Converter()
            : base(
                v => v.Value,
                v => new EmailAddress(v))
        {
        }
    }
}



