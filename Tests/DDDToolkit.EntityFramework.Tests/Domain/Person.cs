using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;

namespace DDDToolkit.EntityFramework.Tests.Domain;

/// <summary>Aggregate with a [ValueObject] complex type and a nullable [SingleValueObject].</summary>
[AggregateRoot<MemberId>]
public partial class Person
{
    public Person(MemberId id, PersonName name, EmailAddress? email, DateOfBirth dateOfBirth) : base(id)
    {
        Name = name;
        Email = email;
        DateOfBirth = dateOfBirth;
    }

    public PersonName Name { get; private set; }

    public EmailAddress? Email { get; private set; }

    public DateOfBirth DateOfBirth { get; private set; }

    public void ChangeEmail(EmailAddress? email) => Email = email;

    public void Rename(PersonName name) => Name = name;
}
