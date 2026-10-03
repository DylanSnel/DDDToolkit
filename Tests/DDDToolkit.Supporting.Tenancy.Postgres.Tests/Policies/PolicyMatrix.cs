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
/// Where in Harbor they are given a role first, placed there if they are not: the Administrator role, every key there and
/// below, and at a unit below the root nothing for the whole tenant; or, with <paramref name="Keys"/>, a role holding those
/// alone.
/// </param>
/// <param name="Keys">The keys of the role they are given at <paramref name="At"/>, or <see langword="null"/> for the Administrator role.</param>
public sealed record MatrixCaller(string Name, Person Person, TenantId Tenant, OrganizationUnitId? At = null, string[]? Keys = null);

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
public sealed record PolicyCell(string Table, string Command, string Holds, MatrixCaller Caller, string Sql, Expectation Expected, params string[] Before)
{
    public string Name => $"{Table} {Command}: {Holds} ({Caller.Name})";
}

/// <summary>
/// The cells of the policies on Tenancy's tables, each with a caller that holds the key where the command asks it, one
/// that holds it elsewhere, and one without it, where the cell asks a key; with a seat of the tenant and one of another where it asks
/// only the tenant; and with the seat a row is of and another seat where only its own seat reads it. Harbor as seeded: Ada administers it, Seth supervises North (units, seats, grants and widgets),
/// Hiro gives roles at North, Oli operates widgets at North Pier, Sue is suspended, and Eve holds nothing that applies
/// now; Odette administers Orchard, where Oli has a seat too. A caller given a role first gets a database of its own.
/// </summary>
public static class PolicyMatrix
{
    private static readonly MatrixCaller AdaCaller = new("Ada, administrator", Ada, Harbor);
    private static readonly MatrixCaller SethCaller = new("Seth, units, seats and grants at North", Seth, Harbor);
    private static readonly MatrixCaller HiroCaller = new("Hiro, grants at North", Hiro, Harbor);
    private static readonly MatrixCaller HiroNorth = new("Hiro, every key at North", Hiro, Harbor, At: North);
    private static readonly MatrixCaller HiroSettings = new("Hiro, settings for the whole tenant too", Hiro, Harbor, At: HarborRoot, Keys: [TenancyKeys.SettingsManage]);
    private static readonly MatrixCaller OliAdministrator = new("Oli, administrator, with a seat in Orchard too", Oli, Harbor, At: HarborRoot);
    private static readonly MatrixCaller OliCaller = new("Oli, widgets at North Pier", Oli, Harbor);
    private static readonly MatrixCaller OliActingInOrchard = new("Oli, in Orchard", Oli, Orchard);
    private static readonly MatrixCaller EveCaller = new("Eve, nothing now", Eve, Harbor);
    private static readonly MatrixCaller EveSeats = new("Eve, seats at North", Eve, Harbor, At: North, Keys: [TenancyKeys.SeatsManage]);
    private static readonly MatrixCaller EveUnits = new("Eve, units at North", Eve, Harbor, At: North, Keys: [TenancyKeys.UnitsManage]);
    private static readonly MatrixCaller EveRoles = new("Eve, roles at North", Eve, Harbor, At: North, Keys: [TenancyKeys.RolesManage]);
    private static readonly MatrixCaller EveRolesForTheTenant = new("Eve, roles for the whole tenant", Eve, Harbor, At: HarborRoot, Keys: [TenancyKeys.RolesManage]);
    private static readonly MatrixCaller SueCaller = new("Sue, suspended", Sue, Harbor);
    private static readonly MatrixCaller OdetteCaller = new("Odette, of Orchard", Odette, Orchard);

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
        // Tenants: read by its members and by whoever has a seat there; changed with the settings key for the whole tenant.
        yield return new("Tenants", "SELECT", "a seat, its tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Tenants", "SELECT", "a seat, another tenant of the person's", OliCaller, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 2", Expectation.Rows);
        yield return new("Tenants", "SELECT", "a seat, a tenant not the person's", AdaCaller, "SELECT count(*) FROM tenancy.\"Tenants\" WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant", AdaCaller, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Tenants", "UPDATE", "settings below the root", HiroNorth, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "holding no such key", OliCaller, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, another tenant", AdaCaller, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, another tenant of the person's", OliAdministrator, "UPDATE tenancy.\"Tenants\" SET \"IsDemo\" = true WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, closing it", AdaCaller, "UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Closed' WHERE \"Id\" = 1", Expectation.Refused);
        yield return new("Tenants", "UPDATE", "settings for the whole tenant, suspending it", AdaCaller, "UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Suspended' WHERE \"Id\" = 1", Expectation.Refused);
        yield return new("Tenants", "INSERT", "no one", AdaCaller, "INSERT INTO tenancy.\"Tenants\" (\"Id\", \"IsDemo\", \"Version\", \"Slug\", \"Status\", \"Shape\") VALUES (9, false, 0, 'ninth', 'Active', 'Flat')", Expectation.Refused);
        yield return new("Tenants", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"Tenants\" WHERE \"Id\" = 1", Expectation.NoRows);

        // Organizations: read as tenants are; changed with the units key anywhere or the settings key for the whole tenant.
        yield return new("Organizations", "SELECT", "a seat, its tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "SELECT", "a seat, another tenant of the person's", OliCaller, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 2", Expectation.Rows);
        yield return new("Organizations", "SELECT", "a seat, a tenant not the person's", AdaCaller, "SELECT count(*) FROM tenancy.\"Organizations\" WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Organizations", "UPDATE", "units at a unit", SethCaller, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "UPDATE", "settings for the whole tenant", AdaCaller, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.Rows);
        yield return new("Organizations", "UPDATE", "without either key", HiroCaller, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Harbor Group' WHERE \"Id\" = 1", Expectation.NoRows);
        yield return new("Organizations", "UPDATE", "settings for the whole tenant, another tenant of the person's", OliAdministrator, "UPDATE tenancy.\"Organizations\" SET \"Name\" = 'Orchard Group' WHERE \"Id\" = 2", Expectation.NoRows);
        yield return new("Organizations", "INSERT", "no one", AdaCaller, "INSERT INTO tenancy.\"Organizations\" (\"Id\", \"Version\", \"Name\") VALUES (9, 0, 'Ninth')", Expectation.Refused);
        yield return new("Organizations", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"Organizations\" WHERE \"Id\" = 1", Expectation.NoRows);

        // Units: read by the tenant's members; added, renamed and moved with the units key where the use case asks it.
        const string AddUnit = "INSERT INTO tenancy.\"OrganizationUnits\" (\"Id\", \"TenantId\", \"ParentId\", \"Name\", \"Kind\", \"Status\") VALUES (gen_random_uuid(), {0}, {1}, 'Dock', 'site', 'Active')";
        yield return new("OrganizationUnits", "SELECT", "a seat of the tenant", OliCaller, "SELECT count(*) FROM tenancy.\"OrganizationUnits\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("OrganizationUnits", "SELECT", "seated in another tenant", OdetteCaller, "SELECT count(*) FROM tenancy.\"OrganizationUnits\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("OrganizationUnits", "INSERT", "units at the parent", SethCaller, Args(AddUnit, 1, "{North}"), Expectation.Rows);
        yield return new("OrganizationUnits", "INSERT", "units elsewhere", SethCaller, Args(AddUnit, 1, "{South}"), Expectation.Refused);
        yield return new("OrganizationUnits", "INSERT", "holding no such key", OliCaller, Args(AddUnit, 1, "{NorthPier}"), Expectation.Refused);
        yield return new("OrganizationUnits", "INSERT", "units at the root, another tenant", AdaCaller, Args(AddUnit, 2, "{OrchardRoot}"), Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit and its parent", SethCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Pier' WHERE \"Id\" = {NorthPier}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit, not its parent", SethCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'North Coast' WHERE \"Id\" = {North}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit alone, not its parent", EveUnits, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'North Coast' WHERE \"Id\" = {North}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit alone, moving it", EveUnits, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {North}", Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units elsewhere", SethCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Southside' WHERE \"Id\" = {South}", Expectation.NoRows);
        yield return new("OrganizationUnits", "UPDATE", "holding no such key", OliCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Pier' WHERE \"Id\" = {NorthPier}", Expectation.NoRows);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit, not at the new parent", SethCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {NorthPier}", Expectation.Refused);
        yield return new("OrganizationUnits", "UPDATE", "units at the unit and at the new parent", AdaCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"ParentId\" = {South} WHERE \"Id\" = {NorthPier}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units at the root, the root itself", AdaCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Harbour' WHERE \"Id\" = {Root}", Expectation.Rows);
        yield return new("OrganizationUnits", "UPDATE", "units below the root, the root", SethCaller, "UPDATE tenancy.\"OrganizationUnits\" SET \"Name\" = 'Harbour' WHERE \"Id\" = {Root}", Expectation.NoRows);
        yield return new("OrganizationUnits", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = {NorthPier}", Expectation.NoRows);

        // The closure: read by the tenant's members; written with the units key anywhere, the trigger checking the rest at commit.
        const string AddPath = "INSERT INTO tenancy.\"OrganizationUnitPaths\" (\"AncestorId\", \"DescendantId\", \"TenantId\", \"Distance\") VALUES ({0}, gen_random_uuid(), 1, 1)";
        const string OnePath = " WHERE \"AncestorId\" = {North} AND \"DescendantId\" = {NorthPier}";
        yield return new("OrganizationUnitPaths", "SELECT", "a seat of the tenant", OliCaller, "SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("OrganizationUnitPaths", "SELECT", "seated in another tenant", OdetteCaller, "SELECT count(*) FROM tenancy.\"OrganizationUnitPaths\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("OrganizationUnitPaths", "INSERT", "units at the unit", SethCaller, Args(AddPath, "{North}"), Expectation.Rows);
        yield return new("OrganizationUnitPaths", "INSERT", "units elsewhere, which the trigger checks", SethCaller, Args(AddPath, "{South}"), Expectation.Rows);
        yield return new("OrganizationUnitPaths", "INSERT", "holding no such key", OliCaller, Args(AddPath, "{NorthPier}"), Expectation.Refused);
        yield return new("OrganizationUnitPaths", "UPDATE", "units held", SethCaller, "UPDATE tenancy.\"OrganizationUnitPaths\" SET \"Distance\" = 1" + OnePath, Expectation.Rows);
        yield return new("OrganizationUnitPaths", "UPDATE", "holding no such key", OliCaller, "UPDATE tenancy.\"OrganizationUnitPaths\" SET \"Distance\" = 1" + OnePath, Expectation.NoRows);
        yield return new("OrganizationUnitPaths", "DELETE", "units held", SethCaller, "DELETE FROM tenancy.\"OrganizationUnitPaths\"" + OnePath, Expectation.Rows);
        yield return new("OrganizationUnitPaths", "DELETE", "holding no such key", OliCaller, "DELETE FROM tenancy.\"OrganizationUnitPaths\"" + OnePath, Expectation.NoRows);

        // Seats: read by the tenant's members and by the person whose seat it is; added with the seats key for the whole
        // tenant; changed by a seats or grants manager, or the seat itself.
        const string AddSeat = "INSERT INTO tenancy.\"Seats\" (\"Id\", \"Version\", \"TenantId\", \"Identity\", \"DisplayName\", \"Status\") VALUES (gen_random_uuid(), 0, 1, gen_random_uuid(), 'Pat', 'Active')";
        const string RenameSue = "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Susan' WHERE \"Id\" = {Sue}";
        yield return new("Seats", "SELECT", "a seat of the tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("Seats", "SELECT", "the person, their seat in another tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Id\" = {OliInOrchard}", Expectation.Rows);
        yield return new("Seats", "SELECT", "a seat, someone else's seat in another tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Seats\" WHERE \"Id\" = {Odette}", Expectation.NoRows);
        yield return new("Seats", "INSERT", "seats for the whole tenant", AdaCaller, AddSeat, Expectation.Rows);
        yield return new("Seats", "INSERT", "seats at a unit", SethCaller, AddSeat, Expectation.Refused);
        yield return new("Seats", "INSERT", "holding no such key", OliCaller, AddSeat, Expectation.Refused);
        yield return new("Seats", "UPDATE", "seats held", SethCaller, RenameSue, Expectation.Rows);
        yield return new("Seats", "UPDATE", "grants held", HiroCaller, RenameSue, Expectation.Rows);
        yield return new("Seats", "UPDATE", "its own seat", OliCaller, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Oliver' WHERE \"Id\" = {Oli}", Expectation.Rows);
        yield return new("Seats", "UPDATE", "without a key, another seat", OliCaller, RenameSue, Expectation.NoRows);
        yield return new("Seats", "UPDATE", "every key, a seat of another tenant", AdaCaller, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Odette' WHERE \"Id\" = {Odette}", Expectation.NoRows);
        yield return new("Seats", "UPDATE", "seats for the whole tenant, the person's own seat in another tenant", OliAdministrator, "UPDATE tenancy.\"Seats\" SET \"DisplayName\" = 'Oliver' WHERE \"Id\" = {OliInOrchard}", Expectation.NoRows);
        yield return new("Seats", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"Seats\" WHERE \"Id\" = {Oli}", Expectation.NoRows);

        // Placements: read by the tenant's members; placed and withdrawn with the seats key at the unit; made primary only
        // where it is held, and demoted wherever, since making one primary demotes the old one elsewhere.
        const string Place = "INSERT INTO tenancy.\"SeatPlacements\" (\"UnitId\", \"SeatId\", \"IsPrimary\", \"PlacedAt\", \"TenantId\") VALUES ({0}, {1}, false, now(), 1)";
        const string Primary = "UPDATE tenancy.\"SeatPlacements\" SET \"IsPrimary\" = {0} WHERE \"SeatId\" = {1} AND \"UnitId\" = {2}";
        const string Withdraw = "DELETE FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = {0} AND \"UnitId\" = {1}";
        yield return new("SeatPlacements", "SELECT", "a seat of the tenant", OliCaller, "SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("SeatPlacements", "SELECT", "seated in another tenant", OdetteCaller, "SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("SeatPlacements", "INSERT", "seats at the unit", SethCaller, Args(Place, "{North}", "{Oli}"), Expectation.Rows);
        yield return new("SeatPlacements", "INSERT", "seats elsewhere", SethCaller, Args(Place, "{South}", "{Oli}"), Expectation.Refused);
        yield return new("SeatPlacements", "INSERT", "holding no such key", OliCaller, Args(Place, "{NorthPier}", "{Eve}"), Expectation.Refused);
        yield return new("SeatPlacements", "INSERT", "seats at the unit, a seat of another tenant", SethCaller, Args(Place, "{North}", "{Odette}"), Expectation.Refused);
        yield return new("SeatPlacements", "UPDATE", "seats held, demoting elsewhere", SethCaller, Args(Primary, "false", "{Eve}", "{South}"), Expectation.Rows);
        yield return new("SeatPlacements", "UPDATE", "seats at the unit, making it primary", SethCaller, Args(Primary, "true", "{Seth}", "{NorthPier}"), Expectation.Rows, Args(Primary, "false", "{Seth}", "{North}"));
        yield return new("SeatPlacements", "UPDATE", "seats elsewhere, making it primary", SethCaller, Args(Primary, "true", "{Eve}", "{South}"), Expectation.Refused, Args(Primary, "false", "{Eve}", "{South}"));
        yield return new("SeatPlacements", "UPDATE", "holding no such key", OliCaller, "UPDATE tenancy.\"SeatPlacements\" SET \"PlacedBy\" = {Oli} WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatPlacements", "UPDATE", "seats held, moving it to another unit", SethCaller, "UPDATE tenancy.\"SeatPlacements\" SET \"UnitId\" = {South} WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "UPDATE", "seats held, giving it to another seat", SethCaller, "UPDATE tenancy.\"SeatPlacements\" SET \"SeatId\" = {Eve} WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "UPDATE", "seats held, into another tenant", SethCaller, "UPDATE tenancy.\"SeatPlacements\" SET \"TenantId\" = 2 WHERE \"SeatId\" = {Seth} AND \"UnitId\" = {NorthPier}", Expectation.Fixed);
        yield return new("SeatPlacements", "DELETE", "seats at the unit", SethCaller, Args(Withdraw, "{Seth}", "{NorthPier}"), Expectation.Rows);
        yield return new("SeatPlacements", "DELETE", "seats elsewhere", SethCaller, Args(Withdraw, "{Eve}", "{South}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "holding no such key", OliCaller, Args(Withdraw, "{Seth}", "{NorthPier}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "seats at the unit, a placement that has grants", SethCaller, Args(Withdraw, "{Hiro}", "{North}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "seats at the unit, once its grants are gone", SethCaller, Args(Withdraw, "{Hiro}", "{North}"), Expectation.Rows, "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Hiro} AND \"UnitId\" = {North}");
        yield return new("SeatPlacements", "DELETE", "its own, a placement that has grants", OliCaller, Args(Withdraw, "{Oli}", "{NorthPier}"), Expectation.NoRows);
        yield return new("SeatPlacements", "DELETE", "its own, once its grants are gone", OliCaller, Args(Withdraw, "{Oli}", "{NorthPier}"), Expectation.Rows, "DELETE FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Oli} AND \"UnitId\" = {NorthPier}");

        // Grants: read by their own seat, and by a seat that manages grants, seats or units somewhere, or roles for the
        // whole tenant, in the seat's tenant; given, changed and taken away with the grants key at the grant's unit, in
        // the caller's own name when given. The roles that manage access have tests of their own.
        const string Give = "INSERT INTO tenancy.\"SeatRoleGrants\" (\"RoleId\", \"SeatId\", \"UnitId\", \"StartsAt\", \"EndsAt\", \"GrantedBy\", \"Reason\") VALUES ({Watcher}, {0}, {1}, now(), NULL, {2}, NULL)";
        const string OliOperates = " WHERE \"SeatId\" = {Oli} AND \"RoleId\" = {Operator}";
        const string EveOperates = " WHERE \"SeatId\" = {Eve} AND \"RoleId\" = {Operator}";
        const string SethSupervises = " WHERE \"SeatId\" = {Seth} AND \"RoleId\" = {Supervisor}";
        const string HiroGives = " WHERE \"SeatId\" = {Hiro} AND \"RoleId\" = {GrantsDesk}";
        const string AdasGrants = "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Ada}";
        yield return new("SeatRoleGrants", "SELECT", "its own", OliCaller, "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Oli}", Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, managing nothing", OliCaller, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, nothing that applies now", EveCaller, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, grants held somewhere", HiroCaller, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, seats held somewhere", EveSeats, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, units held somewhere", EveUnits, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, roles for the whole tenant", EveRolesForTheTenant, AdasGrants, Expectation.Rows);
        yield return new("SeatRoleGrants", "SELECT", "another seat's, roles below the root", EveRoles, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "seated in another tenant", OdetteCaller, AdasGrants, Expectation.NoRows);
        yield return new("SeatRoleGrants", "SELECT", "every key, a seat of another tenant of the person's", OliAdministrator, "SELECT count(*) FROM tenancy.\"SeatRoleGrants\" WHERE \"SeatId\" = {Odette}", Expectation.NoRows);
        yield return new("SeatRoleGrants", "INSERT", "grants at the unit", HiroCaller, Args(Give, "{Oli}", "{NorthPier}", "{Hiro}"), Expectation.Rows);
        yield return new("SeatRoleGrants", "INSERT", "grants elsewhere", HiroCaller, Args(Give, "{Eve}", "{South}", "{Hiro}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "INSERT", "holding no such key", OliCaller, Args(Give, "{Seth}", "{NorthPier}", "{Oli}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "INSERT", "grants at the unit, in someone else's name", HiroCaller, Args(Give, "{Oli}", "{NorthPier}", "{Ada}"), Expectation.Refused);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "UPDATE", "grants elsewhere", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + EveOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "UPDATE", "holding no such key", OliCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + OliOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another role", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"RoleId\" = {Watcher}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another unit", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"UnitId\" = {North}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, another giver", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"GrantedBy\" = {Hiro}" + OliOperates, Expectation.Fixed);
        yield return new("SeatRoleGrants", "UPDATE", "grants at the unit, a role that manages access without its keys", HiroCaller, "UPDATE tenancy.\"SeatRoleGrants\" SET \"Reason\" = 'Standing in'" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit", HiroCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "DELETE", "grants elsewhere", HiroCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + EveOperates, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "holding no such key", OliCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "holding no such key, its own", OliCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + OliOperates, Expectation.Rows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit, a role that manages access without its keys", HiroCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + SethSupervises, Expectation.NoRows);
        yield return new("SeatRoleGrants", "DELETE", "grants at the unit, a role that manages access with its keys", SethCaller, "DELETE FROM tenancy.\"SeatRoleGrants\"" + HiroGives, Expectation.Rows);

        // Rights: a seat reads its own, and no other seat's, whatever it manages. No caller writes one: the database
        // writes them as the grants, the seats and the roles they follow from are written.
        const string Right = "INSERT INTO tenancy.\"SeatRights\" (\"SeatId\", \"UnitId\", \"RoleId\", \"Key\", \"TenantId\", \"StartsAt\", \"EndsAt\") VALUES ({0}, {1}, {Watcher}, 'widget.read', 1, now(), NULL)";
        const string End = "UPDATE tenancy.\"SeatRights\" SET \"EndsAt\" = now() + interval '1 day'";
        yield return new("SeatRights", "SELECT", "its own", OliCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.Rows);
        yield return new("SeatRights", "SELECT", "another seat's, managing nothing", OliCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "another seat's, grants and seats held at its unit", SethCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "another seat's, every key for the whole tenant", AdaCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Oli}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "the person's own, in another tenant", OliCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {OliInOrchard}", Expectation.NoRows);
        yield return new("SeatRights", "SELECT", "seated in another tenant", OdetteCaller, "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "INSERT", "no one, with every key", AdaCaller, Args(Right, "{Oli}", "{South}"), Expectation.Refused);
        yield return new("SeatRights", "INSERT", "no one, for itself", OliCaller, Args(Right, "{Oli}", "{NorthPier}"), Expectation.Refused);
        yield return new("SeatRights", "UPDATE", "no one, with every key", AdaCaller, End + OliOperates, Expectation.NoRows);
        yield return new("SeatRights", "UPDATE", "no one, its own", AdaCaller, End + " WHERE \"SeatId\" = {Ada}", Expectation.NoRows);
        yield return new("SeatRights", "DELETE", "no one, with every key", AdaCaller, "DELETE FROM tenancy.\"SeatRights\"" + OliOperates, Expectation.NoRows);
        yield return new("SeatRights", "DELETE", "no one, its own", OliCaller, "DELETE FROM tenancy.\"SeatRights\"" + OliOperates, Expectation.NoRows);

        // Roles: read by the tenant's members; added with the role or settings key for the whole tenant, changed with the
        // role key for the whole tenant.
        const string AddRole = "INSERT INTO tenancy.\"Roles\" (\"Id\", \"NormalizedName\", \"Version\", \"TenantId\", \"Name\", \"Description\", \"FromPack\", \"Status\", \"Keys\") VALUES (gen_random_uuid(), 'CLERK', 0, 1, 'Clerk', '', NULL, 'Active', ARRAY['widget.read'])";
        const string CopyPack = "INSERT INTO tenancy.\"Roles\" (\"Id\", \"NormalizedName\", \"Version\", \"TenantId\", \"Name\", \"Description\", \"FromPack\", \"Status\", \"Keys\") VALUES (gen_random_uuid(), 'LOOKOUT', 0, 1, 'Lookout', '', {0}, 'Active', ARRAY[{1}])";
        const string DescribeWatcher = "UPDATE tenancy.\"Roles\" SET \"Description\" = 'Looks on' WHERE \"Id\" = {Watcher}";
        yield return new("Roles", "SELECT", "a seat of the tenant", OliCaller, "SELECT count(*) FROM tenancy.\"Roles\" WHERE \"TenantId\" = 1", Expectation.Rows);
        yield return new("Roles", "SELECT", "seated in another tenant", OdetteCaller, "SELECT count(*) FROM tenancy.\"Roles\" WHERE \"TenantId\" = 1", Expectation.NoRows);
        yield return new("Roles", "INSERT", "roles for the whole tenant", AdaCaller, AddRole, Expectation.Rows);
        yield return new("Roles", "INSERT", "roles and settings below the root", HiroNorth, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "without the keys", OliCaller, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy of a pack", HiroSettings, Args(CopyPack, "'watcher'", "'widget.read'"), Expectation.Rows);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a copy of a pack, its keys in another order", HiroSettings, Args(CopyPack, "'operator'", "'widget.read', 'widget.create', 'widget.change'"), Expectation.Rows);
        yield return new("Roles", "INSERT", "settings for the whole tenant, keys of no pack", HiroSettings, AddRole, Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, more keys than its pack", HiroSettings, Args(CopyPack, "'watcher'", "'widget.read', 'tenancy.roles.manage'"), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, fewer keys than its pack", HiroSettings, Args(CopyPack, "'operator'", "'widget.change'"), Expectation.Refused);
        yield return new("Roles", "INSERT", "settings for the whole tenant, a pack the catalogue does not have", HiroSettings, Args(CopyPack, "'lookouts'", "'widget.read'"), Expectation.Refused);
        yield return new("Roles", "UPDATE", "roles for the whole tenant", AdaCaller, DescribeWatcher, Expectation.Rows);
        yield return new("Roles", "UPDATE", "roles below the root", HiroNorth, DescribeWatcher, Expectation.NoRows);
        yield return new("Roles", "UPDATE", "holding no such key", OliCaller, DescribeWatcher, Expectation.NoRows);
        yield return new("Roles", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"Roles\" WHERE \"Id\" = {Watcher}", Expectation.NoRows);

        // The access revision: a counter, read and taken by whoever has a seat, of any status, in the tenant the connection
        // names, since a seat that takes its own keys away, or suspends itself, takes it once it no longer holds them.
        const string Take = "UPDATE tenancy.\"TenancyAccessRevisions\" SET \"Revision\" = \"Revision\" + 1 WHERE \"TenantId\" = 1";
        const string ReadRevision = "SELECT count(*) FROM tenancy.\"TenancyAccessRevisions\" WHERE \"TenantId\" = 1";
        yield return new("TenancyAccessRevisions", "SELECT", "a seat of the tenant", OliCaller, ReadRevision, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "SELECT", "a suspended seat of the tenant", SueCaller, ReadRevision, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "SELECT", "seated in another tenant", OdetteCaller, ReadRevision, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "SELECT", "a seat here, acting in another tenant", OliActingInOrchard, ReadRevision, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "UPDATE", "grants held", HiroCaller, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "without an access key", OliCaller, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "nothing that applies now", EveCaller, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "a suspended seat of the tenant", SueCaller, Take, Expectation.Rows);
        yield return new("TenancyAccessRevisions", "UPDATE", "seated in another tenant", OdetteCaller, Take, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "UPDATE", "a seat here, acting in another tenant", OliActingInOrchard, Take, Expectation.NoRows);
        yield return new("TenancyAccessRevisions", "INSERT", "no one", AdaCaller, "INSERT INTO tenancy.\"TenancyAccessRevisions\" (\"TenantId\", \"Revision\") VALUES (9, 0)", Expectation.Refused);
        yield return new("TenancyAccessRevisions", "DELETE", "no one", AdaCaller, "DELETE FROM tenancy.\"TenancyAccessRevisions\" WHERE \"TenantId\" = 1", Expectation.NoRows);
    }
}
