using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Validation;

namespace DDDToolkit.Examples.Ordering.Domain.ValueObjects;

/// <summary>
/// Where an order is going. A value object: two addresses with the same contents are the same address.
/// </summary>
/// <remarks>
/// The rules live here rather than in a request validator because they are true of an address wherever
/// it came from. The endpoint asks for them with <c>TryToValid</c> and turns the failures into a 400;
/// see <c>Api/OrderingEndpoints.cs</c>.
/// <para>
/// <c>[JsonInclude]</c> is on the properties because the setters are <c>protected init</c>, which is
/// what stops a caller cloning an invalid copy with <c>with</c>. System.Text.Json will not write a
/// property it cannot reach, and the outbox stores the domain event this address travels in.
/// </para>
/// </remarks>
[ValueObject]
public partial record Address
{
    public Address(string street, string city, string postalCode)
        => (Street, City, PostalCode) = (street, city, postalCode);

    [JsonInclude]
    public string Street { get; protected init; }

    [JsonInclude]
    public string City { get; protected init; }

    [JsonInclude]
    public string PostalCode { get; protected init; }

    /// <summary>
    /// The overload that says why, not just whether. A caller reaching this address from an HTTP body
    /// has to tell the user which field is wrong, and a bare <c>false</c> cannot.
    /// </summary>
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (string.IsNullOrWhiteSpace(Street))
        {
            errors.Add("A street is required.", nameof(Street), "Required", Street);
        }

        if (string.IsNullOrWhiteSpace(City))
        {
            errors.Add("A city is required.", nameof(City), "Required", City);
        }

        // Deliberately one country's rule, so the example does not pretend to know every format.
        if (!PostalCodes.IsMatch(PostalCode ?? string.Empty))
        {
            errors.Add("A postal code looks like 1234 AB.", nameof(PostalCode), "Malformed", PostalCode);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex PostalCodes =
        new(@"^\d{4}\s?[A-Za-z]{2}$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
