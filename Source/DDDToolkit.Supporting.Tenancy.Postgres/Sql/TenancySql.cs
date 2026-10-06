using System.Text;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Tenancy.Postgres;

/// <summary>
/// The SQL of Tenancy's row access contribution, written from a context's model: the functions the policies
/// and the modules' rules ask, of the tenant the connection names or of one given as an argument, the ones a
/// module's queries read Tenancy through, the ones that answer what a seat may learn of other seats' rights,
/// and the ones system work reads across tenants through; the policies on Tenancy's own tables and on every
/// table kept to a tenant, with what they leave the operators' roles; the policies on Tenancy's access history;
/// the trigger that writes the rights, so no caller does; the triggers that keep what no policy can see, such as
/// a tenant's last administrator, at commit; and the trigger that holds callers to themselves in the columns
/// that say who changed a row.
/// <para>
/// Functions are asked by <c>{fn:tenancy/name}</c> and the caller by <c>{caller:...}</c>, which the script fills
/// in: the schema of the context that defines a function, and the host's caller functions. Every other name is
/// read from the model.
/// </para>
/// </summary>
internal static class TenancySql
{
    /// <summary>The settings, the tree, the seats, the grants and the roles: Tenancy's keys, by what they manage.</summary>
    private const string SettingsKey = TenancyKeys.SettingsManage;

    private const string UnitsKey = TenancyKeys.UnitsManage;

    private const string SeatsKey = TenancyKeys.SeatsManage;

    private const string GrantsKey = TenancyKeys.GrantsManage;

    private const string RolesKey = TenancyKeys.RolesManage;

    private const string HistoryKey = TenancyKeys.HistoryView;

    /// <summary>The functions, by the names rules ask them by, relative to <see cref="TenancyRowAccess.Owner"/>.</summary>
    internal const string CallerSeat = "caller_seat";

    internal const string CallerTenant = "caller_tenant";

    internal const string SystemTenant = "system_tenant";

    internal const string UnitsWhereIHold = "units_where_i_hold";

    internal const string ReadableUnits = "readable_units";

    internal const string RolesWithKey = "roles_with_key";

    internal const string HoldsKey = "holds_key";

    internal const string HoldsTenantWide = "holds_tenant_wide";

    internal const string IdentityTenants = "identity_tenants";

    internal const string ManagesAccess = "manages_access";

    internal const string PackKeys = "pack_keys";

    internal const string UnitParent = "unit_parent";

    internal const string KeyIsLive = "key_is_live";

    internal const string RewriteTenantRights = "rewrite_tenant_rights";

    /// <summary>
    /// The functions that take the tenant as an argument, for policies that run where the connection names none:
    /// by the names rules ask them by (<see cref="TenancyRowAccess"/>).
    /// </summary>
    internal const string SeatInTenant = "seat_in_tenant";

    internal const string SeatedInTenant = "seated_in_tenant";

    internal const string HoldsKeyInTenant = "holds_key_in_tenant";

    internal const string UnitsWhereIHoldInTenant = "units_where_i_hold_in_tenant";

    internal const string RolesWithKeyInTenant = "roles_with_key_in_tenant";

    /// <summary>The trigger functions, made in the schema of the context by the contribution's statements.</summary>
    internal const string RightsFollowGrants = "rights_follow_grants";

    internal const string AdministratorRemains = "administrator_remains";

    internal const string RightsBackedByGrants = "rights_backed_by_grants";

    internal const string PathsFollowTheTree = "paths_follow_the_tree";

    internal const string SeatIdentityIsFixed = "seat_identity_is_fixed";

    internal const string PlacementIsFixed = "placement_is_fixed";

    internal const string GrantIsFixed = "grant_is_fixed";

    internal const string SeatStatusIsManaged = "seat_status_is_managed";

    internal const string InvitationTermsAreFixed = "invitation_terms_are_fixed";

    internal const string RolePackIsFixed = "role_pack_is_fixed";

    internal const string RoleFollowsItsPack = "role_follows_its_pack";

    /// <summary>The constraint names the triggers raise with, which a translation of failures reads.</summary>
    internal const string AdministratorRemainsConstraint = "tenancy_administrator_remains";

    internal const string RightsBackedByGrantsConstraint = "tenancy_rights_backed_by_grants";

    internal const string PathsFollowTheTreeConstraint = "tenancy_paths_follow_the_tree";

    internal const string SeatIdentityIsFixedConstraint = "tenancy_seat_identity_is_fixed";

    internal const string PlacementIsFixedConstraint = "tenancy_placement_is_fixed";

    internal const string GrantIsFixedConstraint = "tenancy_grant_is_fixed";

    internal const string SeatStatusIsManagedConstraint = "tenancy_seat_status_is_managed";

    internal const string InvitationTermsAreFixedConstraint = "tenancy_invitation_terms_are_fixed";

    internal const string RolePackIsFixedConstraint = "tenancy_role_pack_is_fixed";

    internal const string RoleFollowsItsPackConstraint = "tenancy_role_follows_its_pack";

    /// <summary>The trigger that writes the rights as a grant, a seat or a role is written.</summary>
    internal const string RightsFollowGrantsTrigger = "tenancy_rights_follow_grants";

    /// <summary>
    /// The trigger function that holds a caller to itself in the columns of <c>RecordsWhoChanged</c>, made in the
    /// schema of every context that has such a table, and the trigger that runs it.
    /// </summary>
    internal const string AttributionMatchesCaller = "attribution_matches_caller";

    internal const string AttributionMatchesCallerTrigger = "tenancy_attribution_matches_caller";

    /// <summary>What the attribution trigger tells a statement it refuses, by what was wrong with it.</summary>
    internal const string AttributionOfASeat = "A seat records a row as written and changed by itself.";

    internal const string AttributionOfSystemWork = "System work records a row as written and changed by no seat.";

    internal const string AttributionIsKept = "Who wrote a row first does not change.";

    /// <summary>What the trigger on what a role's pack gave it tells a statement it refuses.</summary>
    internal const string RoleFollowsItsPackRefusal = "What its pack gave a role is written as the role is made from the pack, and as Tenancy's own system work makes it follow the pack: by no seat.";

    /// <summary>The property of a role that holds what its pack gave it, which a sync of the packs compares the pack with.</summary>
    private const string KeysFromPackProperty = "KeysFromPack";

    /// <summary>What the trigger on what a seat is tells a statement it refuses.</summary>
    internal const string SeatIdentityRefusal = "A seat keeps the id, the identity and the tenant it was made with: only Tenancy's system work in its tenant changes them.";

    /// <summary>What the trigger on a seat's status tells a statement it refuses.</summary>
    internal const string SeatStatusRefusal = "A seat's status is changed by a seat that manages seats for the whole tenant and holds, at each of its grants, the keys that manage access the grant gives.";

    /// <summary>The caller's verified identity, as the script fills it in: <c>(SELECT auth.uid())</c> on Supabase.</summary>
    private const string Uid = "{caller:uid}";

    private const string User = RowAccessRoles.User;

    private const string SystemIn = RowAccessRoles.SystemIn;

    private const string Anonymous = RowAccessRoles.Anonymous;

    /// <summary>How deep the trigger that checks the tree follows it, well past the deepest tree an organization may have.</summary>
    private const int TreeDepth = 64;

    /// <summary>
    /// What Tenancy writes for a context: for the one that maps Tenancy's tables, its functions, the policies on
    /// those tables and on the application's entities kept with them, and its triggers; for every context, the
    /// restrictive policies that keep every caller to its tenant on the tables it keeps to a tenant.
    /// <see langword="null"/> for a context with neither.
    /// </summary>
    /// <param name="context">The context whose model is written for.</param>
    /// <param name="catalogue">The catalogue the functions are written from.</param>
    /// <param name="written">What of the export the SQL is written from: the token roles, and the callers' roles.</param>
    /// <exception cref="InvalidOperationException">
    /// The model maps some of Tenancy's tables but not all, keeps to a tenant what those policies cannot follow, or
    /// maps Tenancy's access history without Tenancy's tables.
    /// </exception>
    public static RowAccessContributionResult? For(DbContext context, TenancyCatalogue catalogue, Written written)
    {
        var model = context.Model;
        var functions = new List<ContributedFunction>();
        var policies = new List<ContributedPolicy>();
        var statements = new List<string>();
        var exclusive = new List<IEntityType>();

        // The tables no caller's role reads, whatever else it reads: the digests of the invitations' tokens.
        var unread = new List<IEntityType>();

        if (TenancyTables.Of(model) is { } tenancy)
        {
            functions.AddRange(Functions(tenancy, catalogue));
            OwnTables(tenancy, policies, exclusive);
            Invitations(tenancy, policies, exclusive, unread);
            HostEntities(tenancy, policies, exclusive);
            History(tenancy, written.RemovesOldHistory, policies, exclusive);
            statements.AddRange(Triggers(tenancy, written));
        }
        else if (TenancyModel.EventLogOf(model) is not null)
        {
            // The history's policies ask Tenancy's seats who is writing, so they are written where the seats are.
            // Anywhere else the table would get no policy at all, and say so nowhere.
            throw new InvalidOperationException(
                $"{context.GetType().Name} maps Tenancy's access history, with AddTenancyEventLogTable, and none of Tenancy's tables. " +
                "The history's policies are written with those of Tenancy's own tables, so here it would get none, and every tenant's rows would be read alike. " +
                "Map it in the context that calls AddTenancy; a module keeps its own events with AddEventLog.");
        }

        ScopedTables(model, policies);

        // Wherever anonymous callers are kept out, so is every token role with a database role of its own. A rule of
        // a module may be for such a role, and nothing in it names a tenant: without this, its policy would reach
        // every tenant's rows. An operator's role is held otherwise: it reads, and writes nothing.
        foreach (var closed in policies.Where(policy => policy.Restrictive && policy.Role == Anonymous).ToList())
        {
            policies.AddRange(written.ClosedTokenRoles.Select(tokenRole => new ContributedPolicy(closed.Table, "Closed to token roles", "ALL", tokenRole, "false", "false", Restrictive: true)));

            foreach (var operatorRole in written.OperatorRoles)
            {
                Operators(closed.Table, operatorRole, readsEveryRow: exclusive.Contains(closed.Table) && !unread.Contains(closed.Table), policies);
            }
        }

        statements.AddRange(AttributionTriggers(model, written));

        return functions.Count + policies.Count + statements.Count + exclusive.Count == 0
            ? null
            : new RowAccessContributionResult(functions, policies, statements, exclusive);
    }

    /// <summary>What of an export Tenancy's SQL is written from, beyond the model and the catalogue.</summary>
    /// <param name="ClosedTokenRoles">
    /// The token roles the host mapped to a database role of their own that are no operators', each by the name a
    /// rule gives it, <c>RowAccessRoles.Token("analyst")</c>: every table kept to a tenant is closed to them, as it
    /// is to anonymous callers, since the holder of such a token has no seat.
    /// </param>
    /// <param name="OperatorRoles">
    /// The token roles of the application's operators, named the same way: each reads every row of Tenancy's own
    /// tables and writes none, and on a module's table reads what a rule of the module admits and writes nothing.
    /// </param>
    /// <param name="UserRole">The database role a signed-in user's queries run as.</param>
    /// <param name="SystemInRole">The database role scoped system work runs as.</param>
    /// <param name="RemovesOldHistory">
    /// Whether the role the host's own bookkeeping runs as reads and removes rows of Tenancy's access history: the
    /// export writes privileges from the policies, and the host has such a role.
    /// </param>
    internal sealed record Written(
        IReadOnlyList<string> ClosedTokenRoles,
        IReadOnlyList<string> OperatorRoles,
        string UserRole,
        string SystemInRole,
        bool RemovesOldHistory)
    {
        /// <summary>The same for the same SQL, and another for any other: what an answer is kept by.</summary>
        public string Key { get; } = string.Join(
            "|",
            new[] { string.Join(",", ClosedTokenRoles.Select(Uri.EscapeDataString)), string.Join(",", OperatorRoles.Select(Uri.EscapeDataString)) }
                .Append(Uri.EscapeDataString(UserRole))
                .Append(Uri.EscapeDataString(SystemInRole))
                .Append(RemovesOldHistory ? "1" : "0"));
    }

    // ------------------------------------------------------------------------------------------------ the questions

    /// <summary>A function of Tenancy's, as contributed SQL asks it.</summary>
    private static string Fn(string name) => "{fn:" + TenancyRowAccess.Owner + "/" + name + "}";

    /// <summary><paramref name="column"/> is the calling seat's tenant.</summary>
    private static string T(string column) => $"{column} = (SELECT {Fn(CallerTenant)}())";

    /// <summary><paramref name="column"/> is the tenant system work acts in.</summary>
    private static string S(string column) => $"{column} = (SELECT {Fn(SystemTenant)}())";

    /// <summary>The calling seat holds <paramref name="key"/> now, at some unit.</summary>
    private static string K(string key) => $"(SELECT {Fn(HoldsKey)}({RowAccessModel.Literal(key)}))";

    /// <summary>The calling seat holds <paramref name="key"/> now at the root.</summary>
    private static string W(string key) => $"(SELECT {Fn(HoldsTenantWide)}({RowAccessModel.Literal(key)}))";

    /// <summary><paramref name="column"/> is a unit where the calling seat holds <paramref name="key"/> now: asked once per statement.</summary>
    private static string U(string key, string column) => $"{column} = ANY (ARRAY(SELECT {Fn(UnitsWhereIHold)}({RowAccessModel.Literal(key)})))";

    /// <summary>The system work is Tenancy's own: it began <c>TenancyWork.BeginSystemIn</c> in Tenancy's scope.</summary>
    private static string Scope => "{caller:claim:scope} = " + RowAccessModel.Literal(TenancyWork.SystemScope);

    /// <summary>
    /// The calling seat may read the grant of the seat in <paramref name="seat"/> at the unit in
    /// <paramref name="unit"/>: it is the calling seat's own, or the calling seat manages grants, seats or units at
    /// that unit, held there or at a unit above it, or manages roles for the whole tenant. A key reads only where it
    /// applies, as it acts only there: held at a unit, it reaches the grants at that unit and below it, and none above
    /// it or beside it. The roles key is the exception, because a change of a role reaches every seat that holds it,
    /// wherever; held below the root it changes no role, and reads nothing.
    /// <para>
    /// Written once, for the policy that lets grants be read and for the functions that answer about other seats'
    /// rights, a right being read where the grant that gives it is: the two cannot come to disagree.
    /// </para>
    /// </summary>
    /// <param name="seat">The grant's or the right's seat, as SQL.</param>
    /// <param name="unit">The grant's or the right's unit, as SQL.</param>
    private static string ReadsGrantOf(string seat, string unit)
        => $"({seat} = (SELECT {Fn(CallerSeat)}()) OR {U(GrantsKey, unit)} OR {U(SeatsKey, unit)} OR {U(UnitsKey, unit)} OR {W(RolesKey)})";

    /// <summary>A period of <paramref name="alias"/>'s row that applies now.</summary>
    private static string Live(IEntityType table, string alias)
        => $"{alias}.{C(table, "StartsAt")} <= pg_catalog.now() AND ({alias}.{C(table, "EndsAt")} IS NULL OR {alias}.{C(table, "EndsAt")} > pg_catalog.now())";

    private static string C(IEntityType table, string property) => TenancyTables.Column(table, property);

    private static string Q(IEntityType table) => TenancyTables.Table(table);

    /// <summary>
    /// Tenancy's functions: the questions about the caller, the sets rules ask, what the catalogue says of a key or
    /// a pack, the rows a module reads Tenancy through, the three that answer about other seats' rights, the
    /// one that writes a tenant's rights again, the three that read across tenants for system work, with the one
    /// that finds an invitation by its token's digest where the context maps invitations, and the five that take
    /// the tenant as an argument.
    /// </summary>
    private static IEnumerable<ContributedFunction> Functions(TenancyTables tenancy, TenancyCatalogue catalogue)
    {
        var tenants = tenancy.Tenants;
        var seats = tenancy.Seats;
        var units = tenancy.Units;
        var paths = tenancy.UnitPaths;
        var rights = tenancy.Rights;
        var roles = tenancy.Roles;
        var placements = tenancy.Placements;

        var tenantType = TenancyTables.ColumnType(tenants, "Id");
        var seatType = TenancyTables.ColumnType(seats, "Id");
        var unitType = TenancyTables.ColumnType(units, "Id");
        var roleType = TenancyTables.ColumnType(roles, "Id");
        string[] callers = [User, SystemIn];

        var activeTenant = TenancyTables.Stored(tenants, "Status", TenantStatus.Active);
        var activeSeat = TenancyTables.Stored(seats, "Status", SeatStatus.Active);
        var activeRole = TenancyTables.Stored(roles, "Status", RoleStatus.Active);
        var callerSeat = $"(SELECT {Fn(CallerSeat)}())";

        // The questions about a seat, written once each: asked of the calling seat, which the tenant the
        // connection names finds, and of the seat the same person has in a tenant given as an argument. The two
        // ways of asking cannot come to answer differently.

        // The active seat of the verified identity in a tenant, when the tenant is active too.
        string SeatOfTheIdentityIn(string tenant) =>
            $"""
            SELECT s.{C(seats, "Id")} FROM {Q(seats)} s
            JOIN {Q(tenants)} t ON t.{C(tenants, "Id")} = s.{C(seats, "TenantId")}
            WHERE s.{C(seats, "Identity")} = {Uid}
              AND s.{C(seats, "TenantId")} = {tenant}
              AND s.{C(seats, "Status")} = {activeSeat} AND t.{C(tenants, "Status")} = {activeTenant}
            """;

        // A key the catalogue has retired answers nothing in any of them, as it answers nothing in C#: the rights
        // written for it before stay until their holders' rights are written again, and a role keeps the key it
        // was given. These run as their owner, so they may ask which keys are live.

        // The units where a seat holds a key now: the units it is held at, and every unit below them.
        string UnitsHeldBy(string seat, string key) =>
            $"""
            SELECT DISTINCT p.{C(paths, "DescendantId")} FROM {Q(rights)} r
            JOIN {Q(paths)} p ON p.{C(paths, "AncestorId")} = r.{C(rights, "UnitId")} AND p.{C(paths, "TenantId")} = r.{C(rights, "TenantId")}
            WHERE r.{C(rights, "SeatId")} = {seat} AND r.{C(rights, "Key")} = {key} AND {Fn(KeyIsLive)}({key})
              AND {Live(rights, "r")}
            """;

        // Whether a seat holds a key now, at any unit.
        string HeldBy(string seat, string key) =>
            $"""
            SELECT EXISTS (SELECT 1 FROM {Q(rights)} r
                           WHERE r.{C(rights, "SeatId")} = {seat} AND r.{C(rights, "Key")} = {key} AND {Fn(KeyIsLive)}({key}) AND {Live(rights, "r")})
            """;

        // The active roles of a tenant that hold a key.
        string RolesHolding(string tenant, string key) =>
            $"""
            SELECT r.{C(roles, "Id")} FROM {Q(roles)} r
            WHERE r.{C(roles, "TenantId")} = {tenant} AND r.{C(roles, "Status")} = {activeRole}
              AND {KeyAmong(roles, "r", key)} AND {Fn(KeyIsLive)}({key})
            """;

        yield return new ContributedFunction(
            SystemTenant,
            "",
            tenantType,
            $"SELECT nullif(pg_catalog.current_setting({RowAccessModel.Literal(TenancyRowLevelSecurity.TenantSetting)}, true), '')::{TenancyTables.Cast(tenantType)}",
            SecurityDefiner: false,
            GrantTo: callers);

        yield return new ContributedFunction(
            CallerSeat,
            "",
            seatType,
            SeatOfTheIdentityIn($"(SELECT {Fn(SystemTenant)}())"),
            SecurityDefiner: true,
            GrantTo: callers);

        yield return new ContributedFunction(
            CallerTenant,
            "",
            tenantType,
            $"SELECT s.{C(seats, "TenantId")} FROM {Q(seats)} s WHERE s.{C(seats, "Id")} = {callerSeat}",
            SecurityDefiner: true,
            GrantTo: callers);

        yield return new ContributedFunction(
            UnitsWhereIHold,
            "key text",
            "SETOF " + unitType,
            UnitsHeldBy(callerSeat, "$1"),
            SecurityDefiner: true,
            GrantTo: callers);

        // Kept to the seat's tenant, as the C# question is, whatever tenant a placement row says it is of.
        yield return new ContributedFunction(
            ReadableUnits,
            "",
            "SETOF " + unitType,
            $"""
            SELECT DISTINCT p.{C(paths, "DescendantId")} FROM {Q(placements)} pl
            JOIN {Q(paths)} p ON p.{C(paths, "AncestorId")} = pl.{C(placements, "UnitId")} AND p.{C(paths, "TenantId")} = pl.{C(placements, "TenantId")}
            WHERE pl.{C(placements, "SeatId")} = {callerSeat} AND p.{C(paths, "TenantId")} = (SELECT {Fn(CallerTenant)}())
            """,
            SecurityDefiner: true,
            GrantTo: callers);

        yield return new ContributedFunction(
            RolesWithKey,
            "key text",
            "SETOF " + roleType,
            RolesHolding($"(SELECT {Fn(CallerTenant)}())", "$1"),
            SecurityDefiner: true,
            GrantTo: callers);

        yield return new ContributedFunction(
            HoldsKey,
            "key text",
            "boolean",
            HeldBy(callerSeat, "$1"),
            SecurityDefiner: true,
            GrantTo: callers);

        yield return new ContributedFunction(
            HoldsTenantWide,
            "key text",
            "boolean",
            $"""
            SELECT EXISTS (SELECT 1 FROM {Q(rights)} r
                           JOIN {Q(units)} u ON u.{C(units, "Id")} = r.{C(rights, "UnitId")} AND u.{C(units, "TenantId")} = r.{C(rights, "TenantId")}
                           WHERE r.{C(rights, "SeatId")} = {callerSeat} AND r.{C(rights, "Key")} = $1 AND {Fn(KeyIsLive)}($1) AND {Live(rights, "r")}
                             AND u.{C(units, "ParentId")} IS NULL)
            """,
            SecurityDefiner: true,
            GrantTo: callers);

        // The parent a unit has before the statement that asks, which a policy on the units cannot read from the units
        // itself: Postgres takes a policy that reads its own table for one that never ends.
        yield return new ContributedFunction(
            UnitParent,
            "unit " + unitType,
            unitType,
            $"SELECT u.{C(units, "ParentId")} FROM {Q(units)} u WHERE u.{C(units, "Id")} = $1 AND u.{C(units, "TenantId")} = (SELECT {Fn(CallerTenant)}())",
            SecurityDefiner: true,
            GrantTo: callers);

        // Every tenant the identity has a seat in, whatever the status of the seat or of the tenant. It is for the
        // directory, where a person finds their tenants to choose one, and for the access revision, which a seat
        // takes as it suspends itself; it decides no access. What does goes by the caller's seat, or by one of the
        // questions that take the tenant as an argument, and those answer only while seat and tenant are active.
        yield return new ContributedFunction(
            IdentityTenants,
            "",
            "SETOF " + tenantType,
            $"SELECT DISTINCT s.{C(seats, "TenantId")} FROM {Q(seats)} s WHERE s.{C(seats, "Identity")} = {Uid}",
            SecurityDefiner: true,
            GrantTo: callers);

        // From the catalogue the application runs with: when it marks another key, the next export writes this
        // function again, and the access file that does is the migration.
        yield return new ContributedFunction(
            ManagesAccess,
            "key text",
            "boolean",
            ManagesAccessBody(catalogue),
            SecurityDefiner: false,
            GrantTo: callers,
            Volatility: "IMMUTABLE");

        // The keys a copy of each pack holds, from the same catalogue: what a settings manager's copy of a pack may
        // hold, and nothing more.
        yield return new ContributedFunction(
            PackKeys,
            "pack text",
            "text[]",
            PackKeysBody(catalogue),
            SecurityDefiner: false,
            GrantTo: callers,
            Volatility: "IMMUTABLE");

        // Which keys are live, from the same catalogue: a key that is not gets no rights, and answers nothing. No
        // caller's to ask: the functions that write the rights and the ones that answer about a key, which run as
        // their owner, ask it.
        yield return new ContributedFunction(
            KeyIsLive,
            "key text",
            "boolean",
            KeyIsLiveBody(catalogue),
            SecurityDefiner: false,
            GrantTo: [],
            Volatility: "IMMUTABLE");

        foreach (var function in ReadFunctions(tenancy))
        {
            yield return function;
        }

        // What a seat may learn of other seats' rights, which the policies keep from its own reads: a right where
        // the seat may read the grant that gives it, as ids, keys and dates and nothing else of anyone.
        var seatTenant = $"(SELECT {Fn(CallerTenant)}())";
        string[] seatCallers = [User];
        var readable = ReadsGrantOf($"r.{C(rights, "SeatId")}", $"r.{C(rights, "UnitId")}");

        // Held at the root, these are read with a key held there: by every command that could take an
        // administrator away, which asks one there, and by no seat that manages only part of the tree.
        yield return new ContributedFunction(
            TenancyFunctionNames.TenantAdministrators,
            "",
            $"TABLE (\"SeatId\" {seatType}, \"RoleId\" {roleType})",
            $"""
            SELECT r.{C(rights, "SeatId")}, r.{C(rights, "RoleId")} FROM {Q(rights)} r
            JOIN {Q(units)} u ON u.{C(units, "Id")} = r.{C(rights, "UnitId")} AND u.{C(units, "TenantId")} = r.{C(rights, "TenantId")}
            WHERE r.{C(rights, "TenantId")} = {seatTenant} AND r.{C(rights, "Key")} = {RowAccessModel.Literal(TenancyKeys.AdministratorKey)}
              AND r.{C(rights, "EndsAt")} IS NULL AND r.{C(rights, "StartsAt")} <= pg_catalog.now()
              AND u.{C(units, "ParentId")} IS NULL
              AND {readable}
            """,
            SecurityDefiner: true,
            GrantTo: seatCallers);

        // Every right that has not ended, the calling seat's or of a key that manages access, held at a parent or
        // above it, once for each of the two parents it reaches: answered to a seat that manages units at both.
        // Another seat's right is answered only where it reaches one parent and not the other, which is a right the
        // move changes: one above both reaches the unit wherever it hangs, so the check never weighs it, and the
        // caller, who may manage only part of the tree, learns nothing of the rights above that part that stay.
        // The caller's own rights are answered at both, so a seat that manages units at both always has a row.
        var managesUnits = $"ANY (ARRAY(SELECT {Fn(UnitsWhereIHold)}({RowAccessModel.Literal(UnitsKey)})))";
        var otherParent = $"CASE WHEN p.{C(paths, "DescendantId")} = $1 THEN $2 ELSE $1 END";
        var changesWithTheMove = $"NOT EXISTS (SELECT 1 FROM {Q(paths)} o WHERE o.{C(paths, "AncestorId")} = r.{C(rights, "UnitId")} AND o.{C(paths, "TenantId")} = r.{C(rights, "TenantId")} AND o.{C(paths, "DescendantId")} = {otherParent})";
        yield return new ContributedFunction(
            TenancyFunctionNames.RightsAMoveChanges,
            $"parent {unitType}, new_parent {unitType}",
            $"TABLE (\"UnitId\" {unitType}, \"Key\" text, \"EndsAt\" {TenancyTables.ColumnType(rights, "EndsAt")}, \"Parent\" {unitType}, \"OfCaller\" boolean)",
            $"""
            SELECT r.{C(rights, "UnitId")}, r.{C(rights, "Key")}::pg_catalog.text, r.{C(rights, "EndsAt")}, p.{C(paths, "DescendantId")},
                   coalesce(r.{C(rights, "SeatId")} = {callerSeat}, false)
            FROM {Q(rights)} r
            JOIN {Q(paths)} p ON p.{C(paths, "AncestorId")} = r.{C(rights, "UnitId")} AND p.{C(paths, "TenantId")} = r.{C(rights, "TenantId")}
            WHERE r.{C(rights, "TenantId")} = {seatTenant}
              AND (r.{C(rights, "EndsAt")} IS NULL OR r.{C(rights, "EndsAt")} > pg_catalog.now())
              AND (r.{C(rights, "SeatId")} = {callerSeat} OR ({Fn(ManagesAccess)}(r.{C(rights, "Key")}) AND {changesWithTheMove}))
              AND p.{C(paths, "DescendantId")} IN ($1, $2)
              AND $1 = {managesUnits} AND $2 = {managesUnits}
            """,
            SecurityDefiner: true,
            GrantTo: seatCallers);

        // The active seats with a right for the key that applies now, at the unit or above it: each one whose grant
        // there the calling seat may read, which is itself, any other seat at a unit where it manages grants, seats
        // or units, and every one when it manages roles for the whole tenant.
        yield return new ContributedFunction(
            TenancyFunctionNames.SeatsHoldingAt,
            $"key text, unit {unitType}",
            $"TABLE (\"SeatId\" {seatType})",
            $"""
            SELECT DISTINCT r.{C(rights, "SeatId")} FROM {Q(rights)} r
            JOIN {Q(paths)} p ON p.{C(paths, "AncestorId")} = r.{C(rights, "UnitId")} AND p.{C(paths, "TenantId")} = r.{C(rights, "TenantId")}
            JOIN {Q(seats)} s ON s.{C(seats, "Id")} = r.{C(rights, "SeatId")} AND s.{C(seats, "TenantId")} = r.{C(rights, "TenantId")} AND s.{C(seats, "Status")} = {activeSeat}
            WHERE r.{C(rights, "TenantId")} = {seatTenant} AND r.{C(rights, "Key")} = $1 AND {Fn(KeyIsLive)}($1)
              AND {Live(rights, "r")}
              AND p.{C(paths, "DescendantId")} = $2
              AND {readable}
            """,
            SecurityDefiner: true,
            GrantTo: seatCallers);

        // Every right of the tenant written again from its grants, for Tenancy's own system work in that tenant:
        // what repairs the rights after rows were written past the triggers, an import say. It answers how many
        // rows it added, changed or removed; in any other scope, or outside a tenant, none, and it touches nothing.
        var systemTenant = $"(SELECT {Fn(SystemTenant)}())";
        yield return new ContributedFunction(
            RewriteTenantRights,
            "",
            "integer",
            $"""
            WITH removed AS (
                {Indented(RemoveStaleRights(tenancy, $"r.{C(rights, "TenantId")} = {systemTenant} AND {Scope}", $"s.{C(seats, "TenantId")} = {systemTenant} AND {Scope}"), 4)}
                RETURNING 1),
            written AS (
                {Indented(WriteDesiredRights(tenancy, $"s.{C(seats, "TenantId")} = {systemTenant} AND {Scope}"), 4)}
                RETURNING 1)
            SELECT ((SELECT pg_catalog.count(*) FROM removed) + (SELECT pg_catalog.count(*) FROM written))::pg_catalog.int4
            """,
            SecurityDefiner: true,
            GrantTo: [SystemIn],
            Volatility: "VOLATILE");

        // The reads across tenants: what is needed before any tenant is known. Each answers one question, as
        // ids or keys, to system work alone, which asks in no tenant and so reads no row of any table itself:
        // nothing has to run past the policies for them. The keys in use and a person's seats are answered to
        // Tenancy's own work and to no other scope's; the tenants to visit to the rounds of every module, each
        // of which then works in a tenant under its own scope.
        string[] systemWork = [SystemIn];

        yield return new ContributedFunction(
            TenancyFunctionNames.RoleKeysInUse,
            "",
            "SETOF text",
            $"""
            SELECT DISTINCT held.k::pg_catalog.text FROM {Q(roles)} r
            CROSS JOIN LATERAL {KeysOf(roles, "r", "held")}
            WHERE {Scope}
            """,
            SecurityDefiner: true,
            GrantTo: systemWork);

        yield return new ContributedFunction(
            TenancyFunctionNames.TenantsToSweep,
            "",
            "SETOF " + tenantType,
            $"""
            SELECT t.{C(tenants, "Id")} FROM {Q(tenants)} t
            WHERE t.{C(tenants, "Status")} IN ({activeTenant}, {TenancyTables.Stored(tenants, "Status", TenantStatus.Suspended)})
            """,
            SecurityDefiner: true,
            GrantTo: systemWork);

        yield return new ContributedFunction(
            TenancyFunctionNames.SeatsOfIdentity,
            "identity " + TenancyTables.ColumnType(seats, "Identity"),
            $"TABLE (\"TenantId\" {tenantType}, \"SeatId\" {seatType})",
            $"""
            SELECT s.{C(seats, "TenantId")}, s.{C(seats, "Id")} FROM {Q(seats)} s
            WHERE s.{C(seats, "Identity")} = $1 AND {Scope}
            """,
            SecurityDefiner: true,
            GrantTo: systemWork);

        // Which invitation a token's digest is for, in whatever tenant: whoever accepts an invitation names no
        // tenant, the token does. It is the one way a digest is read at all, since no policy lets any caller read
        // the digests' table: it answers ids for a digest somebody already has, to Tenancy's own work alone.
        if (tenancy is { Invitations: { } invitations, InvitationDigests: { } digests })
        {
            yield return new ContributedFunction(
                TenancyFunctionNames.InvitationOfDigest,
                "digest " + TenancyTables.ColumnType(digests, "Digest"),
                $"TABLE (\"TenantId\" {tenantType}, \"InvitationId\" {TenancyTables.ColumnType(invitations, "Id")}, \"IssuedBy\" {seatType})",
                $"""
                SELECT i.{C(invitations, "TenantId")}, i.{C(invitations, "Id")}, i.{C(invitations, "IssuedBy")} FROM {Q(digests)} d
                JOIN {Q(invitations)} i ON i.{C(invitations, "Id")} = d.{C(digests, "InvitationId")} AND i.{C(invitations, "TenantId")} = d.{C(digests, "TenantId")}
                WHERE d.{C(digests, "Digest")} = $1 AND {Scope}
                """,
                SecurityDefiner: true,
                GrantTo: systemWork);
        }

        // The questions about the caller with the tenant as an argument, for a policy that runs where the
        // connection names no tenant: on the path of a stored file, or on a channel. They read no setting. Each
        // speaks of the seat the verified identity has in that tenant, and only while that seat and the tenant
        // are active, so naming a tenant says nothing of anyone else: to someone without such a seat every one
        // of them answers nothing, the roles of that tenant included.
        var seatThere = $"(SELECT {Fn(SeatInTenant)}($1))";
        var inTenant = "tenant " + tenantType;

        yield return new ContributedFunction(
            SeatInTenant,
            inTenant,
            seatType,
            SeatOfTheIdentityIn("$1"),
            SecurityDefiner: true,
            GrantTo: seatCallers);

        yield return new ContributedFunction(
            SeatedInTenant,
            inTenant,
            "boolean",
            $"SELECT {Fn(SeatInTenant)}($1) IS NOT NULL",
            SecurityDefiner: true,
            GrantTo: seatCallers);

        yield return new ContributedFunction(
            HoldsKeyInTenant,
            inTenant + ", key text",
            "boolean",
            HeldBy(seatThere, "$2"),
            SecurityDefiner: true,
            GrantTo: seatCallers);

        yield return new ContributedFunction(
            UnitsWhereIHoldInTenant,
            inTenant + ", key text",
            "SETOF " + unitType,
            UnitsHeldBy(seatThere, "$2"),
            SecurityDefiner: true,
            GrantTo: seatCallers);

        yield return new ContributedFunction(
            RolesWithKeyInTenant,
            inTenant + ", key text",
            "SETOF " + roleType,
            RolesHolding("$1", "$2") + $"\n  AND {seatThere} IS NOT NULL",
            SecurityDefiner: true,
            GrantTo: seatCallers);
    }

    /// <summary>
    /// The functions a module reads Tenancy through (<see cref="TenancyFunctionNames"/>), one for each row of the
    /// read model: what <c>AddTenancyReadFunctions</c> maps the rows to, so a module's model names these and no
    /// table of Tenancy's. Each answers its table's rows as they are, under the names of the row's properties and
    /// with types of its own, whatever the application calls Tenancy's tables and columns and however it stores
    /// a status or a role's keys.
    /// <para>
    /// They run as their caller and are written to be folded into the query that asks them
    /// (<see cref="ContributedFunction.Inlinable"/>), so Postgres plans a module's question as over the tables,
    /// with their indexes, and the policies on those tables decide the rows: a seat its own rights and the rest of
    /// its tenant, system work in a tenant all of that tenant, and the tables' owner, whom no policy holds, every
    /// row. None has a condition of its own. A person's seats in other tenants, which the seats' policy lets
    /// that person read, are kept out by the tenant filter the rows carry in the module's model.
    /// </para>
    /// <para>
    /// They answer access facts only: ids, keys, periods, statuses, a unit's parent, and a role's pack and keys.
    /// None answers what a seat, a unit or a role is called, so a module that reads Tenancy through them has no
    /// name to show, and asks the directory for one, by id.
    /// </para>
    /// </summary>
    private static IEnumerable<ContributedFunction> ReadFunctions(TenancyTables tenancy)
        => ReadColumns(tenancy).Select(read => Read(read.Function, read.Table, read.Columns));

    /// <summary>
    /// The column names each read function answers, in order, by the function's name: what a start-up check
    /// compares with the functions the database has. Written from the list the functions themselves are written
    /// from, so the two cannot drift apart.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadFunctionColumns(TenancyTables tenancy)
        => ReadColumns(tenancy).ToDictionary(
            read => read.Function,
            read => (IReadOnlyList<string>)[.. read.Columns.Select(column => column.Name)],
            StringComparer.Ordinal);

    /// <summary>
    /// The columns of each read function, listed here once, one to a line, in the order of the read row's
    /// properties: a function's signature and its body are both written from that list, and so is what the
    /// start-up check expects of the database.
    /// </summary>
    private static IEnumerable<(string Function, IEntityType Table, Answered[] Columns)> ReadColumns(TenancyTables tenancy)
    {
        var units = tenancy.Units;
        var paths = tenancy.UnitPaths;
        var rights = tenancy.Rights;
        var roles = tenancy.Roles;
        var placements = tenancy.Placements;
        var seats = tenancy.Seats;

        yield return (
            TenancyFunctionNames.CallerRights,
            rights,
            [
                Answered.AsStored(rights, "TenantId"),
                Answered.AsStored(rights, "SeatId"),
                Answered.AsStored(rights, "UnitId"),
                Answered.AsStored(rights, "RoleId"),
                Answered.Text(rights, "Key"),
                Answered.Instant(rights, "StartsAt"),
                Answered.Instant(rights, "EndsAt"),
            ]);

        yield return (
            TenancyFunctionNames.TenantUnitPaths,
            paths,
            [
                Answered.AsStored(paths, "TenantId"),
                Answered.AsStored(paths, "AncestorId"),
                Answered.AsStored(paths, "DescendantId"),
                Answered.Whole(paths, "Distance"),
            ]);

        // Not the name, and no column the application added: no access rule reads them.
        yield return (
            TenancyFunctionNames.TenantUnits,
            units,
            [
                Answered.AsStored(units, "Id"),
                Answered.AsStored(units, "TenantId"),
                Answered.AsStored(units, "ParentId"),
                Answered.NameOf<UnitStatus>(units, "Status"),
            ]);

        // Not the name. The pack and the keys are what rules read of a role.
        yield return (
            TenancyFunctionNames.TenantRoles,
            roles,
            [
                Answered.AsStored(roles, "Id"),
                Answered.AsStored(roles, "TenantId"),
                Answered.Text(roles, "FromPack"),
                Answered.NameOf<RoleStatus>(roles, "Status"),
                new Answered("Keys", "text[]", KeysAsArray(roles)),
            ]);

        yield return (
            TenancyFunctionNames.TenantPlacements,
            placements,
            [
                Answered.AsStored(placements, "SeatId"),
                Answered.AsStored(placements, "UnitId"),
                Answered.Flag(placements, "IsPrimary"),
                Answered.AsStored(placements, "TenantId"),
            ]);

        // Never the identity, nor a field the application adds to its seat class.
        yield return (
            TenancyFunctionNames.TenantSeats,
            seats,
            [
                Answered.AsStored(seats, "Id"),
                Answered.AsStored(seats, "TenantId"),
                Answered.NameOf<SeatStatus>(seats, "Status"),
            ]);
    }

    /// <summary>
    /// A function that answers the rows of <paramref name="table"/>, read as <c>t</c>, as <paramref name="columns"/>:
    /// executable by signed-in users and by system work in a tenant, as their caller, and written to be folded
    /// into the query that asks it.
    /// </summary>
    private static ContributedFunction Read(string name, IEntityType table, Answered[] columns)
        => new(
            name,
            "",
            "TABLE (" + string.Join(", ", columns.Select(column => $"\"{column.Name}\" {column.Type}")) + ")",
            "SELECT " + string.Join(", ", columns.Select(column => column.Sql)) + "\n" + $"FROM {Q(table)} t",
            SecurityDefiner: false,
            GrantTo: [User, SystemIn],
            Inlinable: true);

    /// <summary>
    /// A role's keys as an array of text, whichever way the roles' table stores them: the array as it is, an array
    /// of another type as text, and the elements of a JSON document gathered into one.
    /// </summary>
    private static string KeysAsArray(IEntityType roles)
        => KeysColumn(roles) switch
        {
            (var column, KeysStore.Array) when Answered.IsOfType(roles, "Keys", "text[]") => $"t.{column}",
            (var column, KeysStore.Array) => $"t.{column}::pg_catalog.text[]",
            (var column, var store) => $"ARRAY(SELECT held.k FROM {Elements(store, "t." + column)} AS held(k))",
        };

    /// <summary>
    /// One column a read function answers: the name it answers it under, which is the property's of the read row,
    /// its type, and what it selects of the table's row, <c>t</c>.
    /// </summary>
    /// <param name="Name">The column's name in the function's answer.</param>
    /// <param name="Type">The column's type in the function's answer.</param>
    /// <param name="Sql">What is selected for it.</param>
    private sealed record Answered(string Name, string Type, string Sql)
    {
        /// <summary>The column as the table stores it, with the store type the model gives it: an id.</summary>
        public static Answered AsStored(IEntityType table, string property)
            => new(property, TenancyTables.ColumnType(table, property), $"t.{C(table, property)}");

        /// <summary>The column as text, whatever its length in the table.</summary>
        public static Answered Text(IEntityType table, string property) => Of(table, property, "text", "pg_catalog.text");

        /// <summary>The column as an instant.</summary>
        public static Answered Instant(IEntityType table, string property) => Of(table, property, "timestamp with time zone", "pg_catalog.timestamptz");

        /// <summary>The column as a whole number.</summary>
        public static Answered Whole(IEntityType table, string property) => Of(table, property, "integer", "pg_catalog.int4");

        /// <summary>The column as true or false.</summary>
        public static Answered Flag(IEntityType table, string property) => Of(table, property, "boolean", "pg_catalog.bool");

        /// <summary>
        /// The column of an enum as the name of its member, whatever the table stores for it, a number or a text
        /// in another spelling: a branch for each member, from the value the model stores it as.
        /// </summary>
        public static Answered NameOf<TEnum>(IEntityType table, string property)
            where TEnum : struct, Enum
            => new(
                property,
                "text",
                $"CASE t.{C(table, property)} "
                + string.Join(" ", Enum.GetValues<TEnum>().Select(value => $"WHEN {TenancyTables.Stored(table, property, value)} THEN {RowAccessModel.Literal(value.ToString())}"))
                + " END");

        /// <summary>Whether the model stores the column of <paramref name="property"/> as <paramref name="type"/>.</summary>
        public static bool IsOfType(IEntityType table, string property, string type)
            => string.Equals(TenancyTables.ColumnType(table, property).Trim(), type, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The column answered as <paramref name="type"/>: as it is where the model stores it so, and cast where it
        /// does not, to the type named with its schema, since a function folded into a query runs with the caller's
        /// search path.
        /// </summary>
        private static Answered Of(IEntityType table, string property, string type, string cast)
            => new(property, type, IsOfType(table, property, type) ? $"t.{C(table, property)}" : $"t.{C(table, property)}::{cast}");
    }

    /// <summary>
    /// The body of <c>key_is_live(key)</c>: whether the key is one of the keys <paramref name="catalogue"/> has
    /// live. A start-up check compares it with the function the database has.
    /// </summary>
    internal static string KeyIsLiveBody(TenancyCatalogue catalogue)
        => catalogue.LiveKeys.Count == 0
            ? "SELECT false"
            : "SELECT $1 IN (" + string.Join(", ", catalogue.LiveKeys.Select(RowAccessModel.Literal)) + ")";

    /// <summary>
    /// The rights the grants in <paramref name="scope"/> give, as the projection works them out
    /// (<see cref="TenancyProjection.RightsOf{TTenantId, TSeatId, TUnitId, TRoleId}"/>): for each grant <c>g</c> of
    /// an active seat <c>s</c> and an active role <c>ro</c> of the seat's tenant, one row per live key the role
    /// holds, with the grant's period and the seat's tenant. Its columns are <c>tenant</c>, <c>seat</c>,
    /// <c>unit</c>, <c>role</c>, <c>key</c>, <c>starts</c> and <c>ends</c>. The one place that says what the
    /// rights are, for the trigger that keeps them and for the function that writes a tenant's again.
    /// </summary>
    private static string DesiredRights(TenancyTables tenancy, string scope)
    {
        var (grants, seats, roles) = (tenancy.Grants, tenancy.Seats, tenancy.Roles);
        return
            $"SELECT s.{C(seats, "TenantId")} AS tenant, g.{C(grants, "SeatId")} AS seat, g.{C(grants, "UnitId")} AS unit, g.{C(grants, "RoleId")} AS role, held.k AS key, " +
            $"g.{C(grants, "StartsAt")} AS starts, g.{C(grants, "EndsAt")} AS ends\n" +
            $"FROM {Q(grants)} g\n" +
            $"JOIN {Q(seats)} s ON s.{C(seats, "Id")} = g.{C(grants, "SeatId")} AND s.{C(seats, "Status")} = {TenancyTables.Stored(seats, "Status", SeatStatus.Active)}\n" +
            $"JOIN {Q(roles)} ro ON ro.{C(roles, "Id")} = g.{C(grants, "RoleId")} AND ro.{C(roles, "TenantId")} = s.{C(seats, "TenantId")} AND ro.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)}\n" +
            $"CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM {KeysOf(roles, "ro", "each")}) held\n" +
            $"WHERE {scope} AND {Fn(KeyIsLive)}(held.k)";
    }

    /// <summary>
    /// Removes the rights <c>r</c> in <paramref name="rightsScope"/> that the grants in <paramref name="grantsScope"/>
    /// no longer give. The two scopes name the same rights, one by the rights' columns and one by the grants'.
    /// </summary>
    private static string RemoveStaleRights(TenancyTables tenancy, string rightsScope, string grantsScope)
    {
        var rights = tenancy.Rights;
        return
            $"DELETE FROM {Q(rights)} r\n" +
            $"WHERE {rightsScope}\n" +
            "  AND NOT EXISTS (SELECT 1 FROM (\n" +
            $"{Indented(DesiredRights(tenancy, grantsScope), 8, first: true)}) d\n" +
            $"    WHERE d.seat = r.{C(rights, "SeatId")} AND d.unit = r.{C(rights, "UnitId")} AND d.role = r.{C(rights, "RoleId")} AND d.key = r.{C(rights, "Key")})";
    }

    /// <summary>
    /// Adds the rights the grants in <paramref name="grantsScope"/> give that are not there, and brings the tenant
    /// and the period of those that are up to date. A row that already holds the right values is left alone, so
    /// what changes nothing about access writes no row, and no trigger on the rights fires for it.
    /// </summary>
    private static string WriteDesiredRights(TenancyTables tenancy, string grantsScope)
    {
        var rights = tenancy.Rights;
        var key = string.Join(", ", rights.FindPrimaryKey()!.Properties.Select(property => C(rights, property.Name)));
        string[] kept = ["TenantId", "StartsAt", "EndsAt"];
        return
            $"INSERT INTO {Q(rights)} AS r ({C(rights, "TenantId")}, {C(rights, "SeatId")}, {C(rights, "UnitId")}, {C(rights, "RoleId")}, {C(rights, "Key")}, {C(rights, "StartsAt")}, {C(rights, "EndsAt")})\n" +
            "SELECT d.tenant, d.seat, d.unit, d.role, d.key, d.starts, d.ends FROM (\n" +
            $"{Indented(DesiredRights(tenancy, grantsScope), 4, first: true)}) d\n" +
            $"ON CONFLICT ({key}) DO UPDATE SET {string.Join(", ", kept.Select(property => $"{C(rights, property)} = EXCLUDED.{C(rights, property)}"))}\n" +
            $"WHERE ({string.Join(", ", kept.Select(property => "r." + C(rights, property)))}) IS DISTINCT FROM ({string.Join(", ", kept.Select(property => "EXCLUDED." + C(rights, property)))})";
    }

    /// <summary><paramref name="sql"/> with every line after the first, or every line, <paramref name="spaces"/> further in.</summary>
    private static string Indented(string sql, int spaces, bool first = false)
    {
        var indent = new string(' ', spaces);
        return (first ? indent : string.Empty) + sql.Replace("\n", "\n" + indent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body of <c>manages_access(key)</c>: whether the key is one of the live keys <paramref name="catalogue"/>
    /// marks. A start-up check compares it with the function the database has.
    /// </summary>
    internal static string ManagesAccessBody(TenancyCatalogue catalogue)
        => "SELECT $1 IN (" + string.Join(", ", catalogue.AccessManagingKeys.Select(RowAccessModel.Literal)) + ")";

    /// <summary>
    /// The body of <c>pack_keys(pack)</c>: the keys, implied ones included, a role made from the pack of that key
    /// holds, as <paramref name="catalogue"/> expands them; null for a pack it does not have. A start-up check
    /// compares it with the function the database has.
    /// </summary>
    internal static string PackKeysBody(TenancyCatalogue catalogue)
    {
        if (catalogue.Packs.Count == 0)
        {
            return "SELECT NULL::pg_catalog.text[]";
        }

        var body = new StringBuilder("SELECT CASE $1");
        foreach (var pack in catalogue.Packs.OrderBy(pack => pack.Key, StringComparer.Ordinal))
        {
            // A pack with no keys is an empty array, written without braces: a brace in contributed SQL asks the
            // script to fill something in.
            var keys = pack.Keys.Order(StringComparer.Ordinal).Select(RowAccessModel.Literal);
            body.Append(" WHEN ").Append(RowAccessModel.Literal(pack.Key)).Append(" THEN ")
                .Append("ARRAY[").AppendJoin(", ", keys).Append("]::pg_catalog.text[]");
        }

        return body.Append(" END").ToString();
    }

    /// <summary>Whether <paramref name="value"/> is among the keys of <paramref name="alias"/>'s role, in the form the column's store type needs.</summary>
    private static string KeyAmong(IEntityType roles, string alias, string value)
        => KeysColumn(roles) switch
        {
            (var column, KeysStore.Array) => $"{value} = ANY ({alias}.{column})",
            (var column, var store) => $"EXISTS (SELECT 1 FROM {Elements(store, alias + "." + column)} AS held(k) WHERE held.k = {value})",
        };

    /// <summary>
    /// The keys of <paramref name="alias"/>'s role as a set of rows with one column, <c>k</c>, named <paramref name="name"/>:
    /// the keys it grants, or with <paramref name="property"/> another list of keys of the role, such as what its pack gave it.
    /// </summary>
    private static string KeysOf(IEntityType roles, string alias, string name, string property = "Keys")
        => KeysColumn(roles, property) switch
        {
            (var column, KeysStore.Array) => $"pg_catalog.unnest({alias}.{column}) AS {name}(k)",
            (var column, var store) => $"{Elements(store, alias + "." + column)} AS {name}(k)",
        };

    private static string Elements(KeysStore store, string column)
        => store == KeysStore.Jsonb ? $"pg_catalog.jsonb_array_elements_text({column})" : $"pg_catalog.json_array_elements_text({column})";

    /// <summary>
    /// Whether the keys of a role's row changed, from <c>OLD</c> to <c>NEW</c>, in a trigger's <c>WHEN</c>: a JSON
    /// document compared as <c>jsonb</c>, since <c>json</c> has no equality of its own.
    /// </summary>
    private static string KeysChanged(IEntityType roles)
        => KeysColumn(roles) switch
        {
            (var column, KeysStore.Json) => $"OLD.{column}::pg_catalog.jsonb <> NEW.{column}::pg_catalog.jsonb",
            (var column, _) => $"OLD.{column} <> NEW.{column}",
        };

    /// <summary>
    /// A row of <paramref name="table"/>'s role, active, joined to each of its keys that manage access, as
    /// <c>managed.k</c>: for a grant or a right, the keys whose holders alone give or take it away.
    /// </summary>
    private static string Managed(IEntityType roles, IEntityType table)
        => $"SELECT 1 FROM {Q(roles)} r CROSS JOIN LATERAL {KeysOf(roles, "r", "managed")} "
           + $"WHERE r.{C(roles, "Id")} = {TenancyTables.Own(table, "RoleId")} AND r.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)} "
           + $"AND {Fn(ManagesAccess)}(managed.k)";

    /// <summary>
    /// The calling seat holds, at the unit of <paramref name="table"/>'s row, each key that manages access of the
    /// row's role: true for a role that manages none, an archived one included.
    /// </summary>
    private static string Contained(IEntityType roles, IEntityType table)
        => $"NOT EXISTS ({Managed(roles, table)} AND NOT ({TenancyTables.Own(table, "UnitId")} = ANY (ARRAY(SELECT {Fn(UnitsWhereIHold)}(managed.k)))))";

    /// <summary>
    /// The role written is a copy of a pack of the catalogue: it names the pack, holds exactly its keys, and
    /// remembers exactly those as what the pack gave it, which the next sync of the packs compares the pack with.
    /// </summary>
    private static string CopyOfItsPack(IEntityType roles)
    {
        var pack = $"{Fn(PackKeys)}({TenancyTables.Own(roles, "FromPack")})";
        var packed = $"SELECT pg_catalog.unnest({pack})";
        string Exactly(string property)
        {
            var held = $"SELECT held.k FROM {KeysOf(roles, Q(roles), "held", property)}";
            return $"NOT EXISTS ({held} EXCEPT {packed}) AND NOT EXISTS ({packed} EXCEPT {held})";
        }

        return $"({pack} IS NOT NULL AND {Exactly("Keys")} AND {TenancyTables.Own(roles, KeysFromPackProperty)} IS NOT NULL AND {Exactly(KeysFromPackProperty)})";
    }

    /// <summary>The role written is made by hand: it names no pack, and remembers nothing a pack gave it.</summary>
    private static string MadeByHand(IEntityType roles)
        => $"({TenancyTables.Own(roles, "FromPack")} IS NULL AND {TenancyTables.Own(roles, KeysFromPackProperty)} IS NULL)";

    /// <summary>
    /// A column of the roles that holds keys, their keys by default, and how it is stored: an array, <c>text[]</c> on
    /// Npgsql by default, or a JSON document.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model stores the keys some other way.</exception>
    private static (string Column, KeysStore Store) KeysColumn(IEntityType roles, string property = "Keys")
    {
        var column = C(roles, property);
        var type = TenancyTables.ColumnType(roles, property).Trim().ToLowerInvariant();
        return type.EndsWith("[]", StringComparison.Ordinal) ? (column, KeysStore.Array)
            : type == "jsonb" ? (column, KeysStore.Jsonb)
            : type == "json" ? (column, KeysStore.Json)
            : throw new InvalidOperationException(
                $"A role's keys are stored as {type}, and Tenancy's SQL reads them from an array, such as text[], or from a JSON array. Map the keys as a primitive collection, which Npgsql stores as text[].");
    }

    private enum KeysStore
    {
        Array,
        Jsonb,
        Json,
    }

    // ------------------------------------------------------------------------------------------------ Tenancy's tables

    /// <summary>
    /// The policies on Tenancy's own tables. Signed-in users read their tenant's rows, except the rights, of which
    /// a seat reads its own, and the grants, which their own seat reads, a seat that manages grants, seats or units
    /// where they are, and a seat that manages roles for the whole tenant; and they write where the use case asks the
    /// key: at the unit for units, placements and grants, and held somewhere or for the whole tenant elsewhere; a seat
    /// also takes away its own grants and placements, as the use cases let it. System work reads its tenant, and
    /// writes it in Tenancy's own scope. Nobody writes the rights: the database keeps them itself. Every table is kept
    /// to its tenant for system work and closed to anonymous callers, and kept to this contribution.
    /// </summary>
    private static void OwnTables(TenancyTables tenancy, List<ContributedPolicy> policies, List<IEntityType> exclusive)
    {
        var tenants = tenancy.Tenants;
        var organizations = tenancy.Organizations;
        var units = tenancy.Units;
        var paths = tenancy.UnitPaths;
        var seats = tenancy.Seats;
        var placements = tenancy.Placements;
        var grants = tenancy.Grants;
        var rights = tenancy.Rights;
        var roles = tenancy.Roles;
        var revisions = tenancy.AccessRevisions;
        var callerSeat = $"(SELECT {Fn(CallerSeat)}())";

        void Add(IEntityType table, string name, string command, string? existing, string? left)
            => policies.Add(new ContributedPolicy(table, name, command, User, existing, left));

        // The directory: a person finds the tenants where they have a seat, whatever its status, to choose one.
        var directory = (IEntityType table) => $"({T(C(table, "Id"))} OR {C(table, "Id")} = ANY (ARRAY(SELECT {Fn(IdentityTenants)}())))";

        // A seat acts only in an active tenant, and only system work suspends, closes or reopens one.
        var tenant = Both(T(C(tenants, "Id")), W(SettingsKey));
        Add(tenants, "Seats read their tenants", "SELECT", directory(tenants), null);
        Add(tenants, "Settings managers change the tenant", "UPDATE", tenant, Both(tenant, $"{C(tenants, "Status")} = {TenancyTables.Stored(tenants, "Status", TenantStatus.Active)}"));

        var organization = Both(T(C(organizations, "Id")), $"({K(UnitsKey)} OR {W(SettingsKey)})");
        Add(organizations, "Seats read their organizations", "SELECT", directory(organizations), null);
        Add(organizations, "Unit and settings managers change it", "UPDATE", organization, organization);

        // A unit changes with the key at the unit; a move needs it at the new parent too. The root, which has no
        // parent, changes with the key at itself and stays the root: no unit that had a parent is left without
        // one, since a key held at a unit that became a root would be held for the whole tenant. The parent a unit
        // had is read as it was before the statement. The key at the unit is asked at the parent the row has as
        // well, which by the tree says the same: the save of a move rewrites the unit's own paths, and removes the
        // old ones before it changes the unit, so a key held from above the old parent alone would not be found
        // at the unit in between.
        var unitTenant = T(C(units, "TenantId"));
        var sameParent = $"{C(units, "ParentId")} IS NOT DISTINCT FROM (SELECT {Fn(UnitParent)}({C(units, "Id")}))";
        Add(units, "Members read the units", "SELECT", unitTenant, null);
        Add(units, "Unit managers add below their units", "INSERT", null, Both(unitTenant, U(UnitsKey, C(units, "ParentId"))));
        Add(
            units,
            "Unit managers change their units",
            "UPDATE",
            Both(unitTenant, $"({U(UnitsKey, C(units, "Id"))} OR {U(UnitsKey, C(units, "ParentId"))})"),
            Both(unitTenant, $"(({C(units, "ParentId")} IS NOT NULL AND {U(UnitsKey, C(units, "ParentId"))}) OR ({U(UnitsKey, C(units, "Id"))} AND {sameParent}))"));

        var pathTenant = T(C(paths, "TenantId"));
        var tree = Both(pathTenant, K(UnitsKey));
        Add(paths, "Members read the tree", "SELECT", pathTenant, null);
        Add(paths, "Unit managers write the tree", "INSERT", null, tree);
        Add(paths, "Unit managers write the tree", "UPDATE", tree, tree);
        Add(paths, "Unit managers write the tree", "DELETE", tree, null);

        // A seat's row is written by every use case that changes the seat, since each save writes the seat's version:
        // its status, with the seats key for the whole tenant; a placement, with the seats key at the unit, for any
        // seat of the tenant; a grant, with the grants key at a unit the seat is placed at; and the seat itself,
        // taking away what is its own. The application's own commands on its seat class write it too, and this policy
        // is all that lets them: the application cannot widen it, since Tenancy keeps the table to itself, and narrows
        // it column by column with column rules of its own. So the policy asks the seats or the grants key held
        // anywhere in the tenant, or the seat itself, and holds the grants key to no unit of the seat's: that would
        // decide for the application which of its own fields a grants manager writes. What that costs is the version:
        // a grants manager at one unit writes the version of any seat, which makes a save of that seat at the same
        // moment a concurrency conflict, as a seats manager anywhere could already. What is Tenancy's on the row the
        // triggers hold column by column: the id, the identity and the tenant change for no seat, and the status only
        // as the use cases change it. The application's own columns are its own to hold: Tenancy decides nothing of them.
        var seatTenant = T(C(seats, "TenantId"));
        var seat = Both(seatTenant, $"({K(SeatsKey)} OR {K(GrantsKey)} OR {C(seats, "Id")} = {callerSeat})");
        Add(seats, "Members and the person read seats", "SELECT", $"({seatTenant} OR {C(seats, "Identity")} = {Uid})", null);
        Add(seats, "Seat managers add seats", "INSERT", null, Both(seatTenant, W(SeatsKey)));
        Add(seats, "Managers and the seat change it", "UPDATE", seat, seat);

        // A placement is of a seat of the caller's tenant, and keeps its seat, unit and tenant (a trigger). It is
        // withdrawn only once its grants are gone: the database would take them with it, past their own policy.
        // Whether any are left is read as the caller, which the policy on the grants lets read them: its own, and
        // those at a unit where it manages seats. A seat takes away what is its own, as the use cases let it: the
        // policy for its own row asks no key, since its keys may be the first rows the same save removes.
        var placementTenant = T(C(placements, "TenantId"));
        var ownSeat = (IEntityType table) => $"{TenancyTables.Own(table, "SeatId")} = {callerSeat}";
        var seatOfTheTenant = $"EXISTS (SELECT 1 FROM {Q(seats)} s WHERE s.{C(seats, "Id")} = {TenancyTables.Own(placements, "SeatId")} AND {T("s." + C(seats, "TenantId"))})";
        var noGrantsLeft = $"NOT EXISTS (SELECT 1 FROM {Q(grants)} g WHERE g.{C(grants, "SeatId")} = {TenancyTables.Own(placements, "SeatId")} AND g.{C(grants, "UnitId")} = {TenancyTables.Own(placements, "UnitId")})";
        Add(placements, "Members read placements", "SELECT", placementTenant, null);
        Add(placements, "Seat managers place at the unit", "INSERT", null, Both(placementTenant, seatOfTheTenant, U(SeatsKey, C(placements, "UnitId"))));
        Add(
            placements,
            "Seat managers change placements",
            "UPDATE",
            Both(placementTenant, K(SeatsKey)),
            Both(placementTenant, seatOfTheTenant, K(SeatsKey), $"(NOT {C(placements, "IsPrimary")} OR {U(SeatsKey, C(placements, "UnitId"))})"));
        Add(
            placements,
            "Seat managers withdraw at the unit",
            "DELETE",
            Both(placementTenant, $"({U(SeatsKey, C(placements, "UnitId"))} OR {ownSeat(placements)})", noGrantsLeft),
            null);

        // A grant has no tenant of its own: its seat's. Giving and taking away a role that manages access is
        // contained: the caller holds each of its keys that manage access at the grant's unit, and never gives
        // one to itself. A role that manages no access goes by the grants key alone. A grant keeps its seat, unit,
        // role and giver (a trigger), so a change never makes another grant of it.
        var grantTenant = $"EXISTS (SELECT 1 FROM {Q(seats)} s WHERE s.{C(seats, "Id")} = {TenancyTables.Own(grants, "SeatId")} AND {T("s." + C(seats, "TenantId"))})";
        var grantUnit = U(GrantsKey, C(grants, "UnitId"));
        var activeRole = $"EXISTS (SELECT 1 FROM {Q(roles)} r WHERE r.{C(roles, "Id")} = {TenancyTables.Own(grants, "RoleId")} AND {T("r." + C(roles, "TenantId"))} AND r.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)})";
        var contained = Contained(roles, grants);
        var notToItself = $"(NOT EXISTS ({Managed(roles, grants)}) OR {TenancyTables.Own(grants, "SeatId")} <> {callerSeat})";

        // A grant is read by its own seat, and by a seat that manages grants, seats or units at its unit, or roles
        // for the whole tenant: a key reads only where it applies. A use case loads a seat with the grants its caller
        // may read, which are all those it acts on: each command asks its key at the unit it changes, and a change of
        // a seat's status, which reaches every grant of it, asks the seats key for the whole tenant. The seats key
        // at a unit reads the grants there too: withdrawing a placement takes its grants with it, so the use case
        // reads them to ask the grants key, and the policy on the placements reads them to keep a placement that
        // has any.
        Add(grants, "Seats and managers read grants", "SELECT", Both(grantTenant, ReadsGrantOf(TenancyTables.Own(grants, "SeatId"), TenancyTables.Own(grants, "UnitId"))), null);
        Add(grants, "Grants managers give at the unit", "INSERT", null, Both(grantTenant, grantUnit, activeRole, $"{C(grants, "GrantedBy")} = {callerSeat}", contained, notToItself));
        Add(grants, "Grants managers change at the unit", "UPDATE", Both(grantTenant, grantUnit, activeRole, contained), Both(grantTenant, grantUnit, activeRole, contained, notToItself));
        Add(grants, "Grants managers revoke at the unit", "DELETE", Both(grantTenant, $"({Both(grantUnit, contained)} OR {ownSeat(grants)})"), null);

        // A seat reads its own rights, and no other seat's, whatever it manages: what it may learn of those, the
        // functions answer. No caller writes a right: a trigger writes them as the grants, the seats and the roles
        // they follow from are written.
        Add(rights, "A seat reads its own rights", "SELECT", Both(T(C(rights, "TenantId")), ownSeat(rights)), null);

        // A role's keys that manage access change only with the role key held for the whole tenant, which every
        // change of a role asks here. A role manager adds a role made by hand, which names no pack, as the use case
        // that makes a role does; a role that names a pack is added only as a copy of it, remembering exactly what
        // the pack gave it, by a role or a settings manager, as the use case that changes a tenant's shape adds it.
        // So no seat makes a role that a sync of the packs would give what its pack never gave it.
        var roleTenant = T(C(roles, "TenantId"));
        Add(roles, "Members read roles", "SELECT", roleTenant, null);
        Add(
            roles,
            "Role and settings managers add roles",
            "INSERT",
            null,
            Both(roleTenant, $"({Both(W(RolesKey), MadeByHand(roles))} OR {Both($"({W(RolesKey)} OR {W(SettingsKey)})", CopyOfItsPack(roles))})"));
        Add(roles, "Role managers change roles", "UPDATE", Both(roleTenant, W(RolesKey)), Both(roleTenant, W(RolesKey)));

        // The revision is a counter every change of access takes, a seat's change of its own included: taking away
        // its own keys, or suspending itself, which leaves it no seat to be found by. A person with a seat of any
        // status in the tenant the connection names takes it.
        var revisionTenant = C(revisions, "TenantId");
        var seatedHere = Both(S(revisionTenant), $"{revisionTenant} = ANY (ARRAY(SELECT {Fn(IdentityTenants)}()))");
        Add(revisions, "Seats of the tenant read the revision", "SELECT", seatedHere, null);
        Add(revisions, "Seats of the tenant take the revision", "UPDATE", seatedHere, seatedHere);

        foreach (var table in tenancy.All)
        {
            var own = table == grants
                ? $"EXISTS (SELECT 1 FROM {Q(seats)} s WHERE s.{C(seats, "Id")} = {TenancyTables.Own(grants, "SeatId")} AND {S("s." + C(seats, "TenantId"))})"
                : S(C(table, TenantProperty(tenancy, table)));
            SystemWork(table, own, policies, writes: table != rights);
            exclusive.Add(table);
        }
    }

    /// <summary>
    /// The policies on the invitations and on the digests of their tokens, where the model maps them
    /// (<c>AddTenancyInvitations</c>). An invitation is read by the seats that manage seats at its unit, as the use
    /// case lists it. It is added by a seat that could add the seat and make the grant itself: one that manages
    /// seats for the whole tenant and grants at the unit, and for a role that manages access holds each of its keys
    /// that do there, as the policy on the grants asks; open, as its own, and never as system work's, which the
    /// acceptance would trust. It is changed by a seat that manages seats at its unit, while it is open and to
    /// nothing but cancelled: accepting is the application's own work. What it offers is fixed for every role, by
    /// a trigger.
    /// <para>
    /// The digest of a token is read by nobody: the table has no policy that lets any caller's role read it, the
    /// scoped system role's and an operator's included. A seat adds the digest of an invitation of its own that the
    /// running transaction wrote, and Tenancy's system work of one it wrote; which invitation a digest is for is
    /// answered by a function. Both tables are kept to their tenant and to this contribution.
    /// </para>
    /// </summary>
    private static void Invitations(TenancyTables tenancy, List<ContributedPolicy> policies, List<IEntityType> exclusive, List<IEntityType> unread)
    {
        if (tenancy is not { Invitations: { } invitations, InvitationDigests: { } digests })
        {
            return;
        }

        var roles = tenancy.Roles;
        var callerSeat = $"(SELECT {Fn(CallerSeat)}())";
        string State(InvitationState state) => TenancyTables.Stored(invitations, "State", state);

        var tenant = T(C(invitations, "TenantId"));
        var managesSeatsThere = U(SeatsKey, C(invitations, "UnitId"));
        var open = $"{C(invitations, "State")} = {State(InvitationState.Open)}";
        var notAccepted = $"{C(invitations, "AcceptedAs")} IS NULL AND {C(invitations, "AcceptedAt")} IS NULL";
        var activeRole =
            $"EXISTS (SELECT 1 FROM {Q(roles)} r WHERE r.{C(roles, "Id")} = {TenancyTables.Own(invitations, "RoleId")} AND {T("r." + C(roles, "TenantId"))} " +
            $"AND r.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)})";

        policies.Add(new ContributedPolicy(invitations, "Seat managers read invitations", "SELECT", User, Both(tenant, managesSeatsThere), null));
        policies.Add(new ContributedPolicy(
            invitations,
            "Seat managers issue invitations",
            "INSERT",
            User,
            null,
            Both(
                tenant,
                W(SeatsKey),
                U(GrantsKey, C(invitations, "UnitId")),
                activeRole,
                Contained(roles, invitations),
                $"NOT {C(invitations, "IssuedAsSystem")}",
                $"{C(invitations, "IssuedBy")} = {callerSeat}",
                open,
                notAccepted)));
        policies.Add(new ContributedPolicy(
            invitations,
            "Seat managers cancel invitations",
            "UPDATE",
            User,
            Both(tenant, managesSeatsThere, open),
            Both(tenant, managesSeatsThere, $"{C(invitations, "State")} IN ({State(InvitationState.Open)}, {State(InvitationState.Cancelled)})", notAccepted)));
        SystemWork(invitations, S(C(invitations, "TenantId")), policies);
        exclusive.Add(invitations);

        // The invitation the digest is of, written by the running transaction: nobody adds a digest to an
        // invitation that was there already, which would give a token of their own making to somebody's offer.
        string WrittenNow(string and) =>
            $"EXISTS (SELECT 1 FROM {Q(invitations)} i WHERE i.{C(invitations, "Id")} = {TenancyTables.Own(digests, "InvitationId")} " +
            $"AND i.{C(invitations, "TenantId")} = {TenancyTables.Own(digests, "TenantId")} AND ddd.written_in_this_transaction(i.xmin){and})";

        var digestTenant = C(digests, "TenantId");
        policies.Add(new ContributedPolicy(
            digests,
            "Issuers keep their token's digest",
            "INSERT",
            User,
            null,
            Both(T(digestTenant), WrittenNow($" AND i.{C(invitations, "IssuedBy")} = {callerSeat} AND i.{C(invitations, "State")} = {State(InvitationState.Open)}"))));
        policies.Add(new ContributedPolicy(
            digests,
            "Tenancy work keeps a token's digest",
            "INSERT",
            SystemIn,
            null,
            Both(S(digestTenant), Scope, WrittenNow(string.Empty))));
        KeptToTheTenant(digests, systemIn: S(digestTenant), user: null, policies);
        exclusive.Add(digests);
        unread.Add(digests);
    }

    /// <summary>The property that holds the tenant of one of Tenancy's tables, other than the grants', which have none.</summary>
    private static string TenantProperty(TenancyTables tenancy, IEntityType table)
        => table == tenancy.Tenants || table == tenancy.Organizations ? "Id" : "TenantId";

    /// <summary>
    /// What system work may do with one of Tenancy's tables, whose row is of the tenant <paramref name="ofItsTenant"/>
    /// says: read it in any scope, and with <paramref name="writes"/> write it in Tenancy's own; kept to that
    /// tenant, and closed to anonymous callers. The rights it only reads: the database writes them.
    /// </summary>
    private static void SystemWork(IEntityType table, string ofItsTenant, List<ContributedPolicy> policies, bool writes = true)
    {
        policies.Add(new ContributedPolicy(table, "System work reads its tenant", "SELECT", SystemIn, ofItsTenant, null));
        if (writes)
        {
            var write = Both(ofItsTenant, Scope);
            policies.Add(new ContributedPolicy(table, "Tenancy work writes its tenant", "INSERT", SystemIn, null, write));
            policies.Add(new ContributedPolicy(table, "Tenancy work writes its tenant", "UPDATE", SystemIn, write, write));
            policies.Add(new ContributedPolicy(table, "Tenancy work writes its tenant", "DELETE", SystemIn, write, null));
        }

        KeptToTheTenant(table, systemIn: ofItsTenant, user: null, policies);
    }

    /// <summary>
    /// The restrictive policies of a table that keep every caller to its tenant: system work stays in its tenant, a signed-in user in the calling seat's
    /// when <paramref name="user"/> is given, and anonymous callers get nothing; <see cref="For"/> closes the table
    /// to the mapped token roles the same way. Restrictive policies are never
    /// merged, so a permissive policy for the same role, a rule's or another contribution's, cannot widen them.
    /// </summary>
    private static void KeptToTheTenant(IEntityType table, string systemIn, string? user, List<ContributedPolicy> policies)
    {
        policies.Add(new ContributedPolicy(table, "Kept to its tenant", "ALL", SystemIn, systemIn, systemIn, Restrictive: true));
        if (user is not null)
        {
            policies.Add(new ContributedPolicy(table, "Kept to its tenant", "ALL", User, user, user, Restrictive: true));
        }

        policies.Add(new ContributedPolicy(table, "Closed to anonymous callers", "ALL", Anonymous, "false", "false", Restrictive: true));
    }

    /// <summary>
    /// What an operator's role may do with a table that is kept to a tenant: read, and nothing else. On a table
    /// only this contribution writes policies for, one of Tenancy's own, <paramref name="readsEveryRow"/> lets it
    /// read every row, in every tenant. On a module's table it gets no such policy, so it reads there what a rule
    /// of the module for that role admits. The restrictive policies say the rest: whatever a permissive policy
    /// would allow, a rule's or another contribution's, the role adds, changes and removes nothing.
    /// </summary>
    private static void Operators(IEntityType table, string operatorRole, bool readsEveryRow, List<ContributedPolicy> policies)
    {
        if (readsEveryRow)
        {
            policies.Add(new ContributedPolicy(table, "Operators read every tenant", "SELECT", operatorRole, "true", null));
        }

        policies.Add(new ContributedPolicy(table, "Operators only read", "SELECT", operatorRole, "true", null, Restrictive: true));
        policies.Add(new ContributedPolicy(table, "Operators only read", "INSERT", operatorRole, null, "false", Restrictive: true));
        policies.Add(new ContributedPolicy(table, "Operators only read", "UPDATE", operatorRole, "false", "false", Restrictive: true));
        policies.Add(new ContributedPolicy(table, "Operators only read", "DELETE", operatorRole, "false", null, Restrictive: true));
    }

    /// <summary>
    /// The policies on Tenancy's access history, where the model maps one (<c>AddTenancyEventLogTable</c>): a row
    /// is added by the save that raised its event, and never changed. A person adds rows about their own seat in
    /// the tenant the connection names, so nobody writes that somebody else did something, and a seat reads its
    /// tenant's rows with the history key held for the whole tenant. System work reads its tenant's rows in any
    /// scope, and adds to them in Tenancy's own, which is the scope Tenancy's events are raised in, as the system,
    /// an operator or a token and never as a seat: a row is kept for good, so what it says of who acted is held
    /// to the role that wrote it, as the trigger on the columns of who changed a row holds it. With
    /// <paramref name="removesOldRows"/>, the role of the host's own bookkeeping reads and removes rows across
    /// tenants, for the retention of the rows the table's guard lets go. The table is kept to its tenant and to
    /// this contribution, like Tenancy's own.
    /// <para>
    /// Who adds a row is the calling seat: the person's active seat in the tenant. A seat that suspends or
    /// deactivates itself is no seat to be found by once its row is written, and the same save still records
    /// that it did: the person's seat in the tenant counts as well while the running transaction is the one
    /// that wrote it, and in no later one. The calling seat is asked first, in so many words, so that the seat's
    /// row is only looked at where there is no calling seat.
    /// </para>
    /// </summary>
    private static void History(TenancyTables tenancy, bool removesOldRows, List<ContributedPolicy> policies, List<IEntityType> exclusive)
    {
        if (tenancy.History is not var (log, letsRowsGo))
        {
            return;
        }

        var seats = tenancy.Seats;
        var tenant = C(log, TenancyEventLogTable.TenantId);
        var actor = TenancyTables.Own(log, nameof(EventLogEntry.ActedById));
        var bySeat = $"{C(log, nameof(EventLogEntry.ActedByKind))} = {RowAccessModel.Literal(TenancyActorKinds.Seat)}";
        var stoppedInThisSave =
            $"EXISTS (SELECT 1 FROM {Q(seats)} s WHERE s.{C(seats, "Identity")} = {Uid} AND s.{C(seats, "TenantId")} = {TenancyTables.Own(log, TenancyEventLogTable.TenantId)} " +
            $"AND s.{C(seats, "Id")}::pg_catalog.text = {actor} AND ddd.written_in_this_transaction(s.xmin))";
        var byTheCaller = $"(CASE WHEN {actor} = (SELECT {Fn(CallerSeat)}())::pg_catalog.text THEN true ELSE {stoppedInThisSave} END)";

        // The kinds system work is recorded as, named one by one: a kind nobody thought of yet is refused, not let in.
        var notBySeat = $"{C(log, nameof(EventLogEntry.ActedByKind))} IN ({string.Join(", ", new[] { TenancyActorKinds.System, TenancyActorKinds.Operator, TenancyActorKinds.Token }.Select(RowAccessModel.Literal))})";

        policies.Add(new ContributedPolicy(log, "Seats record what they do", "INSERT", User, null, Both(S(tenant), bySeat, byTheCaller)));
        policies.Add(new ContributedPolicy(log, "History readers read their tenant", "SELECT", User, Both(T(tenant), W(HistoryKey)), null));
        policies.Add(new ContributedPolicy(log, "System work reads its tenant", "SELECT", SystemIn, S(tenant), null));
        policies.Add(new ContributedPolicy(log, "Tenancy work records its tenant", "INSERT", SystemIn, null, Both(S(tenant), Scope, notBySeat)));

        if (removesOldRows && letsRowsGo)
        {
            // Finding the rows that are old enough, and removing them: the guard refuses a younger one.
            policies.Add(new ContributedPolicy(log, "Bookkeeping removes old rows", "SELECT", RowAccessRoles.System, "true", null));
            policies.Add(new ContributedPolicy(log, "Bookkeeping removes old rows", "DELETE", RowAccessRoles.System, "true", null));
        }

        KeptToTheTenant(log, systemIn: S(tenant), user: null, policies);
        exclusive.Add(log);
    }

    /// <summary>
    /// The tables of the application's own entities on Tenancy's aggregates, a note on a tenant say: read with
    /// the row they belong to, in the calling seat's tenant, and written by whoever may change that row, since
    /// changing one changes the row's version too. Kept to this contribution, like Tenancy's own tables.
    /// </summary>
    private static void HostEntities(TenancyTables tenancy, List<ContributedPolicy> policies, List<IEntityType> exclusive)
    {
        var callerSeat = $"(SELECT {Fn(CallerSeat)}())";
        (IEntityType Root, string Tenant, Func<string, string> Changes)[] roots =
        [
            (tenancy.Tenants, "Id", _ => W(SettingsKey)),
            (tenancy.Organizations, "Id", _ => $"({K(UnitsKey)} OR {W(SettingsKey)})"),
            (tenancy.Seats, "TenantId", alias => $"({K(SeatsKey)} OR {K(GrantsKey)} OR {alias}.{C(tenancy.Seats, "Id")} = {callerSeat})"),
            (tenancy.Roles, "TenantId", _ => W(RolesKey)),
            .. tenancy.Invitations is { } invitations
                ? new (IEntityType Root, string Tenant, Func<string, string> Changes)[]
                {
                    (invitations, "TenantId", alias => $"{alias}.{C(invitations, "UnitId")} = ANY (ARRAY(SELECT {Fn(UnitsWhereIHold)}({RowAccessModel.Literal(SeatsKey)})))"),
                }
                : [],
        ];

        foreach (var (root, tenant, changes) in roots)
        {
            foreach (var chain in RowAccessModel.EntityTablesOf(root))
            {
                if (TenancyModel.TableOf(chain.Entity) is not null || exclusive.Contains(chain.Entity))
                {
                    continue;
                }

                var ofTenant = Through(chain, alias => T($"{alias}.{C(root, tenant)}"));
                var written = Through(chain, alias => Both(T($"{alias}.{C(root, tenant)}"), changes(alias)));
                policies.Add(new ContributedPolicy(chain.Entity, "Read with the row it belongs to", "SELECT", User, ofTenant, null));
                policies.Add(new ContributedPolicy(chain.Entity, "Written with the row it belongs to", "INSERT", User, null, written));
                policies.Add(new ContributedPolicy(chain.Entity, "Written with the row it belongs to", "UPDATE", User, written, written));
                policies.Add(new ContributedPolicy(chain.Entity, "Written with the row it belongs to", "DELETE", User, written, null));

                SystemWork(chain.Entity, Through(chain, alias => S($"{alias}.{C(root, tenant)}")), policies);
                exclusive.Add(chain.Entity);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the modules' tables

    /// <summary>
    /// Every table of the model kept to a tenant that is not Tenancy's, and the tables of its entities: row level
    /// security on, system work of any scope reading and writing its tenant, and restrictive policies that keep every
    /// caller to its tenant,
    /// so the modules' rules never repeat the tenant and no permissive policy reaches past it. A table without a
    /// rule for signed-in users is closed to them.
    /// </summary>
    /// <exception cref="InvalidOperationException">A type kept to a tenant has a table of its own apart from the type it derives from.</exception>
    private static void ScopedTables(IModel model, List<ContributedPolicy> policies)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entityType in model.GetEntityTypes().OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (TenancyModel.TableOf(entityType) is not null
                || entityType.GetTableName() is null
                || entityType.GetViewName() is not null
                || TenancyModel.TenantPropertyOf(entityType) is not { } tenant)
            {
                continue;
            }

            if (entityType.BaseType is { } baseType && TenancyTables.Table(baseType) == TenancyTables.Table(entityType))
            {
                // A type of a hierarchy in one table: the table is kept to the tenant once, from the type it starts from.
                continue;
            }

            if (entityType.BaseType is not null)
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()} is kept to a tenant in {TenancyTables.Table(entityType)}, a table of its own apart from the type it derives from, and the policies that keep a table to its tenant follow a hierarchy only in one table. Map the hierarchy to one table, or keep the base type's rows to the tenant.");
            }

            if (!done.Add(TenancyTables.Table(entityType)))
            {
                continue;
            }

            var column = C(entityType, tenant.Name);
            Scoped(entityType, S(column), T(column), policies);

            foreach (var chain in RowAccessModel.EntityTablesOf(entityType))
            {
                if (done.Add(chain.Table))
                {
                    Scoped(
                        chain.Entity,
                        Through(chain, alias => S($"{alias}.{column}")),
                        Through(chain, alias => T($"{alias}.{column}")),
                        policies);
                }
            }
        }
    }

    /// <summary>A table of a module kept to a tenant: system work of any scope in its tenant, and every caller kept to it.</summary>
    private static void Scoped(IEntityType table, string systemIn, string user, List<ContributedPolicy> policies)
    {
        policies.Add(new ContributedPolicy(table, "System work in its tenant", "ALL", SystemIn, systemIn, systemIn));
        KeptToTheTenant(table, systemIn, user, policies);
    }

    /// <summary>
    /// A condition about the root row of <paramref name="chain"/>'s entity, asked through every table in between:
    /// <c>EXISTS (SELECT 1 FROM "Tenants" r WHERE r."Id" = "TenantNote"."HostTenantId" AND ...)</c>. The root is
    /// <c>r</c>, the tables between <c>p1</c> and on.
    /// </summary>
    private static string Through(EntityTableChain chain, Func<string, string> onRoot)
    {
        var aliases = chain.Links.Select((_, level) => level == chain.Links.Count - 1 ? "r" : "p" + (level + 1)).ToList();
        var exists = onRoot("r");
        for (var level = chain.Links.Count - 1; level >= 0; level--)
        {
            var link = chain.Links[level];
            var below = level == 0 ? chain.Table : aliases[level - 1];
            var belongs = string.Join(" AND ", link.ParentColumns.Zip(link.Columns, (parent, child) => $"{aliases[level]}.{parent} = {below}.{child}"));
            exists = $"EXISTS (SELECT 1 FROM {link.Parent} {aliases[level]} WHERE {belongs} AND {exists})";
        }

        return exists;
    }

    /// <summary>The conditions, all of which must hold, each in parentheses unless it is one group already.</summary>
    private static string Both(params string[] conditions) => "(" + string.Join(" AND ", conditions.Select(Grouped)) + ")";

    /// <summary><paramref name="condition"/> in parentheses, unless the whole of it is one group of them already.</summary>
    private static string Grouped(string condition)
    {
        if (!condition.StartsWith('('))
        {
            return "(" + condition + ")";
        }

        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < condition.Length; i++)
        {
            var character = condition[i];
            if (quote != '\0')
            {
                quote = character == quote ? '\0' : quote;
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0 && i < condition.Length - 1)
            {
                return "(" + condition + ")";
            }
        }

        return condition;
    }

    // ------------------------------------------------------------------------------------------------ the triggers

    /// <summary>
    /// The trigger that writes the rights, and the triggers that keep what a policy cannot see, because it is
    /// about rows other than the one written, about the row as it was, or about one of its columns: a tenant keeps
    /// an administrator, every right is backed by a grant, the closure follows the tree, a placement and a grant
    /// keep what they are about, a seat keeps what it is for every caller but Tenancy's system work in its tenant,
    /// and a seat's status changes only by a seat that may give or take away what it holds. The rights are written
    /// as the row they follow from is; a tenant's administrator, the rights' grants and the closure are checked at
    /// commit, once the save has written every row it writes, the others as the row changes; all of them fire for
    /// every role, the tables' owner included. Each can run again.
    /// </summary>
    private static IEnumerable<string> Triggers(TenancyTables tenancy, Written written)
    {
        var schema = Quoted(tenancy.Schema);
        foreach (var statement in RightsFollowTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in AdministratorTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in RightsTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in TreeTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in IdentityTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in SeatIdentityTriggers(tenancy, schema, written))
        {
            yield return statement;
        }

        foreach (var statement in StatusTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in PackTriggers(tenancy, schema))
        {
            yield return statement;
        }

        foreach (var statement in InvitationTriggers(tenancy, schema))
        {
            yield return statement;
        }
    }

    /// <summary>
    /// The trigger that keeps the rights: whenever a grant, a seat or a role is written, the rights it reaches
    /// are written again from what is stored, in the same statement, so they are never behind, inside the
    /// transaction either. A grant reaches the rights of its seat, unit and role; a seat's status every right of
    /// the seat; a role's status, keys or tenant every right the role gives. Whichever of the three a save writes
    /// last completes the rows, whatever order it writes them in. Only what differs is written. It runs as its
    /// owner, since no caller may write a right, and for every writer, a statement past the use cases included.
    /// <para>
    /// It takes no lock of its own. The use cases write a tenant's access one command after the other, so each
    /// reads what the one before stored. Two transactions past them that write at once do not see each other: a
    /// right written from a role's keys while another transaction takes the key away is refused when the second
    /// of the two commits, by the check that every right comes from a grant; a right left unwritten because the
    /// key was being added is not refused, and is added when the tenant's rights are written again.
    /// </para>
    /// </summary>
    private static IEnumerable<string> RightsFollowTriggers(TenancyTables tenancy, string schema)
    {
        var seats = tenancy.Seats;
        var roles = tenancy.Roles;
        var rights = tenancy.Rights;
        var grants = tenancy.Grants;
        var function = $"{schema}.{RightsFollowGrants}";

        // Brings the rights in a scope up to date: those the grants there no longer give go, the rest are written.
        string Follow(string rightsScope, string grantsScope, int indent)
            => Indented(RemoveStaleRights(tenancy, rightsScope, grantsScope) + ";\n" + WriteDesiredRights(tenancy, grantsScope) + ";", indent, first: true);

        string OfGrant(string alias, IEntityType table)
            => $"{alias}.{C(table, "SeatId")} = of_seat AND {alias}.{C(table, "UnitId")} = at_unit AND {alias}.{C(table, "RoleId")} = of_role";

        var body = new StringBuilder()
            .Append("CREATE OR REPLACE FUNCTION ").Append(function).Append("() RETURNS trigger\n")
            .Append("    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n")
            .Append("DECLARE\n")
            .Append("    of_seat ").Append(TenancyTables.Cast(TenancyTables.ColumnType(seats, "Id"))).Append(";\n")
            .Append("    at_unit ").Append(TenancyTables.Cast(TenancyTables.ColumnType(grants, "UnitId"))).Append(";\n")
            .Append("    of_role ").Append(TenancyTables.Cast(TenancyTables.ColumnType(roles, "Id"))).Append(";\n")
            .Append("    pass pg_catalog.int4;\n")
            .Append("BEGIN\n")
            .Append("    CASE TG_ARGV[0]\n")
            .Append("        WHEN 'grants' THEN\n")
            .Append("            -- The rights of the grant as it was, and as it is where that is another seat's, unit's or role's.\n")
            .Append("            FOR pass IN 1..2 LOOP\n")
            .Append("                IF pass = 1 THEN\n")
            .Append("                    CONTINUE WHEN TG_OP = 'INSERT';\n")
            .Append("                    of_seat := OLD.").Append(C(grants, "SeatId")).Append("; at_unit := OLD.").Append(C(grants, "UnitId")).Append("; of_role := OLD.").Append(C(grants, "RoleId")).Append(";\n")
            .Append("                ELSE\n")
            .Append("                    CONTINUE WHEN TG_OP = 'DELETE';\n")
            .Append("                    CONTINUE WHEN TG_OP = 'UPDATE' AND (NEW.").Append(C(grants, "SeatId")).Append(", NEW.").Append(C(grants, "UnitId")).Append(", NEW.").Append(C(grants, "RoleId"))
            .Append(") IS NOT DISTINCT FROM (of_seat, at_unit, of_role);\n")
            .Append("                    of_seat := NEW.").Append(C(grants, "SeatId")).Append("; at_unit := NEW.").Append(C(grants, "UnitId")).Append("; of_role := NEW.").Append(C(grants, "RoleId")).Append(";\n")
            .Append("                END IF;\n")
            .Append(Follow(OfGrant("r", rights), OfGrant("g", grants), 16)).Append('\n')
            .Append("            END LOOP;\n")
            .Append("        WHEN 'seats' THEN\n")
            .Append("            IF TG_OP = 'DELETE' THEN of_seat := OLD.").Append(C(seats, "Id")).Append("; ELSE of_seat := NEW.").Append(C(seats, "Id")).Append("; END IF;\n")
            .Append(Follow($"r.{C(rights, "SeatId")} = of_seat", $"g.{C(grants, "SeatId")} = of_seat", 12)).Append('\n')
            .Append("        WHEN 'roles' THEN\n")
            .Append("            IF TG_OP = 'DELETE' THEN of_role := OLD.").Append(C(roles, "Id")).Append("; ELSE of_role := NEW.").Append(C(roles, "Id")).Append("; END IF;\n")
            .Append(Follow($"r.{C(rights, "RoleId")} = of_role", $"g.{C(grants, "RoleId")} = of_role", 12)).Append('\n')
            .Append("    END CASE;\n")
            .Append("    RETURN NULL;\n")
            .Append("END\n")
            .Append("$body$");

        yield return body.ToString();
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";

        (IEntityType Table, string Events, string Kind)[] firing =
        [
            (grants, "INSERT OR UPDATE OR DELETE", "grants"),
            (seats, $"INSERT OR UPDATE OF {C(seats, "Status")} OR DELETE", "seats"),
            (roles, $"INSERT OR UPDATE OF {C(roles, "Status")}, {C(roles, "Keys")}, {C(roles, "TenantId")} OR DELETE", "roles"),
        ];

        foreach (var (table, events, kind) in firing)
        {
            yield return $"DROP TRIGGER IF EXISTS {RightsFollowGrantsTrigger} ON {Q(table)}";
            yield return
                $"CREATE TRIGGER {RightsFollowGrantsTrigger} AFTER {events} ON {Q(table)}\n" +
                $"    FOR EACH ROW EXECUTE FUNCTION {function}({RowAccessModel.Literal(kind)})";
        }
    }

    private static IEnumerable<string> AdministratorTriggers(TenancyTables tenancy, string schema)
    {
        var tenants = tenancy.Tenants;
        var units = tenancy.Units;
        var seats = tenancy.Seats;
        var roles = tenancy.Roles;
        var rights = tenancy.Rights;
        var placements = tenancy.Placements;
        var grants = tenancy.Grants;
        var key = RowAccessModel.Literal(TenancyKeys.AdministratorKey);
        var function = $"{schema}.{AdministratorRemains}";
        var tenantType = TenancyTables.ColumnType(tenants, "Id");

        // A rights row that makes an administrator: at the root, for the administrator key, for good, started.
        string Administrating(string alias)
            => $"{alias}.{C(rights, "Key")} = {key} AND {alias}.{C(rights, "EndsAt")} IS NULL AND {alias}.{C(rights, "StartsAt")} <= pg_catalog.now()";
        string AtTheRoot(string alias)
            => $"EXISTS (SELECT 1 FROM {Q(units)} u WHERE u.{C(units, "Id")} = {alias}.{C(rights, "UnitId")} AND u.{C(units, "TenantId")} = {alias}.{C(rights, "TenantId")} AND u.{C(units, "ParentId")} IS NULL)";
        string StillHeld(string where)
            => $"SELECT r.{C(rights, "TenantId")} FROM {Q(rights)} r WHERE {where} AND {Administrating("r")} AND {AtTheRoot("r")} LIMIT 1";

        var body = new StringBuilder()
            .Append("CREATE OR REPLACE FUNCTION ").Append(function).Append("() RETURNS trigger\n")
            .Append("    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n")
            .Append("DECLARE\n")
            .Append("    tenant ").Append(TenancyTables.Cast(tenantType)).Append(";\n")
            .Append("BEGIN\n")
            .Append("    -- Whether the old row was part of an administrator, and whose.\n")
            .Append("    CASE TG_ARGV[0]\n")
            .Append("        WHEN 'rights' THEN\n")
            .Append("            IF NOT (").Append(AtTheRoot("OLD")).Append(" AND OLD.").Append(C(rights, "StartsAt")).Append(" <= pg_catalog.now()) THEN\n")
            .Append("                RETURN NULL;\n")
            .Append("            END IF;\n")
            .Append("            tenant := OLD.").Append(C(rights, "TenantId")).Append(";\n")
            .Append("        WHEN 'seats' THEN\n")
            .Append("            tenant := (").Append(StillHeld($"r.{C(rights, "SeatId")} = OLD.{C(seats, "Id")}")).Append(");\n")
            .Append("        WHEN 'roles' THEN\n")
            .Append("            tenant := (").Append(StillHeld($"r.{C(rights, "RoleId")} = OLD.{C(roles, "Id")}")).Append(");\n")
            .Append("        WHEN 'grants' THEN\n")
            .Append("            tenant := (").Append(StillHeld($"r.{C(rights, "SeatId")} = OLD.{C(grants, "SeatId")} AND r.{C(rights, "UnitId")} = OLD.{C(grants, "UnitId")} AND r.{C(rights, "RoleId")} = OLD.{C(grants, "RoleId")}")).Append(");\n")
            .Append("        WHEN 'placements' THEN\n")
            .Append("            tenant := (").Append(StillHeld($"r.{C(rights, "SeatId")} = OLD.{C(placements, "SeatId")} AND r.{C(rights, "UnitId")} = OLD.{C(placements, "UnitId")}")).Append(");\n")
            .Append("        WHEN 'units' THEN\n")
            .Append("            tenant := (SELECT r.").Append(C(rights, "TenantId")).Append(" FROM ").Append(Q(rights)).Append(" r WHERE r.").Append(C(rights, "UnitId")).Append(" = OLD.").Append(C(units, "Id"))
            .Append(" AND ").Append(Administrating("r")).Append(" LIMIT 1);\n")
            .Append("    END CASE;\n")
            .Append("    IF tenant IS NULL THEN\n")
            .Append("        RETURN NULL;\n")
            .Append("    END IF;\n")
            .Append("    -- One transaction at a time asks, per tenant, until it ends: two that each take away another\n")
            .Append("    -- administrator would otherwise each still see the other's. Each statement after the wait reads\n")
            .Append("    -- what the one before committed.\n")
            .Append("    PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended(")
            .Append(RowAccessModel.Literal(AdministratorRemainsConstraint + " ")).Append(" || tenant::pg_catalog.text, 0));\n")
            .Append("    -- A closed tenant needs none; an active or a suspended one keeps one.\n")
            .Append("    IF EXISTS (SELECT 1 FROM ").Append(Q(tenants)).Append(" t WHERE t.").Append(C(tenants, "Id")).Append(" = tenant\n")
            .Append("                 AND t.").Append(C(tenants, "Status")).Append(" IN (").Append(TenancyTables.Stored(tenants, "Status", TenantStatus.Active)).Append(", ").Append(TenancyTables.Stored(tenants, "Status", TenantStatus.Suspended)).Append("))\n")
            .Append("       AND NOT EXISTS (SELECT 1 FROM ").Append(Q(rights)).Append(" r\n")
            .Append("                       JOIN ").Append(Q(seats)).Append(" s ON s.").Append(C(seats, "Id")).Append(" = r.").Append(C(rights, "SeatId")).Append(" AND s.").Append(C(seats, "Status")).Append(" = ").Append(TenancyTables.Stored(seats, "Status", SeatStatus.Active)).Append('\n')
            .Append("                       JOIN ").Append(Q(roles)).Append(" ro ON ro.").Append(C(roles, "Id")).Append(" = r.").Append(C(rights, "RoleId")).Append(" AND ro.").Append(C(roles, "Status")).Append(" = ").Append(TenancyTables.Stored(roles, "Status", RoleStatus.Active)).Append('\n')
            .Append("                       WHERE r.").Append(C(rights, "TenantId")).Append(" = tenant AND ").Append(Administrating("r")).Append(" AND ").Append(AtTheRoot("r")).Append(") THEN\n")
            .Append("        RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = ").Append(RowAccessModel.Literal(AdministratorRemainsConstraint))
            .Append(", MESSAGE = 'A tenant keeps at least one administrator.';\n")
            .Append("    END IF;\n")
            .Append("    RETURN NULL;\n")
            .Append("END\n")
            .Append("$body$");

        yield return body.ToString();
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";

        (IEntityType Table, string Events, string When, string Kind)[] firing =
        [
            (rights, "UPDATE OR DELETE", $"OLD.{C(rights, "Key")} = {key} AND OLD.{C(rights, "EndsAt")} IS NULL", "rights"),
            (seats, $"UPDATE OF {C(seats, "Status")} OR DELETE", $"OLD.{C(seats, "Status")} = {TenancyTables.Stored(seats, "Status", SeatStatus.Active)}", "seats"),
            (roles, "UPDATE OR DELETE", $"OLD.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)}", "roles"),
            (grants, "UPDATE OR DELETE", $"OLD.{C(grants, "EndsAt")} IS NULL", "grants"),
            (placements, "UPDATE OR DELETE", "", "placements"),
            (units, $"UPDATE OF {C(units, "ParentId")}", $"OLD.{C(units, "ParentId")} IS NULL", "units"),
        ];

        foreach (var (table, events, when, kind) in firing)
        {
            foreach (var statement in AtCommit(AdministratorRemainsConstraint, table, events, when, function, kind))
            {
                yield return statement;
            }
        }
    }

    private static IEnumerable<string> RightsTriggers(TenancyTables tenancy, string schema)
    {
        var seats = tenancy.Seats;
        var roles = tenancy.Roles;
        var rights = tenancy.Rights;
        var grants = tenancy.Grants;
        var function = $"{schema}.{RightsBackedByGrants}";

        // A grant of the row's seat, unit and role, for the row's period, of an active seat of the row's tenant and
        // an active role of that tenant whose keys hold the row's key.
        var backed =
            $"EXISTS (SELECT 1 FROM {Q(grants)} g\n" +
            $"                        JOIN {Q(seats)} s ON s.{C(seats, "Id")} = g.{C(grants, "SeatId")}\n" +
            $"                        JOIN {Q(roles)} ro ON ro.{C(roles, "Id")} = g.{C(grants, "RoleId")}\n" +
            $"                        WHERE g.{C(grants, "SeatId")} = r.{C(rights, "SeatId")} AND g.{C(grants, "UnitId")} = r.{C(rights, "UnitId")} AND g.{C(grants, "RoleId")} = r.{C(rights, "RoleId")}\n" +
            $"                          AND g.{C(grants, "StartsAt")} = r.{C(rights, "StartsAt")} AND g.{C(grants, "EndsAt")} IS NOT DISTINCT FROM r.{C(rights, "EndsAt")}\n" +
            $"                          AND s.{C(seats, "TenantId")} = r.{C(rights, "TenantId")} AND s.{C(seats, "Status")} = {TenancyTables.Stored(seats, "Status", SeatStatus.Active)}\n" +
            $"                          AND ro.{C(roles, "TenantId")} = r.{C(rights, "TenantId")} AND ro.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)}\n" +
            $"                          AND {KeyAmong(roles, "ro", "r." + C(rights, "Key"))})";

        var key = rights.FindPrimaryKey()!.Properties.Select(property => $"r.{C(rights, property.Name)} = NEW.{C(rights, property.Name)}");
        string Unbacked(string where) => $"EXISTS (SELECT 1 FROM {Q(rights)} r WHERE {where} AND NOT {backed})";

        var body = new StringBuilder()
            .Append("CREATE OR REPLACE FUNCTION ").Append(function).Append("() RETURNS trigger\n")
            .Append("    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n")
            .Append("DECLARE\n")
            .Append("    unbacked boolean;\n")
            .Append("BEGIN\n")
            .Append("    -- The rights rows the change reaches, as they are at commit. Each table's firing reads them in a\n")
            .Append("    -- statement of its own, which names that table's columns alone.\n")
            .Append("    CASE TG_ARGV[0]\n")
            .Append("        WHEN 'rights' THEN\n")
            .Append("            unbacked := ").Append(Unbacked(string.Join(" AND ", key))).Append(";\n")
            .Append("        WHEN 'grants' THEN\n")
            .Append("            unbacked := ").Append(Unbacked($"r.{C(rights, "SeatId")} = OLD.{C(grants, "SeatId")} AND r.{C(rights, "UnitId")} = OLD.{C(grants, "UnitId")} AND r.{C(rights, "RoleId")} = OLD.{C(grants, "RoleId")}")).Append(";\n")
            .Append("        WHEN 'roles' THEN\n")
            .Append("            unbacked := ").Append(Unbacked($"r.{C(rights, "RoleId")} = OLD.{C(roles, "Id")}")).Append(";\n")
            .Append("    END CASE;\n")
            .Append("    IF unbacked THEN\n")
            .Append("        RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = ").Append(RowAccessModel.Literal(RightsBackedByGrantsConstraint))
            .Append(", MESSAGE = 'Every right a seat holds comes from a grant of an active role that holds its key.';\n")
            .Append("    END IF;\n")
            .Append("    RETURN NULL;\n")
            .Append("END\n")
            .Append("$body$");

        yield return body.ToString();
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";

        (IEntityType Table, string Events, string When, string Kind)[] firing =
        [
            (rights, "INSERT OR UPDATE", "", "rights"),
            (grants, "UPDATE OR DELETE", "", "grants"),
            (roles, "UPDATE", $"OLD.{C(roles, "Status")} <> NEW.{C(roles, "Status")} OR {KeysChanged(roles)} OR OLD.{C(roles, "TenantId")} <> NEW.{C(roles, "TenantId")}", "roles"),
        ];

        foreach (var (table, events, when, kind) in firing)
        {
            foreach (var statement in AtCommit(RightsBackedByGrantsConstraint, table, events, when, function, kind))
            {
                yield return statement;
            }
        }
    }

    private static IEnumerable<string> TreeTriggers(TenancyTables tenancy, string schema)
    {
        var units = tenancy.Units;
        var paths = tenancy.UnitPaths;
        var function = $"{schema}.{PathsFollowTheTree}";
        var unitType = TenancyTables.ColumnType(units, "Id");

        var body = new StringBuilder()
            .Append("CREATE OR REPLACE FUNCTION ").Append(function).Append("() RETURNS trigger\n")
            .Append("    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n")
            .Append("DECLARE\n")
            .Append("    touched ").Append(TenancyTables.Cast(unitType)).Append("[];\n")
            .Append("    below boolean := false;\n")
            .Append("    checked ").Append(TenancyTables.Cast(unitType)).Append(";\n")
            .Append("BEGIN\n")
            .Append("    -- The units whose paths the change reaches, and for a unit that moved, every unit below it too.\n")
            .Append("    IF TG_ARGV[0] = 'paths' AND TG_OP = 'INSERT' THEN\n")
            .Append("        touched := ARRAY[NEW.").Append(C(paths, "DescendantId")).Append("];\n")
            .Append("    ELSIF TG_ARGV[0] = 'paths' AND TG_OP = 'DELETE' THEN\n")
            .Append("        touched := ARRAY[OLD.").Append(C(paths, "DescendantId")).Append("];\n")
            .Append("    ELSIF TG_ARGV[0] = 'paths' THEN\n")
            .Append("        touched := ARRAY[NEW.").Append(C(paths, "DescendantId")).Append(", OLD.").Append(C(paths, "DescendantId")).Append("];\n")
            .Append("    ELSE\n")
            .Append("        touched := ARRAY[NEW.").Append(C(units, "Id")).Append("];\n")
            .Append("        below := TG_OP = 'UPDATE';\n")
            .Append("    END IF;\n")
            .Append("    FOR checked IN\n")
            .Append("        WITH RECURSIVE reached(id, depth) AS (\n")
            .Append("            SELECT pg_catalog.unnest(touched), 0\n")
            .Append("            UNION ALL\n")
            .Append("            SELECT u.").Append(C(units, "Id")).Append(", reached.depth + 1 FROM ").Append(Q(units)).Append(" u JOIN reached ON u.").Append(C(units, "ParentId")).Append(" = reached.id\n")
            .Append("            WHERE below AND reached.depth < ").Append(TreeDepth).Append(")\n")
            .Append("        SELECT DISTINCT id FROM reached\n")
            .Append("    LOOP\n")
            .Append("        IF EXISTS (\n")
            .Append("            WITH RECURSIVE up(id, parent, tenant, distance) AS (\n")
            .Append("                SELECT u.").Append(C(units, "Id")).Append(", u.").Append(C(units, "ParentId")).Append(", u.").Append(C(units, "TenantId")).Append(", 0 FROM ").Append(Q(units)).Append(" u WHERE u.").Append(C(units, "Id")).Append(" = checked\n")
            .Append("                UNION ALL\n")
            .Append("                SELECT p.").Append(C(units, "Id")).Append(", p.").Append(C(units, "ParentId")).Append(", p.").Append(C(units, "TenantId")).Append(", up.distance + 1 FROM ").Append(Q(units)).Append(" p\n")
            .Append("                JOIN up ON p.").Append(C(units, "Id")).Append(" = up.parent AND p.").Append(C(units, "TenantId")).Append(" = up.tenant\n")
            .Append("                WHERE up.distance < ").Append(TreeDepth).Append("),\n")
            .Append("            expected AS (SELECT tenant, id AS ancestor, distance FROM up),\n")
            .Append("            actual AS (SELECT p.").Append(C(paths, "TenantId")).Append(" AS tenant, p.").Append(C(paths, "AncestorId")).Append(" AS ancestor, p.").Append(C(paths, "Distance")).Append(" AS distance\n")
            .Append("                       FROM ").Append(Q(paths)).Append(" p WHERE p.").Append(C(paths, "DescendantId")).Append(" = checked)\n")
            .Append("            (SELECT * FROM expected EXCEPT SELECT * FROM actual)\n")
            .Append("            UNION ALL\n")
            .Append("            (SELECT * FROM actual EXCEPT SELECT * FROM expected)) THEN\n")
            .Append("            RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = ").Append(RowAccessModel.Literal(PathsFollowTheTreeConstraint))
            .Append(", MESSAGE = 'The paths of a unit are the units above it in the tree, and the unit itself.';\n")
            .Append("        END IF;\n")
            .Append("    END LOOP;\n")
            .Append("    RETURN NULL;\n")
            .Append("END\n")
            .Append("$body$");

        yield return body.ToString();
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";

        (IEntityType Table, string Events, string When, string Kind)[] firing =
        [
            (paths, "INSERT OR UPDATE OR DELETE", "", "paths"),
            (units, $"INSERT OR UPDATE OF {C(units, "ParentId")}", "", "units"),
        ];

        foreach (var (table, events, when, kind) in firing)
        {
            foreach (var statement in AtCommit(PathsFollowTheTreeConstraint, table, events, when, function, kind))
            {
                yield return statement;
            }
        }
    }

    /// <summary>
    /// The rows that keep what they are about as they change, whoever writes: a placement its seat, unit and tenant,
    /// a grant its seat, unit, role and the seat that gave it, and a role the pack it was made from, which a sync of
    /// the packs makes it follow. Entity Framework never changes these; a query that did would move a row past the
    /// checks its policy makes of a new one. What a seat is, its id, identity and tenant, Tenancy's system work in a
    /// tenant may change, so its trigger is an access guard of its own (<see cref="SeatIdentityTriggers"/>).
    /// </summary>
    private static IEnumerable<string> IdentityTriggers(TenancyTables tenancy, string schema)
    {
        (IEntityType Table, string Function, string Constraint, string[] Properties, string Message)[] fixedRows =
        [
            (tenancy.Placements, PlacementIsFixed, PlacementIsFixedConstraint, ["SeatId", "UnitId", "TenantId"], "A placement keeps its seat, its unit and its tenant."),
            (tenancy.Grants, GrantIsFixed, GrantIsFixedConstraint, ["SeatId", "UnitId", "RoleId", "GrantedBy"], "A grant keeps its seat, its unit, its role and the seat that gave it."),
            (tenancy.Roles, RolePackIsFixed, RolePackIsFixedConstraint, ["FromPack"], "A role keeps the pack it was made from."),
        ];

        foreach (var (table, name, constraint, properties, message) in fixedRows)
        {
            var function = $"{schema}.{name}";
            var columns = properties.Select(property => C(table, property)).ToList();
            yield return
                $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
                "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
                "BEGIN\n" +
                $"    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = {RowAccessModel.Literal(constraint)}, MESSAGE = {RowAccessModel.Literal(message)};\n" +
                "END\n" +
                "$body$";
            yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";
            yield return $"DROP TRIGGER IF EXISTS {constraint} ON {Q(table)}";
            yield return
                $"CREATE TRIGGER {constraint} BEFORE UPDATE OF {string.Join(", ", columns)} ON {Q(table)}\n" +
                $"    FOR EACH ROW WHEN (({string.Join(", ", columns.Select(column => "OLD." + column))}) IS DISTINCT FROM ({string.Join(", ", columns.Select(column => "NEW." + column))}))\n" +
                $"    EXECUTE FUNCTION {function}()";
        }
    }

    /// <summary>
    /// The trigger on an invitation, where the model maps invitations: what it offers stays what it was issued
    /// with, for every role, the tables' owner and Tenancy's own system work included. Accepting an invitation
    /// trusts the row: the unit and the role it gives, until when, whether system work issued it, which skips the
    /// question whether its issuer still may, and who issued it, whose rights are asked otherwise. So none of
    /// those changes, nor its tenant, nor when it was issued and when it ends. An invitation that is over stays
    /// over: its state does not change again, and neither does the seat it made. And while it is open, the address
    /// it is for stays the same; it is forgotten only as the invitation ends.
    /// <para>
    /// Entity Framework changes none of these: the model refuses a save that would. The trigger refuses a
    /// statement that goes around the model.
    /// </para>
    /// </summary>
    private static IEnumerable<string> InvitationTriggers(TenancyTables tenancy, string schema)
    {
        if (tenancy.Invitations is not { } invitations)
        {
            yield break;
        }

        var function = $"{schema}.{InvitationTermsAreFixed}";
        string[] offered = ["TenantId", "UnitId", "RoleId", "GrantUntil", "IssuedAt", "ExpiresAt", "IssuedBy", "IssuedAsSystem"];
        var columns = offered.Select(property => C(invitations, property)).ToList();
        var (state, address, acceptedAs) = (C(invitations, "State"), C(invitations, "Address"), C(invitations, "AcceptedAs"));
        var open = TenancyTables.Stored(invitations, "State", InvitationState.Open);

        yield return
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
            "BEGIN\n" +
            $"    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = {RowAccessModel.Literal(InvitationTermsAreFixedConstraint)}, " +
            "MESSAGE = 'What an invitation offers does not change, and an invitation that is over stays over.';\n" +
            "END\n" +
            "$body$";
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";
        yield return $"DROP TRIGGER IF EXISTS {InvitationTermsAreFixedConstraint} ON {Q(invitations)}";
        yield return
            $"CREATE TRIGGER {InvitationTermsAreFixedConstraint} BEFORE UPDATE ON {Q(invitations)}\n" +
            $"    FOR EACH ROW WHEN (({string.Join(", ", columns.Select(column => "OLD." + column))}) IS DISTINCT FROM ({string.Join(", ", columns.Select(column => "NEW." + column))})\n" +
            $"        OR (OLD.{state} <> {open} AND NEW.{state} IS DISTINCT FROM OLD.{state})\n" +
            $"        OR (OLD.{acceptedAs} IS NOT NULL AND NEW.{acceptedAs} IS DISTINCT FROM OLD.{acceptedAs})\n" +
            $"        OR (NEW.{state} = {open} AND NEW.{address} IS DISTINCT FROM OLD.{address}))\n" +
            $"    EXECUTE FUNCTION {function}()";
    }

    /// <summary>
    /// The trigger on what a seat is: its id, the verified identity it belongs to, and its tenant, which none of
    /// Tenancy's use cases changes once the seat is made. The identity links the seat to a person's account: a seat
    /// that wrote another one into it would hand the seat, and every key it holds, to that account. A seat moved to
    /// another tenant would leave its placements, grants and rights in the one it came from, and a seat given another
    /// id would leave every row that names it, Tenancy's and the modules', naming none. The policy on the seats lets a
    /// seat change its own row, and a seats or a grants manager anywhere in the tenant change any seat's row, since
    /// every save of a seat writes its version; a policy cannot say which columns.
    /// <para>
    /// No use case of Tenancy changes them once the seat is made: provisioning, adding a seat, accepting an invitation
    /// and an import write all three as they make it, which is an insert and none of this trigger's business. So every
    /// role but the scoped system role is refused: every seat, whatever it manages, background work as the
    /// application itself, and the tables' owner, so a migration or the SQL editor too. Tenancy's system work in a
    /// tenant passes, which <c>TenancyWork.BeginSystemIn</c> begins, so that a one-off of the application's own can
    /// link a seat to the identity another sign-in provider gives the same person. The policies keep that work to
    /// Tenancy's scope and to its tenant, so it moves no seat to another tenant either.
    /// </para>
    /// <para>
    /// It refuses as a policy does, with <c>42501</c> and the toolkit's hint, since it holds who may: a save that
    /// changed one of them behind the aggregate's back is refused with <c>access.refused</c>. It runs as its caller,
    /// so it asks the role the statement runs as.
    /// </para>
    /// </summary>
    private static IEnumerable<string> SeatIdentityTriggers(TenancyTables tenancy, string schema, Written written)
    {
        var seats = tenancy.Seats;
        var function = $"{schema}.{SeatIdentityIsFixed}";
        var columns = new[] { "Id", "TenantId", "Identity" }.Select(property => C(seats, property)).ToList();

        yield return
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
            "BEGIN\n" +
            "    -- Tenancy's system work in a tenant may; the policies keep it to Tenancy's scope and to that tenant.\n" +
            $"    IF CURRENT_USER IS DISTINCT FROM {RowAccessModel.Literal(written.SystemInRole)} THEN\n" +
            $"        {RowAccessModel.Refusal(SeatIdentityIsFixedConstraint, SeatIdentityRefusal)}\n" +
            "    END IF;\n" +
            "    RETURN NEW;\n" +
            "END\n" +
            "$body$";
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";
        yield return $"DROP TRIGGER IF EXISTS {SeatIdentityIsFixedConstraint} ON {Q(seats)}";
        yield return
            $"CREATE TRIGGER {SeatIdentityIsFixedConstraint} BEFORE UPDATE OF {string.Join(", ", columns)} ON {Q(seats)}\n" +
            $"    FOR EACH ROW WHEN (({string.Join(", ", columns.Select(column => "OLD." + column))}) IS DISTINCT FROM ({string.Join(", ", columns.Select(column => "NEW." + column))}))\n" +
            $"    EXECUTE FUNCTION {function}()";
    }

    /// <summary>
    /// The trigger on a seat's status. The status decides whether the seat's grants count, and the trigger that
    /// writes the rights follows it: a seat made active again gets every right of its grants back, and one
    /// suspended or deactivated loses them. The policy on the seats lets a seats or a grants manager anywhere, and
    /// the seat itself, change the row, since every save of a seat writes its version. So a change of status by a
    /// seat is held here to what giving and taking away those grants one by one is held to: the calling seat holds
    /// <see cref="TenancyKeys.SeatsManage"/> for the whole tenant, as the use cases ask, and for every grant of
    /// the seat that has not ended, of an active role, each key of that role that manages access at the grant's
    /// unit. Its own grants that apply now are its own hold, so a seat that manages seats suspends itself.
    /// <para>
    /// Only a seat is held to it. System work, which the policies keep to Tenancy's own scope, and the tables'
    /// owner are no seat: the function that finds the calling seat answers them nothing. How long the caller
    /// holds each key against the end of each grant stays the use case's to check.
    /// </para>
    /// <para>
    /// It refuses as a policy does, with <c>42501</c> and the toolkit's hint (<see cref="RowAccessModel.Refusal"/>),
    /// since it is an access guard, holding who may and not what may be: a use case whose seat lost a key between
    /// its check and its save is refused with <c>access.refused</c>. The triggers that hold what may never be, a
    /// tenant's last administrator, the rights, the paths and the rows that stay fixed, are no access guards and
    /// refuse with <c>check_violation</c>, whoever writes.
    /// </para>
    /// </summary>
    private static IEnumerable<string> StatusTriggers(TenancyTables tenancy, string schema)
    {
        var seats = tenancy.Seats;
        var grants = tenancy.Grants;
        var roles = tenancy.Roles;
        var function = $"{schema}.{SeatStatusIsManaged}";
        var status = C(seats, "Status");

        // A grant of the seat that has not ended, of an active role, with a key that manages access the calling
        // seat does not hold at the grant's unit.
        var beyond =
            $"SELECT 1 FROM {Q(grants)} g\n" +
            $"                   JOIN {Q(roles)} r ON r.{C(roles, "Id")} = g.{C(grants, "RoleId")} AND r.{C(roles, "Status")} = {TenancyTables.Stored(roles, "Status", RoleStatus.Active)}\n" +
            $"                   CROSS JOIN LATERAL {KeysOf(roles, "r", "managed")}\n" +
            $"                   WHERE g.{C(grants, "SeatId")} = NEW.{C(seats, "Id")}\n" +
            $"                     AND (g.{C(grants, "EndsAt")} IS NULL OR g.{C(grants, "EndsAt")} > pg_catalog.now())\n" +
            $"                     AND {Fn(ManagesAccess)}(managed.k)\n" +
            $"                     AND NOT (g.{C(grants, "UnitId")} = ANY (ARRAY(SELECT {Fn(UnitsWhereIHold)}(managed.k))))";

        yield return
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n" +
            "BEGIN\n" +
            "    -- System work and the tables' owner are no seat, and are not held to this.\n" +
            $"    IF (SELECT {Fn(CallerSeat)}()) IS NULL THEN\n" +
            "        RETURN NEW;\n" +
            "    END IF;\n" +
            $"    IF NOT (SELECT {Fn(HoldsTenantWide)}({RowAccessModel.Literal(SeatsKey)}))\n" +
            $"       OR EXISTS ({beyond}) THEN\n" +
            $"        {RowAccessModel.Refusal(SeatStatusIsManagedConstraint, SeatStatusRefusal)}\n" +
            "    END IF;\n" +
            "    RETURN NEW;\n" +
            "END\n" +
            "$body$";
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";
        yield return $"DROP TRIGGER IF EXISTS {SeatStatusIsManagedConstraint} ON {Q(seats)}";
        yield return
            $"CREATE TRIGGER {SeatStatusIsManagedConstraint} BEFORE UPDATE OF {status} ON {Q(seats)}\n" +
            $"    FOR EACH ROW WHEN (OLD.{status} IS DISTINCT FROM NEW.{status})\n" +
            $"    EXECUTE FUNCTION {function}()";
    }

    /// <summary>
    /// The trigger on what a role's pack gave it, which a sync of the packs compares the pack with to tell a key the
    /// pack gained or lost from one the tenant added or took out. The policy on the roles lets a seat that manages
    /// roles for the whole tenant change the row, and its keys with it; what the pack gave the role it does not
    /// change: a seat that wrote it could have the next sync add to the role what the pack never gave it, or take out
    /// what the tenant added. It is written when the role is made, as a copy of its pack (the policy on new roles), and
    /// as Tenancy's own system work makes the role follow its pack, which the policies keep to Tenancy's scope.
    /// <para>
    /// Only a seat is held to it. System work and the tables' owner are no seat: the function that finds the calling
    /// seat answers them nothing, so a migration that fills the column for rows written before it was there passes.
    /// It refuses as a policy does, with <c>42501</c> and the toolkit's hint, since it holds who may.
    /// </para>
    /// </summary>
    private static IEnumerable<string> PackTriggers(TenancyTables tenancy, string schema)
    {
        var roles = tenancy.Roles;
        var function = $"{schema}.{RoleFollowsItsPack}";
        var (column, store) = KeysColumn(roles, KeysFromPackProperty);
        var changed = store == KeysStore.Json
            ? $"OLD.{column}::pg_catalog.jsonb IS DISTINCT FROM NEW.{column}::pg_catalog.jsonb"
            : $"OLD.{column} IS DISTINCT FROM NEW.{column}";

        yield return
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$\n" +
            "BEGIN\n" +
            "    -- System work and the tables' owner are no seat, and are not held to this.\n" +
            $"    IF (SELECT {Fn(CallerSeat)}()) IS NOT NULL THEN\n" +
            $"        {RowAccessModel.Refusal(RoleFollowsItsPackConstraint, RoleFollowsItsPackRefusal)}\n" +
            "    END IF;\n" +
            "    RETURN NEW;\n" +
            "END\n" +
            "$body$";
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";
        yield return $"DROP TRIGGER IF EXISTS {RoleFollowsItsPackConstraint} ON {Q(roles)}";
        yield return
            $"CREATE TRIGGER {RoleFollowsItsPackConstraint} BEFORE UPDATE OF {column} ON {Q(roles)}\n" +
            $"    FOR EACH ROW WHEN ({changed})\n" +
            $"    EXECUTE FUNCTION {function}()";
    }

    /// <summary>
    /// For every table of the model whose entity keeps who changed its rows (<c>RecordsWhoChanged</c>): a trigger,
    /// before every insert and update of a row, that holds a caller to itself in those columns, and its function,
    /// made once in the context's schema. Nothing for a model without such a table.
    /// <list type="bullet">
    /// <item>A signed-in user writes the kind of a seat, its own seat, and no operator's identity: as who changed
    /// the row, and on a new row as who wrote it first.</item>
    /// <item>Scoped system work never writes the kind of a seat: it acts for a seat at most, and is recorded as
    /// itself, an operator or a token.</item>
    /// <item>Neither changes who wrote the row first.</item>
    /// </list>
    /// Any other role is not held: the tables' owner fills the columns of the rows that were there before them.
    /// The save writes every one of these columns itself, so the trigger only ever refuses a statement that goes
    /// around the model. It runs as the caller, and reads the row's columns by the names the trigger names, which
    /// are the model's, whatever a naming convention made of them.
    /// </summary>
    private static IEnumerable<string> AttributionTriggers(IModel model, Written written)
    {
        var tables = model.GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is not null && entityType.GetViewName() is null && TenancyModel.RecordsWhoChanged(entityType))
            .GroupBy(Q, StringComparer.Ordinal)
            .OrderBy(table => table.Key, StringComparer.Ordinal)
            .Select(table => table.OrderBy(entityType => entityType.Name, StringComparer.Ordinal).First())
            .ToList();
        if (tables.Count == 0)
        {
            yield break;
        }

        var function = $"{Quoted(model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema)}.{AttributionMatchesCaller}";
        var seatKind = RowAccessModel.Literal(TenancyActorKinds.Seat);

        // The columns, as the trigger's arguments name them: who wrote the row first, then who changed it last,
        // each as seat, kind and identity.
        string New(int column) => $"(written ->> TG_ARGV[{column}])";
        string Old(int column) => $"(as_it_was ->> TG_ARGV[{column}])";
        string NotTheCaller(int seat, int kind, int identity)
            => $"{New(kind)} IS DISTINCT FROM {seatKind} OR {New(seat)} IS DISTINCT FROM caller_seat OR {New(identity)} IS NOT NULL";
        // A caller recorded as somebody else is refused as a policy refuses it, with the toolkit's hint.
        string Refusal(string message) => RowAccessModel.Refusal(AttributionMatchesCallerTrigger, message);

        yield return
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
            "DECLARE\n" +
            "    written pg_catalog.jsonb := pg_catalog.to_jsonb(NEW);\n" +
            "    as_it_was pg_catalog.jsonb;\n" +
            "    caller_seat pg_catalog.text;\n" +
            "BEGIN\n" +
            $"    IF CURRENT_USER = {RowAccessModel.Literal(written.UserRole)} THEN\n" +
            $"        caller_seat := (SELECT {Fn(CallerSeat)}())::pg_catalog.text;\n" +
            $"        IF caller_seat IS NULL OR {NotTheCaller(3, 4, 5)}\n" +
            $"           OR (TG_OP = 'INSERT' AND ({NotTheCaller(0, 1, 2)})) THEN\n" +
            $"            {Refusal(AttributionOfASeat)}\n" +
            "        END IF;\n" +
            $"    ELSIF CURRENT_USER = {RowAccessModel.Literal(written.SystemInRole)} THEN\n" +
            $"        IF {New(4)} IS NOT DISTINCT FROM {seatKind} OR (TG_OP = 'INSERT' AND {New(1)} IS NOT DISTINCT FROM {seatKind}) THEN\n" +
            $"            {Refusal(AttributionOfSystemWork)}\n" +
            "        END IF;\n" +
            "    ELSE\n" +
            "        RETURN NEW;\n" +
            "    END IF;\n" +
            "    IF TG_OP = 'UPDATE' THEN\n" +
            "        as_it_was := pg_catalog.to_jsonb(OLD);\n" +
            $"        IF ({New(0)}, {New(1)}, {New(2)}) IS DISTINCT FROM ({Old(0)}, {Old(1)}, {Old(2)}) THEN\n" +
            $"            {Refusal(AttributionIsKept)}\n" +
            "        END IF;\n" +
            "    END IF;\n" +
            "    RETURN NEW;\n" +
            "END\n" +
            "$body$";
        yield return $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC";

        foreach (var table in tables)
        {
            var columns = string.Join(", ", TenancyAttribution.Columns.Select(column => RowAccessModel.Literal(TenancyTables.ColumnName(table, column))));
            yield return $"DROP TRIGGER IF EXISTS {AttributionMatchesCallerTrigger} ON {Q(table)}";
            yield return
                $"CREATE TRIGGER {AttributionMatchesCallerTrigger} BEFORE INSERT OR UPDATE ON {Q(table)}\n" +
                $"    FOR EACH ROW EXECUTE FUNCTION {function}({columns})";
        }
    }

    /// <summary>A trigger checked at commit, per row, made again whenever the statements run.</summary>
    private static IEnumerable<string> AtCommit(string name, IEntityType table, string events, string when, string function, string kind)
    {
        yield return $"DROP TRIGGER IF EXISTS {name} ON {Q(table)}";
        yield return
            $"CREATE CONSTRAINT TRIGGER {name} AFTER {events} ON {Q(table)}\n" +
            "    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW" + (when.Length == 0 ? "" : $" WHEN ({when})") + "\n" +
            $"    EXECUTE FUNCTION {function}({RowAccessModel.Literal(kind)})";
    }

    /// <summary>A schema as SQL names it, in double quotes.</summary>
    private static string Quoted(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
