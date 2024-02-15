﻿// <auto-generated/>
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.ExampleLibrary.Common.ValueObjects;

partial record PersonId 
{
    public class PersonIdConverter : ValueConverter<PersonId, Guid>
    {
        public PersonIdConverter()
            : base(
                v => v.Value,
                v => new PersonId(v))
        {
        }
    }
}

partial record ValidPersonId 
{
    public class ValidPersonIdConverter : ValueConverter<ValidPersonId, Guid>
    {
        public ValidPersonIdConverter()
            : base(
                v => v.Value,
                v => new ValidPersonId(v))
        {
        }
    }
}