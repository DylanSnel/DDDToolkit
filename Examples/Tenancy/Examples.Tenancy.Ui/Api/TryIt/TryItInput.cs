namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>
/// What the try-it form holds: the tenant to name, whether to send a token, and every field any action takes. Ids
/// are text, picked from a list or typed, so another tenant's id or a malformed one can be sent as easily as a
/// valid one.
/// </summary>
public sealed class TryItInput
{
    /// <summary>The slug for the <c>Tenant</c> header; blank sends none.</summary>
    public string? Tenant { get; set; }

    /// <summary>Whether to call anonymously, with no <c>Authorization</c> header.</summary>
    public bool NoToken { get; set; }

    /// <inheritdoc cref="TryItField.Project"/>
    public string? Project { get; set; }

    /// <inheritdoc cref="TryItField.Key"/>
    public string? Key { get; set; }

    /// <inheritdoc cref="TryItField.Name"/>
    public string? Name { get; set; }

    /// <inheritdoc cref="TryItField.Unit"/>
    public string? Unit { get; set; }

    /// <inheritdoc cref="TryItField.Title"/>
    public string? Title { get; set; }

    /// <inheritdoc cref="TryItField.Seat"/>
    public string? Seat { get; set; }

    /// <inheritdoc cref="TryItField.Role"/>
    public string? Role { get; set; }

    /// <inheritdoc cref="TryItField.ProjectRole"/>
    public string? ProjectRole { get; set; }

    /// <inheritdoc cref="TryItField.Until"/>
    public DateOnly? Until { get; set; }

    /// <inheritdoc cref="TryItField.Reason"/>
    public string? Reason { get; set; }

    /// <summary><see cref="Until"/> as the instant the API reads: the first moment after that day, so the day itself still counts.</summary>
    public DateTimeOffset? UntilInstant => UntilDay.Instant(Until);
}
