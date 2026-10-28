using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>What a statement comes to under the policies.</summary>
public enum Expectation
{
    /// <summary>It reads or changes at least one row.</summary>
    Rows,

    /// <summary>It reads or changes none: a policy's <c>USING</c> hid every row.</summary>
    NoRows,

    /// <summary>It fails with 42501: a policy's <c>WITH CHECK</c> refused the row it would write, or no policy lets the command in.</summary>
    Refused,

    /// <summary>It fails with 23514 as the row changes: a trigger keeps what the row is about, such as its seat or its unit.</summary>
    Fixed,
}

/// <summary>A caller of the matrix: a seeded person in Harbor or Orchard, possibly given a role in Harbor first.</summary>
/// <param name="Name">How the case names them.</param>
/// <param name="Person">The person.</param>
/// <param name="Tenant">The tenant they act in.</param>
/// <param name="At">
/// Where in Harbor they are given a role first, placed there if they are not: the Administrator role, every permission there and
/// below, and at a unit below the root nothing for the whole tenant; or, with <paramref name="Permissions"/>, a role holding those
/// alone.
/// </param>
/// <param name="Permissions">The permissions of the role they are given at <paramref name="At"/>, or <see langword="null"/> for the Administrator role.</param>
public sealed record MatrixUser(string Name, Person Person, TenantId Tenant, OrganizationUnitId? At = null, string[]? Keys = null);

/// <summary>
/// One cell of the policies on Tenancy's tables, asked by one caller: a statement, the statements that set it up in
/// the same transaction, and what it comes to.
/// </summary>
/// <param name="Table">The table.</param>
/// <param name="Command">The command.</param>
/// <param name="Holds">What the caller holds, where, for the case's name.</param>
/// <param name="Caller">Who runs it.</param>
/// <param name="Sql">The statement, with the seed's names in braces; a count for <c>SELECT</c>.</param>
/// <param name="Expected">What it comes to.</param>
/// <param name="Before">Statements that must each change a row first, in the same transaction.</param>
public sealed record PolicyCell(string Table, string Command, string Holds, MatrixUser Caller, string Sql, Expectation Expected, params string[] Before)
{
    public string Name => $"{Table} {Command}: {Holds} ({Caller.Name})";
}

/// <summary>
/// The cells of the policies on Tenancy's tables, each with a caller that holds the permission where the command asks it, one
/// that holds it elsewhere, and one without it, where the cell asks a permission; with a seat of the tenant and one of another where it asks
/// only the tenant; and with the seat a row is of and another seat where only its own seat reads it. Harbor as seeded: Ada administers it, Seth supervises North (units, seats, grants and widgets),
/// Hiro gives roles at North, Oli operates widgets at North Pier, Sue is suspended, and Eve holds nothing that applies
/// now; Odette administers Orchard, where Oli has a seat too. A caller given a role first gets a database of its own.
/// </summary>
public static class PolicyMatrix
{
    private static readonly MatrixUser AdaUser = new("Ada, administrator", Ada, Harbor);
    private static readonly MatrixUser SethUser = new("Seth, units, seats and grants at North", Seth, Harbor);
    private static readonly MatrixUser HiroUser = new("Hiro, grants at North", Hiro, Harbor);
    private static readonly MatrixUser HiroNorth = new("Hiro, every permission at North", Hiro, Harbor, At: North);
    private static readonly MatrixUser HiroSettings = new("Hiro, settings for the whole tenant too", Hiro, Harbor, At: HarborRoot, Keys: [TenancyPermissions.SettingsManage]);
    private static readonly MatrixUser OliAdministrator = new("Oli, administrator, with a seat in Orchard too", Oli, Harbor, At: HarborRoot);
    private static readonly MatrixUser OliUser = new("Oli, widgets at North Pier", Oli, Harbor);
    private static readonly MatrixUser OliActingInOrchard = new("Oli, in Orchard", Oli, Orchard);
    private static readonly MatrixUser EveUser = new("Eve, nothing now", Eve, Harbor);
    private static readonly MatrixUser EveSeats = new("Eve, seats at North", Eve, Harbor, At: North, Keys: [TenancyPermissions.SeatsManage]);
    private static readonly MatrixUser EveSeatsForTheTenant = new("Eve, seats for the whole tenant", Eve, Harbor, At: HarborRoot, Keys: [TenancyPermissions.SeatsManage]);
    private static readonly MatrixUser EveUnits = new("Eve, units at North", Eve, Harbor, At: North, Keys: [TenancyPermissions.UnitsManage]);
    private static readonly MatrixUser EveRoles = new("Eve, roles at North", Eve, Harbor, At: North, Keys: [TenancyPermissions.RolesManage]);
    private static readonly MatrixUser EveRolesForTheTenant = new("Eve, roles for the whole tenant", Eve, Harbor, At: HarborRoot, Keys: [TenancyPermissions.RolesManage]);
    private static readonly MatrixUser SueUser = new("Sue, suspended", Sue, Harbor);
    private static readonly MatrixUser OdetteUser = new("Odette, of Orchard", Odette, Orchard);

    /// <summary>Every cell, by its name.</summary>
    public static IReadOnlyDictionary<string, PolicyCell> Cells { get; } = All().ToDictionary(cell => cell.Name, StringComparer.Ordinal);

    /// <summary><paramref name="sql"/> with the seed's names in braces as the literals of their ids.</summary>
    public static string Fill(string sql)
    {
        foreach (var (name, id) in Ids)
        {
            sql = sql.Replace("{" + name + "}", "'" + id + "'", StringComparison.Ordinal);
        }

        return sql;
    }

    private static readonly (string Name, Guid Id)[] Ids =
    [
        ("Root", HarborRoot.Value), ("North", North.Value), ("South", South.Value), ("NorthPier", NorthPier.Value), ("OrchardRoot", OrchardRoot.Value),
        ("Ada", Ada.Seat.Value), ("Hiro", Hiro.Seat.Value), ("Seth", Seth.Seat.Value), ("OliInOrchard", OliInOrchard.Value), ("Oli", Oli.Seat.Value),
        ("Sue", Sue.Seat.Value), ("Eve", Eve.Seat.Value), ("Odette", Odette.Seat.Value),
        ("Administrator", HarborRoles.Administrator.Value), ("Supervisor", HarborRoles.Supervisor.Value), ("Operator", HarborRoles.Operator.Value),
        ("Watcher", HarborRoles.Watcher.Value), ("GrantsDesk", GrantsDesk.Value),
    ];

    /// <summary><paramref name="template"/> with <c>{0}</c>, <c>{1}</c> and on as <paramref name="values"/>, leaving the seed's names in braces.</summary>
    private static string Args(string template, params object[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            template = template.Replace("{" + index + "}", Convert.ToString(values[index], System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return template;
    }

    private static IEnumerable<PolicyCell> All()
    {
        // Tenants: read by its members and by whoever has a seat there; changed with the settings permission for the whole tenant.
        yield return new("Tenants", "SELECT", "a seat, its tenant", OliUser, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Tenants", "SELECT", "a seat, another tenant of the person's", OliUser, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 2", Expectation.Rows);
        yield return new("Tenants", "SELECT", "a seat, a tenant not the person's", AdaUser, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant", AdaUser, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Tenants", "UPDATE", "settings below the root", HiroNorth, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "holding no such permission", OliUser, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, another tenant", AdaUser, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, another tenant of the person's", OliAdministrator, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, closing it", AdaUser, "UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Closed' WHERE \"Id\" = 1", Expectation.Refused);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, suspending it", AdaUser, "UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Suspended' WHERE \"Id\" = 1", Expectation.Refused);
        yield return new("Tenants", "INSERT", "no one", AdaUser, "INSERT INTO tenancy.\"Tenants\" (\"Id\", \"IsDemo\", \"Version\", \"Slug\", \"Status\", \"Shape\") VALUES (9, false, 0, 'ninth', 'Active', 'Flat')", Expectation.Refused);
        yield return new("Tenants", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"Tenants\" WHERE \"Id\" = 1", Expectation.NoRows);

        // Organizations: read as tenants are; changed with the units permission anywhere or the settings permission for the whole tenant.
        yield return new("Organizations", "SELECT", "a seat, its tenant", OliUser, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "SELECT", "a seat, another tenant of the person's", OliUser, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 2", Expectation.Rows);
        yield return new("Organizations", "SELECT", "a seat, a tenant not the person's", AdaUser, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Organizations", "UPDATE", "units at a unit", SethUser, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "UPDATE", "settings for the whole tenant", AdaUser, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "UPDATE", "without either permission", HiroUser, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Organizations", "UPDATE", "settings for the whole tenant, another tenant of the person's", OliAdministrator, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Orchard Group' WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Organizations", "INSERT", "no one", AdaUser, "INSERT INTO tenancy.\"Organizations\" (\"Id\", \"Version\", \"Name\") VALUES (9, 0, 'Ninth')", Expectation.Refused);
        yield return new("Organizations", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"Organizations\" WHERE \"Id\" = 1", Expectation.NoRows);

        // Units: read by the tenant's members; added, renamed and moved with the units permission where the use case asks it.
        const string AddUnit = "INSERT INTO tenancy.\"OrganizationUnits\" (\"Id\", \"TenantId\", \"ParentId\", \"Name\", \"Status\") VALUES (gen_random_uuid(), {0}, {1}, 'Dock', 'Active')";
        yield return new("OrganizationUnits", "SELECT", "a seat of the tenant", OliUser, "SELECT count(*) FROM tenancy.\"OrganizationUnits\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("OrganizationUnits", "SELECT", "seated in another tenant", OdetteUser, "SELECT count(*) FROM tenancy.\"OrganizationUnits\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("OrganizationUnits", "INSERT", "units at the parent", SethUser, Args(AddUnit, 1, "{North}"), Expectation.Rows);
        yield return new("OrganizationUnits", "INSERT", "units elsewhere", SethUser, Args(AddUnit, 1, "{South}"), Expectation.Refused);
        yield return new("OrganizationUnits", "INSERT", "holding no such permission", OliUser, Args(AddUnit, 1, "{NorthPier}"), Expectation.Refused);
        yield return new("OrganizationUnits", "INSERT", "units at the root, another tenant", AdaUser, Args(AddUnit, 2, "{OrchardRoot}"), Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit and its parent", SethUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Pier' WHERE \"Id\" = {NorthPier}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit, not its parent", SethUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'North Coast' WHERE \"Id\" = {North}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit alone, not its parent", EveUnits, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'North Coast' WHERE \"Id\" = {North}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit alone, moving it", EveUnits, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {North}", Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units elsewhere", SethUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Southside' WHERE \"Id\" = {South}", Expectation.NoRows);
        yield return new("OrganizationUnits", "UPDATE", "holding no such permission", OliUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Pier' WHERE \"Id\" = {NorthPier}", Expectation.NoRows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit, not at the new parent", SethUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {NorthPier}", Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit and at the new parent", AdaUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {NorthPier}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the root, the root itself", AdaUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Harbour' WHERE \"Id\" = {Root}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units below the root, the root", SethUser, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Harbour' WHERE \"Id\" = {Root}", Expectation.NoRows);
        yield return new("OrganizationUnits", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = {NorthPier}", Expectation.NoRows);

        // The closure: read by the tenant's members; written with the units permission anywhere, the trigger checking the rest at commit.
        const string AddPath = "INSERT INTO tenancy.\"OrganizationUnitPaths\" (\"AncestorId\", \"DescendantId\", \"TenantId\", \"Distance\") VALUES ({0}, gen_random_uuid(), 1, 1)";
        const string OnePath = " WHERE \"AncestorId\" = {North} AND \"DescendantId\" = {NorthPier}";
        yield return new("OrganizationUnitPaths", "SELECT", "a seat of the tenant", OliUser, "SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("OrganizationUnitPaths", "SELECT", "seated in another tenant", OdetteUser, "SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("OrganizationUnitPaths", "INSERT", "units at the unit", SethUser, Args(AddPath, "{North}"), Expectation.Rows);
        yield return new("OrganizationUnitPaths", "INSERT", "units elsewhere, which the trigger checks", SethUser, Args(AddPath, "{South}"), Expectation.Rows);
        yield return new("OrganizationUnitPaths", "INSERT", "holding no such permission", OliUser, Args(AddPath, "{NorthPier}"), Expectation.Refused);
        yield return new("OrganizationUnitPaths", "UPDATE", "units held", SethUser, "UPDATE tenancy.\"OrganizationUnitPaths\" SET \"Distance\" = 1" + OnePath, Expectation.Rows);
        yield return new("OrganizationUnitPaths", "UPDATE", "holding no such permission", OliUser, "UPDATE tenancy.\"OrganizationUnitPaths\" SET \"Distance\" = 1" + OnePath, Expectation.NoRows);
        yield return new("OrganizationUnitPaths", "DELETE", "units held", SethUser, "DELETE FROM tenancy.\"OrganizationUnitPaths\"" + OnePath, Expectation.Rows);
        yield return new("OrganizationUnitPaths", "DELETE", "holding no such permission", OliUser, "DELETE FROM tenancy.\"OrganizationUnitPaths\"" + OnePath, Expectation.NoRows);

        // Seats: read by the tenant's members and by the person whose seat it is; added with the seats permission for the whole
        // tenant; changed by a seats or grants manager, or the seat itself.
        const string AddSeat = "INSERT INTO tenancy.\"Seats\" (\"Id\", \"Version\", \"TenantId\", \"Identity\", \"DisplayName\", \"Kind\", \"Status\") VALUES (gen_random_uuid(), 0, 1, gen_random_uuid(), 'Pat', 0, 'Active')";
        const string RenameSue = "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Susan' WHERE \"Id\" = {Sue}";
        yield return new("Seats", "SELECT", "a seat of the tenant", OliUser, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("Seats", "SELECT", "the person, their seat in another tenant", OliUser, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Id\" = {OliInOrchard}", Expectation.Rows);
        yield return new("Seats", "SELECT", "a seat, someone else's seat in another tenant", OliUser, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Id\" = {Odette}", Expectation.NoRows);
        yield return new("Seats", "INSERT", "seats for the whole tenant", AdaUser, AddSeat, Expectation.Rows);
        yield return new("Seats", "INSERT", "seats at a unit", SethUser, AddSeat, Expectation.Refused);
        yield return new("Seats", "INSERT", "holding no such permission", OliUser, AddSeat, Expectation.Refused);
        yield return new("Seats", "UPDATE", "seats held", SethUser, RenameSue, Expectation.Rows);
        yield return new("Seats", "UPDATE", "grants held", HiroUser, RenameSue, Expectation.Rows);
        yield return new("Seats", "UPDATE", "its own seat", OliUser, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Oliver' WHERE \"Id\" = {Oli}", Expectation.Rows);
        yield return new("Seats", "UPDATE", "without a permission, another seat", OliUser, RenameSue, Expectation.NoRows);
        yield return new("Seats", "UPDATE", "every permission, a seat of another tenant", AdaUser, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Odette' WHERE \"Id\" = {Odette}", Expectation.NoRows);
        yield return new("Seats", "UPDATE", "seats for the whole tenant, the person's own seat in another tenant", OliAdministrator, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Oliver' WHERE \"Id\" = {OliInOrchard}", Expectation.NoRows);
        yield return new("Seats", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"Seats\" WHERE \"Id\" = {Oli}", Expectation.NoRows);

        // Placements: read by the tenant's members; placed and withdrawn with the seats permission at the unit; made primary only
        // where it is held, and demoted wherever, since making one primary demotes the old one elsewhere.
        const string Place = "INSERT INTO tenancy.\"SeatPlacements\" (\"UnitId\", \"SeatId\", \"IsPrimary\", \"PlacedAt\", \"TenantId\") VALUES ({0}, {1}, false, now(), 1)";
        const string Primary = "UPDATE tenancy.\"SeatPlacements\" SET \"IsPrimary\" = {0} WHERE \"SeatId\" = {1} AND \"UnitId\" = {2}";
        const string Withdraw = "DELETE FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = {0} AND \"UnitId\" = {1}";
        yield return new("SeatPlacements", "SELECT", "a seat of the tenant", OliUser, "SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("SeatPlacements", "SELECT", "seated in another tenant", OdetteUser, "SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("SeatPlacements", "INSERT", "seats at the unit", SethUser, Args(Place, "{North}", "{Oli}"), Expectation.Rows);
        yield return new("SeatPlacements", "INSERT", "seats elsewhere", SethUser, Args(Place, "{South}", "{Oli}"), Expectation.Refused);
        yield return new("SeatPlacements", "INSERT", "holding no such permission", OliUser, Args(Place, "{NorthPier}", "{Eve}"), Expectation.Refused);
        yield return new("SeatPlacements", "INSERT", "seats at the unit, a seat of another tenant", SethUser, Args(Place, "{North}", "{Odette}"), Expectation.Refused);
        yield return new("SeatPlacements", "UPDATE", "seats held, demoting elsewhere", SethUser, Args(Primary, "false", "{Eve}", "{South}"), Expectation.Rows);
        yield return new("SeatPlacements", "UPDATE", "seats at the unit, making it primary", SethUser, Args(Primary, "true", "{Seth}", "{NorthPier}"), Expectation.Rows, Args(Primary, "false", "{Seth}", "{North}"));
        yield return new("SeatPlacements", "UPDATE", "seats elsewhere, making it primary", SethUser, Args(Primary, "true", "{Eve}", "{South}"), Expectation.Refused, Args(Primary, "false", "{Eve}", "{South}"));
        yield return new("SeatPlacements", "UPDATE", "holding no such permission", OliUser, "UPDATE tenancy.\"SeatPlacements\" SET \"PlacedBy\" = {Oli} WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatPlacements", "UPDATE", "seats held, moving it to another unit", SethUser, "UPDATE tenancy.\"SeatPlacements\" SET \"UnitId\" = {South} WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "UPDATE", "seats held, giving it to another seat", SethUser, "UPDATE tenancy.\"SeatPlacements\" SET \"SeatId\" = {Eve} WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "UPDATE", "seats held, into another tenant", SethUser, "UPDATE tenancy.\"SeatPlacements\" SET \"TenantId\" = 2 WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "DELETE", "seats at the unit", SethUser, Args(Withdraw, "{Seth}", "{NorthPier}"), Expectation.Rows);
        yield return new("SeatPlacements", "DELETE", "seats elsewhere", SethUser, Args(Withdraw, "{Eve}", "{South}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "holding no such permission", OliUser, Args(Withdraw, "{Seth}", "{NorthPier}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "seats at the unit, a placement that has grants", SethUser, Args(Withdraw, "{Hiro}", "{North}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "seats alone at the unit, a placement that has grants", EveSeats, Args(Withdraw, "{Hiro}", "{North}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "seats at the unit, once its grants are gone", SethUser, Args(Withdraw, "{Hiro}", "{North}"), Expectation.Rows, "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Hiro} AND \"UnitId\" = {North}");
        yield return new("SeatPlacements", "DELETE", "its own, a placement that has grants", OliUser, Args(Withdraw, "{Oli}", "{NorthPier}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "its own, once its grants are gone", OliUser, Args(Withdraw, "{Oli}", "{NorthPier}"), Expectation.Rows, "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Oli} AND \"UnitId\" = {NorthPier}");

        // Grants: read by their own seat, and by a seat that manages grants, seats or units at the grant's unit, held there
        // or above it, or roles for the whole tenant, in the seat's tenant: a permission reads only where it applies. Given,
        // changed and taken away with the grants permission at the grant's unit, in the caller's own name when given. The roles
        // that manage access have tests of their own.
        const string Give = "INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ({Watcher}, {0}, {1}, now(), NULL, {2}, NULL)";
        const string OliOperates = " WHERE \"SeatId\" = {Oli} AND \"RoleId\" = {Operator}";
        const string EveOperates = " WHERE \"SeatId\" = {Eve} AND \"RoleId\" = {Operator}";
        const string SethSupervises = " WHERE \"SeatId\" = {Seth} AND \"RoleId\" = {Supervisor}";
        const string HiroGives = " WHERE \"SeatId\" = {Hiro} AND \"RoleId\" = {GrantsDesk}";
        const string AdasGrants = "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Ada}";
        const string OlisGrants = "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Oli}";
        const string SuesGrants = "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Sue}";
        yield return new("SeatRoleGrants", "SELECT", "its own", OliUser, OlisGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, managing nothing", OliUser, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, nothing that applies now", EveUser, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's below, grants held above it", HiroUser, OlisGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, grants held below it", HiroUser, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's beside, grants held elsewhere", HiroUser, SuesGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's below, seats held above it", EveSeats, OlisGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, seats held below it", EveSeats, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's below, units held above it", EveUnits, OlisGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, units held below it", EveUnits, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, seats for the whole tenant", EveSeatsForTheTenant, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, roles for the whole tenant", EveRolesForTheTenant, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's below, roles below the root", EveRoles, OlisGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's at the root, roles below the root", EveRoles, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "seated in another tenant", OdetteUser, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "every permission, a seat of another tenant of the person's", OliAdministrator, "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Odette}", Expectation.NoRows);
        yield return new("SeatRoleGrants", "INSERT", "grants at the unit", HiroUser, Args(Give, "{Oli}", "{NorthPier}", "{Hiro}"), Expectation.Rows);
        yield return new("SeatRoleGrants", "INSERT", "grants elsewhere", HiroUser, Args(Give, "{Eve}", "{South}", "{Hiro}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "INSERT", "holding no such permission", OliUser, Args(Give, "{Seth}", "{NorthPier}", "{Oli}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "INSERT", "grants at the unit, in someone else's name", HiroUser, Args(Give, "{Oli}", "{NorthPier}", "{Ada}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "UPDATE", "grants elsewhere", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + EveOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "UPDATE", "holding no such permission", OliUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + OliOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another role", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"RoleId\" = {Watcher}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another unit", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"UnitId\" = {North}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another giver", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"GrantedBy\" = {Hiro}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, a role that manages access without its permissions", HiroUser, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit", HiroUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "DELETE", "grants elsewhere", HiroUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + EveOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "holding no such permission", OliUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "holding no such permission, its own", OliUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit, a role that manages access without its permissions", HiroUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit, a role that manages access with its permissions", SethUser, "DELETE FROM tenancy.\"SeatRoleGrants\"" + HiroGives, Expectation.Rows);

        // Rights: a seat reads its own, and no other seat's, whatever it manages. No caller writes one: the database
        // writes them as the grants, the seats and the roles they follow from are written.
        const string Right = "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Permission\", \"TenantId\", \"StartsAt\", \"EndsAt\") VALUES ({0}, {1}, {Watcher}, 'widget.read', 1, now(), NULL)";
        const string End = "UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = now() + interval '1 day'";
        yield return new("SeatRights", "SELECT", "its own", OliUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.Rows);
        yield return new("SeatRights", "SELECT", "another seat's, managing nothing", OliUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "another seat's, grants and seats held at its unit", SethUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "another seat's, every permission for the whole tenant", AdaUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "the person's own, in another tenant", OliUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {OliInOrchard}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "seated in another tenant", OdetteUser, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "INSERT", "no one, with every permission", AdaUser, Args(Right, "{Oli}", "{South}"), Expectation.Refused);
        yield return new("SeatRights", "INSERT", "no one, for itself", OliUser, Args(Right, "{Oli}", "{NorthPier}"), Expectation.Refused);
        yield return new("SeatRights", "UPDATE", "no one, with every permission", AdaUser, End + OliOperates, Expectation.NoRows);
        yield return new("SeatRights", "UPDATE", "no one, its own", AdaUser, End + " WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "DELETE", "no one, with every permission", AdaUser, "DELETE FROM tenancy.\"SeatRights\"" + OliOperates, Expectation.NoRows);
        yield return new("SeatRights", "DELETE", "no one, its own", OliUser, "DELETE FROM tenancy.\"SeatRights\"" + OliOperates, Expectation.NoRows);

        // Roles: read by the tenant's members; added with the role or settings permission for the whole tenant, changed with the
        // role permission for the whole tenant. A role made by hand names no starter role and remembers nothing a starter role gave it; one that
        // names a starter role is a copy of it, which remembers exactly the starter role's permissions. Which starter role a role was made from stays,
        // and what the starter role gave it no seat writes: a sync of the starter roles compares the starter role with it.
        const string AddRole = "INSERT INTO tenancy.\"Roles\" (\"Id\", \"NormalizedName\", \"Version\", \"TenantId\", \"Name\", \"Description\", \"FromStarterRole\", \"Status\", \"Permissions\") VALUES (gen_random_uuid(), 'CLERK', 0, 1, 'Clerk', '', NULL, 'Active', ARRAY['widget.read'])";
        const string CopyStarterRole = "INSERT INTO tenancy.\"Roles\" (\"Id\", \"NormalizedName\", \"Version\", \"TenantId\", \"Name\", \"Description\", \"FromStarterRole\", \"Status\", \"Permissions\", \"PermissionsFromStarterRole\") VALUES (gen_random_uuid(), 'LOOKOUT', 0, 1, 'Lookout', '', {0}, 'Active', ARRAY[{1}], {2})";
        const string DescribeWatcher = "UPDATE tenancy.\"Roles\" SET \"Description\" = 'Looks on' WHERE \"Id\" = {Watcher}";
        const string Watched = "ARRAY['widget.read']";
        yield return new("Roles", "SELECT", "a seat of the tenant", OliUser, "SELECT count(*) FROM tenancy.\"Roles\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("Roles", "SELECT", "seated in another tenant", OdetteUser, "SELECT count(*) FROM tenancy.\"Roles\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("Roles", "INSERT", "roles for the whole tenant", AdaUser, AddRole, Expectation.Rows);
        yield return new("Roles", "INSERT", "roles and settings below the root", HiroNorth, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "without the permissions", OliUser, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy of a starter role", HiroSettings, Args(CopyStarterRole, "'watcher'", "'widget.read'", Watched), Expectation.Rows);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy of a starter role, its permissions in another order", HiroSettings, Args(CopyStarterRole, "'operator'", "'widget.read', 'widget.create', 'widget.change'", "ARRAY['widget.create', 'widget.read', 'widget.change']"), Expectation.Rows);
        yield return new("Roles", "INSERT", "settings for the whole tenant, permissions of no starter role", HiroSettings, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, more permissions than its starter role", HiroSettings, Args(CopyStarterRole, "'watcher'", "'widget.read', 'tenancy.roles.manage'", Watched), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, fewer permissions than its starter role", HiroSettings, Args(CopyStarterRole, "'operator'", "'widget.change'", "ARRAY['widget.change', 'widget.read', 'widget.create']"), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a starter role the catalogue does not have", HiroSettings, Args(CopyStarterRole, "'lookouts'", "'widget.read'", Watched), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy that remembers nothing of its starter role", HiroSettings, Args(CopyStarterRole, "'watcher'", "'widget.read'", "NULL"), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy that remembers more than its starter role gave", HiroSettings, Args(CopyStarterRole, "'watcher'", "'widget.read'", "ARRAY['widget.read', 'tenancy.roles.manage']"), Expectation.Refused);
        yield return new("Roles", "INSERT", "roles for the whole tenant, a copy of a starter role", AdaUser, Args(CopyStarterRole, "'watcher'", "'widget.read'", Watched), Expectation.Rows);
        yield return new("Roles", "INSERT", "roles for the whole tenant, naming a starter role it is no copy of", AdaUser, Args(CopyStarterRole, "'watcher'", "'widget.read', 'widget.create'", Watched), Expectation.Refused);
        yield return new("Roles", "INSERT", "roles for the whole tenant, by hand, remembering what a starter role gave", AdaUser, Args(CopyStarterRole, "NULL", "'widget.read'", Watched), Expectation.Refused);
        yield return new("Roles", "UPDATE", "roles for the whole tenant", AdaUser, DescribeWatcher, Expectation.Rows);
        yield return new("Roles", "UPDATE", "roles below the root", HiroNorth, DescribeWatcher, Expectation.NoRows);
        yield return new("Roles", "UPDATE", "holding no such permission", OliUser, DescribeWatcher, Expectation.NoRows);
        yield return new("Roles", "UPDATE", "roles for the whole tenant, its permissions and not what its starter role gave it", AdaUser, "UPDATE tenancy.\"Roles\" SET \"Permissions\" = ARRAY['widget.read', 'widget.create'] WHERE \"Id\" = {Watcher}", Expectation.Rows);
        yield return new("Roles", "UPDATE", "roles for the whole tenant, what its starter role gave it", AdaUser, "UPDATE tenancy.\"Roles\" SET \"PermissionsFromStarterRole\" = ARRAY['widget.read', 'widget.create'] WHERE \"Id\" = {Watcher}", Expectation.Refused);
        yield return new("Roles", "UPDATE", "roles for the whole tenant, the starter role it was made from", AdaUser, "UPDATE tenancy.\"Roles\" SET \"FromStarterRole\" = 'operator' WHERE \"Id\" = {Watcher}", Expectation.Fixed);
        yield return new("Roles", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"Roles\" WHERE \"Id\" = {Watcher}", Expectation.NoRows);

        // The access revision: a counter, read and taken by whoever has a seat, of any status, in the tenant the connection
        // names, since a seat that takes its own permissions away, or suspends itself, takes it once it no longer holds them.
        const string Take = "UPDATE tenancy.\"TenancyAccessRevisions\" SET \"Revision\" = \"Revision\" + 1 WHERE \"TenantId\" = 1";
        const string ReadRevision = "SELECT count(*) FROM tenancy.\"TenancyAccessRevisions\" WHERE \"TenantId\" = 1";
        yield return new("TenancyAccessRevisions", "SELECT", "a seat of the tenant", OliUser, ReadRevision, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "SELECT", "a suspended seat of the tenant", SueUser, ReadRevision, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "SELECT", "seated in another tenant", OdetteUser, ReadRevision, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "SELECT", "a seat here, acting in another tenant", OliActingInOrchard, ReadRevision, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "UPDATE", "grants held", HiroUser, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "without an access permission", OliUser, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "nothing that applies now", EveUser, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "a suspended seat of the tenant", SueUser, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "seated in another tenant", OdetteUser, Take, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "UPDATE", "a seat here, acting in another tenant", OliActingInOrchard, Take, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "INSERT", "no one", AdaUser, "INSERT INTO tenancy.\"TenancyAccessRevisions\" (\"TenantId\", \"Revision\") VALUES (9, 0)", Expectation.Refused);
        yield return new("TenancyAccessRevisions", "DELETE", "no one", AdaUser, "DELETE FROM tenancy.\"TenancyAccessRevisions\" WHERE \"TenantId\" = 1", Expectation.NoRows);
    }
}
