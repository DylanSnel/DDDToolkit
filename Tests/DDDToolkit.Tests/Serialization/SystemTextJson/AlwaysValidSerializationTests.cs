using DDDToolkit.Exceptions;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Serialization;
using FluentAssertions;
using System.Text.Json;

namespace DDDToolkit.Tests.Serialization.SystemTextJson;

/// <summary>
/// An always valid twin promises that every instance passed validation. Deserializing straight into one
/// would break that promise, because the document is not trusted - so it is refused, and the caller is
/// pointed at the raw type plus <c>ToValid()</c>.
/// </summary>
public class AlwaysValidSerializationTests
{
    private readonly JsonSerializerOptions _options = new JsonSerializerOptions().AddDDDToolkitConverters();

    [Fact]
    public void ValidSingleValueObject_CannotBeDeserializedDirectly()
    {
        var act = () => JsonSerializer.Deserialize<ValidEmailAddress>("\"test@example.com\"", _options);

        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void ValidValueObject_CannotBeDeserializedDirectly()
    {
        var act = () => JsonSerializer.Deserialize<ValidPersonName>("""{"FirstName":"John","LastName":"Doe"}""", _options);

        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void ValidClassId_CannotBeDeserializedDirectly()
    {
        var act = () => JsonSerializer.Deserialize<ValidPersonId>($"\"{Guid.NewGuid()}\"", _options);

        act.Should().Throw<SerializationNotAllowedException>();
    }

    [Fact]
    public void RawValueObject_IsDeserializedAndCanBeValidatedAfterwards()
    {
        var email = JsonSerializer.Deserialize<EmailAddress>("\"test@example.com\"", _options);

        email!.ToValid().Should().Be(EmailAddress.Create("test@example.com").ToValid());
    }

    [Fact]
    public void RawValueObject_CarriesAnInvalidValueToTheValidationBoundary()
    {
        var email = JsonSerializer.Deserialize<EmailAddress>("\"not-an-email\"", _options);

        email!.IsValid.Should().BeFalse();
        var act = () => email.ToValid();
        act.Should().Throw<InvalidValueObjectException>();
    }

    [Fact]
    public void ValidTwin_CanStillBeWritten()
    {
        // Writing is safe: the instance was validated when it was built.
        var email = EmailAddress.Create("test@example.com").ToValid();

        JsonSerializer.Serialize(email, _options).Should().Be("\"test@example.com\"");
    }

    [Fact]
    public void ValidTwin_CanStillBeWrittenAsPartOfADocument()
    {
        var name = new PersonName("John", "Doe").ToValid();

        var json = JsonSerializer.Serialize(new { Name = name }, _options);

        json.Should().Contain("\"FirstName\":\"John\"").And.Contain("\"LastName\":\"Doe\"");
    }
}
