﻿// <auto-generated/>
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

partial record EmailAddress 
{
    public class EmailAddressConverter : ValueConverter<EmailAddress, string>
    {
        public EmailAddressConverter()
            : base(
                v => v.Value,
                v => new EmailAddress(v))
        {
        }
    }
}

partial record ValidEmailAddress 
{
    public class ValidEmailAddressConverter : ValueConverter<ValidEmailAddress, string>
    {
        public ValidEmailAddressConverter()
            : base(
                v => v.Value,
                v => new ValidEmailAddress(v))
        {
        }
    }
}