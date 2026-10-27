namespace Examples.Tenancy.Tenants.Application.Seats;

/// <summary>
/// A role the calling seat holds somewhere, as its own overview shows it: what this application selects of the
/// package's role, with its permissions, which a seat may always read of what it holds itself.
/// </summary>
/// <param name="Id">The role.</param>
/// <param name="Name">Its name in the tenant.</param>
/// <param name="FromPack">The pack it was copied from, or <see langword="null"/> for a role the tenant made.</param>
/// <param name="Status">Whether it is active, or archived and granting nothing.</param>
/// <param name="Permissions">The permissions it grants.</param>
/// <param name="ManagesAccess">Whether one of its live permissions manages access, as the role manager says.</param>
public sealed record RoleOfMine(RoleId Id, string Name, string? FromPack, RoleStatus Status, IReadOnlyList<string> Permissions, bool ManagesAccess);
