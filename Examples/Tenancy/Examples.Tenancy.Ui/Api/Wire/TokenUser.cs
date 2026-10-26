using System.Text.Json.Serialization;

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>Who a token was issued to.</summary>
public sealed record TokenUser(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string? Email);
