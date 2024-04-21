using DDDToolkit.Abstractions.Attributes;
using FluentValidation;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

//UserCode
[SingleValueObject<string>(ColumnLength: MaxLength)] //Comment out the attribute to allow for partial classes
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public const string EmailRegex = @"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$";

    public static EmailAddress Create(string value) => new(value);


    partial class Validator
    {
        public Validator()
        {
            RuleFor(x => x.Value).Matches(EmailRegex);
        }
    }
    //protected override bool Validate()
    //{
    //    if (string.IsNullOrWhiteSpace(Value))
    //    {
    //        return false;
    //    }
    //    if (!Regex.Match(Value, EmailRegex).Success)
    //    {
    //        return false;
    //    }

    //    return true;
    //}

    //[GeneratedRegex(EmailRegex)]
    //private static partial Regex ValidEmail();

}


//Everythin below this line is generated code. Any changes will be lost.

//DDDToolkit.Analyzers generates this
//public partial record EmailAddress : SingleValueObject<string>
//{
//    protected EmailAddress(string value) : base(value)
//    {
//    }

//    [JsonConstructor]
//    protected EmailAddress()
//    {
//    }

//    public override int GetHashCode()
//        => base.GetHashCode();

//    public virtual bool Equals(EmailAddress? other)
//    {
//        if (other is null)
//        {
//            return false;
//        }
//        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
//    }

//    public ValidEmailAddress ToValid() => new(this);

//}

//public partial record ValidEmailAddress : EmailAddress, IAlwaysValid
//{
//    public ValidEmailAddress(EmailAddress value)
//    {
//        value.EnsureValidated();
//        this.Value = value.Value;
//        _isValid = true;
//    }

//    public ValidEmailAddress(string value) : base(value)
//    {
//        EnsureValidated();
//    }


//    public virtual bool Equals(ValidEmailAddress? other)
//    {
//        if (other is null)
//        {
//            return false;
//        }
//        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
//    }

//    public override int GetHashCode()
//        => base.GetHashCode();
//}


//// DDDToolkit.EntityFramework
//public partial record EmailAddress
//{
//    public class Converter : ValueConverter<EmailAddress, string>
//    {
//        public Converter()
//            : base(
//                v => v.Value,
//                v => new EmailAddress(v))
//        {
//        }
//    }
//}

//public partial record ValidEmailAddress
//{
//    public new class Converter : ValueConverter<ValidEmailAddress, string>
//    {
//        public Converter()
//            : base(
//                v => v.Value,
//                v => new ValidEmailAddress(v))
//        {
//        }
//    }
//}



