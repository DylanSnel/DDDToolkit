namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// A seat: its id, the name the application keeps for it in its tenant, and its status. The seat of <c>GET /me</c>
/// and of every row of <c>GET /me/seats</c>, and the rows of <c>GET /tenancy/seats</c> and of
/// <c>POST /tenancy/directory/seats</c>.
/// </summary>
public sealed record SeatInfo(Guid Id, string DisplayName, string Status)
{
    /// <summary>Whether the seat is active; any other seat gives its person nothing.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The seat as a picker lists it: its name, with its status after it when it is not active, so every picker
    /// says the same about a seat the API would refuse.
    /// </summary>
    public string Label => IsActive ? DisplayName : DisplayName + " (" + Status + ")";
}
