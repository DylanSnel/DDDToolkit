using DDDToolkit.Abstractions.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;


[SingleValueObject<string>(ColumnLength: MaxLength)]
public partial record EmailAddress : IValidatable<EmailAddress.Validator>
{
    public const int MaxLength = 255;

    public const string EmailRegex = @"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$";




    public static Result<EmailAddress> Create(string value)
    {
        if (!Regex.IsMatch(value, EmailRegex))
        {
            return new FailedResult();
        }
        return new EmailAddress(value);
    }


    public Validate

    public class Validator : FluentValidor
    {
        Rule()
            .ForProperty(x => x.Value)
            .NotEmpty()
            .Matches(EmailRegex);

    }


}
public partial record Unvalidated<T> where T : IValidatable
{
    public string Value { get; init; }

    public bool Validated { get; private set; }
    public ValidationResult IsValid()
    {
        Validated = true;
        return new Validator().Validate(this);
    }



    public static implicit operator T(Unvalidated<T> emailAddress)
    {
        if (emailAddress.Validated || emailAddress.IsValid().IsValid)
        {
            throw new ArgumentException("Email address is not valid");
        }
        return EmailAddress.Create(emailAddress.Value).Value;
    }
}

