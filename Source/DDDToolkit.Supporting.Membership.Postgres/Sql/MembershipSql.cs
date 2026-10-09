using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// The SQL of one resource's membership: its four set functions, written from the resource's rules and from
/// the names the application's model gives the resource's tables and columns, and, where the rules name the
/// keys that change the members and the owner, the lock that holds a caller to them. The same questions the
/// access questions answer in C#, for the caller of the connection.
/// <para>
/// What the rules have the application answer, who the caller is as a member, which roles give a key, where
/// the caller holds a key above, is asked of the functions the rules name, by their logical names. Those are
/// strings: nothing here refers to whatever defines them, and the script that these functions are written
/// into refuses a name nothing defines.
/// </para>
/// <para>
/// Roles kept for the resource are read where they are: the table of the application's role class, in the
/// same context, joined to the roles a member holds. No function is asked for them, the application's or
/// anybody's.
/// </para>
/// </summary>
internal static class MembershipSql
{
    /// <summary>
    /// The form of the SQL this class writes: a number that goes up with every change to what is written for
    /// the same rules, a function's body, a policy or a trigger. It is part of the fingerprint, so a database
    /// that still holds what an earlier version of the package wrote is found by the start-up check, although
    /// its rules have not changed. Change the SQL, and change this: a test keeps the two together.
    /// </summary>
    internal const string SqlForm = "5";

    private const string Instant = "timestamp with time zone";

    /// <summary>
    /// What says, in the body of each function, which rules it was written from, and in which form: the first
    /// line, a comment. A start-up check looks for it, so a database whose functions were written from other
    /// rules than the application runs with, or by another version of the package, is found before it answers
    /// anybody.
    /// </summary>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="systemInRole">The database role scoped system work runs as, which rules that name scopes are written with.</param>
    public static string Marker(MembershipRules rules, string systemInRole)
        => "-- Membership of " + rules.Name + ", in form " + SqlForm + ", written from the rules " + Fingerprint(rules, systemInRole);

    /// <summary>
    /// A fingerprint of everything of <paramref name="rules"/> the functions are written from: where the
    /// caller's member id comes from, the key that being a member gives, the keys of the resource, which its
    /// owner holds, each role with the keys it gives a member, the functions' names, and who may ask them;
    /// and, for rules that say so, the function that answers who the caller is, the function that answers the
    /// roles kept elsewhere with the keys a member's role can give, the function that answers where a key
    /// is held above, and that the roles are kept for the resource, with the keys a member's role can give and
    /// the starter role the owner's role is made from, which the trigger that keeps that role in use asks for.
    /// Under those last rules the roles they declare are starter roles, which no function is written from,
    /// so they are no further part of it: a starter role more changes no function. The keys that change the members
    /// and the owner, which the lock is written from, and the scopes whose work is the application's own, with
    /// the database role that work runs as, are part of it where the rules name them; and so is the form of
    /// the SQL itself (<see cref="SqlForm"/>).
    /// </summary>
    public static string Fingerprint(MembershipRules rules, string systemInRole)
    {
        var said = new StringBuilder();
        void Say(string part) => said.Append(part.Length).Append(':').Append(part).Append(';');

        // What only some rules say goes under a mark of its own. A part starts with its length, so a mark is
        // never taken for one, and rules that say none of it are fingerprinted as they always were.
        void Mark(string what) => said.Append('#').Append(what).Append('#');

        // What cuts a role that is a row: which keys a member's role gives.
        void SayMemberKeys()
        {
            Say(rules.MemberKeys.Excepts ? "all-but" : "only");
            foreach (var key in rules.MemberKeys.Keys)
            {
                Say(key);
            }
        }

        Mark("form");
        Say(SqlForm);
        Say(rules.Members.Kind.ToString());
        Say(rules.Members.ClaimPath ?? string.Empty);
        Say(rules.SeeKey ?? string.Empty);
        foreach (var key in rules.Keys)
        {
            Say(key);
        }

        Say(string.Empty);
        if (!rules.RolesKept)
        {
            foreach (var role in rules.Roles)
            {
                Say(role.Name);
                foreach (var key in rules.KeysOf(new NamedRole(role.Name)))
                {
                    Say(key);
                }

                Say(string.Empty);
            }
        }

        foreach (var name in new[] { rules.Functions.AsMember, rules.Functions.AsMemberWith, rules.Functions.Seen, rules.Functions.HeldOn })
        {
            Say(name);
        }

        foreach (var role in rules.GrantTo)
        {
            Say(role);
        }

        if (rules.Members.Kind == MemberSourceKind.Resolved)
        {
            Mark("member");
            Say(rules.Members.Function ?? string.Empty);
        }

        if (rules.RolesKeptElsewhere is { } elsewhere)
        {
            // The roles are not in the rules, so what cuts them is said itself: which keys a member's role gives.
            Mark("roles");
            Say(elsewhere.Function ?? string.Empty);
            SayMemberKeys();
        }

        if (rules.RolesKept)
        {
            // The roles are rows, read where they are kept: the same cut, and no function to name. And the row
            // of the owner's role, told by the starter role it was made from, is kept in use.
            Mark("kept");
            SayMemberKeys();
            Mark("owner-role");
            Say(rules.OwnerRole);
        }

        if (rules.Above is { } above)
        {
            Mark("above");
            Say(above.Function ?? string.Empty);
        }

        if (rules.ChangeMembersKey is { } membersKey)
        {
            Mark("members-key");
            Say(membersKey);
        }

        if (rules.ChangeOwnerKey is { } ownerKey)
        {
            Mark("owner-key");
            Say(ownerKey);
        }

        if (rules.SystemScopes.Count > 0)
        {
            // The scopes are compared with the claims of the role scoped work runs as, by its name.
            Mark("scopes");
            Say(systemInRole);
            foreach (var scope in rules.SystemScopes)
            {
                Say(scope);
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(said.ToString())));
    }

    /// <summary>The four functions of the resource <paramref name="mapping"/> maps, under the names <paramref name="rules"/> give them.</summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <param name="systemInRole">The database role scoped system work runs as.</param>
    /// <exception cref="InvalidOperationException">The rules, or the way the model stores the members, are not ones these functions can be written for.</exception>
    public static IReadOnlyList<ContributedFunction> Functions(MemberMapping mapping, MembershipRules rules, string owner, string systemInRole)
    {
        var resource = mapping.Resource;
        var members = mapping.Members;
        var held = mapping.Roles;
        var what = resource.DisplayName();

        var role = held.FindProperty(nameof(MemberRole<,>.RoleId))!;
        if (!rules.RolesKept && rules.RolesKeptElsewhere is null && role.ClrType != typeof(NamedRole))
        {
            throw new InvalidOperationException(
                "The members of " + what + " hold roles known by " + role.ClrType.Name + ", and the rules '" + rules.Name + "' declare the roles, which a member holds by name. "
                + "Declare the member class with NamedRole as its role, or say in the rules where the roles are rows: kept for the resource, in its role class, or kept elsewhere, "
                + "with the function that answers which of them give a key.");
        }

        // The member's row carries its resource, and a role's row its member: the columns are the model's.
        var toResource = members.FindOwnership()!;
        var toMember = held.FindOwnership()!;
        var id = resource.FindProperty("Id")
            ?? throw new InvalidOperationException(what + " has no Id property, so its functions have nothing to answer with.");
        var position = IndexOf(toResource.PrincipalKey.Properties, id);
        var resourceOfMember = "m." + Column(members, toResource.Properties[position].Name);
        var memberOfRole = string.Join(
            " AND ",
            toMember.Properties.Select((property, index) => "h." + Column(held, property.Name) + " = m." + Column(members, toMember.PrincipalKey.Properties[index].Name)));

        var caller = Caller(rules, mapping);
        var memberNow = "m." + Column(members, nameof(MemberEntity<,,>.MemberId)) + " = " + caller + " AND " + Now(members, "m");
        var marker = Marker(rules, systemInRole);
        var returns = "SETOF " + RowAccessModel.ColumnType(resource, id.Name);
        string Own(string name) => Asked(owner + "/" + name);

        // Where the caller holds a key above, for a resource that is reached from above: the resources that
        // sit there. Asked with the function's own argument, the application's function is not asked at all
        // when there is no key: a condition without a column gates the whole part.
        var above = Above(rules, mapping);
        string ReachedFromAbove(string key)
            => $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE {(key == "$1" ? "$1 IS NOT NULL AND " : string.Empty)}"
               + $"r.{above!.Value.At} IN (SELECT reached.place FROM {above.Value.Function}({key}) AS reached(place))";

        // Every resource, for the application's own work in a scope the rules name: the caller's claims say the
        // role scoped work runs as, and one of those scopes. Nobody else's token says that role.
        var ownWork = rules.SystemScopes.Count == 0
            ? null
            : $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE {{caller:role}} = {Text(systemInRole)} AND {{caller:claim:scope}} IN ({string.Join(", ", rules.SystemScopes.Select(Text))})";

        // Every body is put together line by line, with the line ending said here: a function is the same text
        // whichever machine wrote the access file, and whatever line endings this file was checked out with.
        var asMember = Lines(
            marker,
            $"SELECT DISTINCT {resourceOfMember} FROM {Table(members)} m",
            $"WHERE {memberNow}");

        // A role counts only inside its membership: both periods apply now. Without a key nothing is asked,
        // of the member rows or of a function of the application's: the first condition has no column, and
        // gates the rest.
        var asMemberWith = Lines(
        [
            marker,
            $"SELECT DISTINCT {resourceOfMember} FROM {Table(members)} m",
            $"WHERE $1 IS NOT NULL AND {memberNow}",
            .. RoleThatGives(rules, mapping, role, memberOfRole),
        ]);

        // Seen by its members; by its owner, by the owner column, who holds every key of it by owning it; and,
        // where the rules name a key for seeing and let a resource be reached from above, by whoever holds that
        // key where the resource sits. The owner by the column, and not by its member row alone: a resource is
        // written before its owner's row, in the same statement or transaction, and a rule that reads the
        // resource through this function would otherwise keep the owner from writing that row.
        List<string> seen =
        [
            marker,
            $"SELECT joined.id FROM {Own(rules.Functions.AsMember)}() AS joined(id)",
            "UNION",
            $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE r.{Column(resource, mapping.Owner.Name)} = {caller}",
        ];
        if (above is not null && rules.SeeKey is { } seenWith)
        {
            seen.Add("UNION");
            seen.Add(ReachedFromAbove(Text(seenWith)));
        }

        if (ownWork is not null)
        {
            seen.Add("UNION");
            seen.Add(ownWork);
        }

        // Through a role that gives the key; by being a member, for the one key that gives; by owning the
        // resource, for every key of the resource, whatever the owner's roles give; and from above.
        List<string> heldOn =
        [
            marker,
            $"SELECT holding.id FROM {Own(rules.Functions.AsMemberWith)}($1) AS holding(id)",
        ];
        if (rules.SeeKey is { } seeKey)
        {
            heldOn.Add("UNION");
            heldOn.Add($"SELECT joined.id FROM {Own(rules.Functions.AsMember)}() AS joined(id) WHERE $1 = {Text(seeKey)}");
        }

        if (rules.Keys.Count > 0)
        {
            heldOn.Add("UNION");
            heldOn.Add($"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE $1 IN ({string.Join(", ", rules.Keys.Select(Text))}) AND r.{Column(resource, mapping.Owner.Name)} = {caller}");
        }

        if (above is not null)
        {
            heldOn.Add("UNION");
            heldOn.Add(ReachedFromAbove("$1"));
        }

        if (ownWork is not null)
        {
            // The application's own work holds every key there is, on every resource.
            heldOn.Add("UNION");
            heldOn.Add(ownWork + " AND $1 IS NOT NULL");
        }

        // Each runs as its owner, so it reads the member rows without the policies that ask it, and is
        // executable by the roles the rules name and no other. The two that answer access say so, by the type of
        // the resource's id: a rule asks them through a [ResourceAccessContract] of that id, and never by name.
        return
        [
            new ContributedFunction(rules.Functions.AsMember, "", returns, asMember, SecurityDefiner: true, GrantTo: rules.GrantTo),
            new ContributedFunction(rules.Functions.AsMemberWith, "text", returns, asMemberWith, SecurityDefiner: true, GrantTo: rules.GrantTo),
            new ContributedFunction(rules.Functions.Seen, "", returns, Lines([.. seen]), SecurityDefiner: true, GrantTo: rules.GrantTo, Answers: new(id.ClrType, ResourceAccessSet.Seen)),
            new ContributedFunction(rules.Functions.HeldOn, "text", returns, Lines([.. heldOn]), SecurityDefiner: true, GrantTo: rules.GrantTo, Answers: new(id.ClrType, ResourceAccessSet.HeldOn)),
        ];
    }

    /// <summary>What the lock on the owner column is called: the trigger, and the function it runs.</summary>
    /// <exception cref="InvalidOperationException">The name is longer than Postgres keeps one.</exception>
    public static string OwnerLock(MembershipRules rules)
    {
        var name = OwnerLockName(rules);
        return name.Length <= LongestName
            ? name
            : throw new InvalidOperationException(
                "The rules '" + rules.Name + "' name the key that changes the owner, and the lock on the owner column would be called '" + name
                + "', which is longer than the 63 characters Postgres keeps of a name. Give the rules a shorter name.");
    }

    /// <summary>The longest name Postgres keeps whole.</summary>
    private const int LongestName = 63;

    /// <summary>What the body of the function the lock's trigger runs stands between.</summary>
    private const string BodyQuote = "$body$";

    /// <summary>The lock's name as the rules' name gives it, however long: letters, digits and underscores, so it needs no quotes.</summary>
    private static string OwnerLockName(MembershipRules rules) => rules.Name.Replace('.', '_').Replace('-', '_') + "_owner_stays";

    /// <summary>
    /// The lock on the two member tables, for rules that name the key that changes the members
    /// (<see cref="MembershipRules.ChangeMembersKey"/>): for each database role the rules let ask the
    /// functions, a restrictive policy for adding, changing and removing a row of either table, which lets a
    /// row be written only on a resource the caller holds that key on. Restrictive, so it narrows whatever the
    /// application's own rules allow, and allows nothing itself: reading the rows stays what those rules say.
    /// None for rules that name no such key: the tables are then written as the application's rules let a
    /// caller change the resource.
    /// <para>
    /// Where the rules name the key that changes the owner as well, whoever holds that key on a resource
    /// writes its member rows too: naming an owner puts the new owner on the list and gives it the owner's
    /// role, and whoever may name the owner could name itself, so nothing is given that it did not have.
    /// </para>
    /// <para>
    /// Where the roles are kept for the resource, a second restrictive policy on the table of the roles a
    /// member holds, for adding and changing a row: the role it holds is one the caller sees in the role
    /// table (<see cref="RoleSeen"/>). The functions find a role by its id, whoever's it is, so a role of
    /// another customer of the application's, written onto a member list past the application, would give
    /// its keys there. The package knows no customer and no column for one; the application's own rules on
    /// its role table do, and the question is asked as the caller, under them: a role they keep from the
    /// caller is no role it may give.
    /// </para>
    /// </summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <exception cref="InvalidOperationException">The rules say the roles are kept, and the model maps no role class for the resource.</exception>
    public static IReadOnlyList<ContributedPolicy> Policies(MemberMapping mapping, MembershipRules rules, string owner)
    {
        if (rules.ChangeMembersKey is not { } key)
        {
            return [];
        }

        var id = mapping.Resource.FindProperty("Id")
            ?? throw new InvalidOperationException(mapping.Resource.DisplayName() + " has no Id property, so the lock on its members has nothing to ask about.");

        // The column of a member's row that says which resource it is of, and the same for a role's row, which
        // carries its member's key and so the resource with it.
        var toResource = mapping.Members.FindOwnership()!;
        var ofMember = toResource.Properties[IndexOf(toResource.PrincipalKey.Properties, id)];
        var toMember = mapping.Roles.FindOwnership()!;
        var ofRole = toMember.Properties[IndexOf(toMember.PrincipalKey.Properties, ofMember)];

        // Asked once for a statement: the resources the caller holds a key on, as the functions answer it.
        string Held(string held) => "(SELECT held.id FROM " + Asked(owner + "/" + rules.Functions.HeldOn) + "(" + Text(held) + ") AS held(id))";
        var handsOn = rules.ChangeOwnerKey is { } ownerKey && !string.Equals(ownerKey, key, StringComparison.Ordinal) ? ownerKey : null;
        var policies = new List<ContributedPolicy>();
        foreach (var (table, column) in new[] { (mapping.Members, ofMember), (mapping.Roles, ofRole) })
        {
            var resource = Column(table, column.Name);
            var holds = handsOn is null
                ? resource + " IN " + Held(key)
                : "(" + resource + " IN " + Held(key) + " OR " + resource + " IN " + Held(handsOn) + ")";
            foreach (var role in rules.GrantTo)
            {
                policies.Add(new ContributedPolicy(table, MemberLock, "INSERT", role, null, holds, Restrictive: true));
                policies.Add(new ContributedPolicy(table, MemberLock, "UPDATE", role, holds, holds, Restrictive: true));
                policies.Add(new ContributedPolicy(table, MemberLock, "DELETE", role, holds, null, Restrictive: true));
            }
        }

        if (rules.RolesKept)
        {
            // Asked as the caller, so the application's rules on its role table decide which roles are there to
            // see. Removing a row asks nothing of its role, so one that should not be there can be taken away.
            var kept = KeptRoleClass(rules, mapping);
            var held = mapping.Roles;
            var seen = $"EXISTS (SELECT 1 FROM {Table(kept)} k WHERE k.{Column(kept, nameof(KeptRoleAggregate<>.Id))} = "
                       + $"{Table(held)}.{Column(held, nameof(MemberRole<,>.RoleId))})";
            foreach (var role in rules.GrantTo)
            {
                policies.Add(new ContributedPolicy(held, RoleSeen, "INSERT", role, null, seen, Restrictive: true));
                policies.Add(new ContributedPolicy(held, RoleSeen, "UPDATE", role, "true", seen, Restrictive: true));
            }
        }

        return policies;
    }

    /// <summary>What a policy of the lock on the member tables is about, which its name starts with.</summary>
    public const string MemberLock = "Members change with the key";

    /// <summary>
    /// What the policy of the lock that holds a role given to one the caller sees is about, which its name
    /// starts with: written on the table of the roles a member holds, where the roles are kept for the resource
    /// and the rules name the key that changes the members.
    /// </summary>
    public const string RoleSeen = "Members hold roles the caller sees";

    /// <summary>
    /// The two triggers of a resource's membership: the lock on the owner column
    /// (<see cref="OwnerColumnStatements"/>), and, where the roles are kept for the resource, the trigger that
    /// keeps the owner's role in use (<see cref="OwnerRoleStatements"/>).
    /// </summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <param name="schema">The schema the resource's functions are in.</param>
    /// <param name="roles">The database roles the rules' symbolic roles are written as.</param>
    /// <exception cref="InvalidOperationException">
    /// The rules name a role no policy can be for, a trigger's name is too long, what a trigger is written from
    /// has the quote of its function's body in it, or the rules say the roles are kept and the model maps no
    /// role class for the resource.
    /// </exception>
    public static IReadOnlyList<string> Statements(MemberMapping mapping, MembershipRules rules, string owner, string schema, RowAccessRoleNames roles)
    {
        List<string> statements = [.. OwnerColumnStatements(mapping, rules, owner, schema, roles)];
        if (rules.RolesKept)
        {
            statements.AddRange(OwnerRoleStatements(mapping, rules, schema, roles.SystemIn));
        }

        return statements;
    }

    /// <summary>What the trigger that keeps the owner's role in use is called: the trigger, and the function it runs.</summary>
    /// <exception cref="InvalidOperationException">The name is longer than Postgres keeps one.</exception>
    public static string OwnerRoleLock(MembershipRules rules)
    {
        var name = rules.Name.Replace('.', '_').Replace('-', '_') + "_owner_role_stays";
        return name.Length <= LongestName
            ? name
            : throw new InvalidOperationException(
                "The rules '" + rules.Name + "' keep the roles of the resource, and the trigger that keeps the owner's role in use would be called '" + name
                + "', which is longer than the 63 characters Postgres keeps of a name. Give the rules a shorter name.");
    }

    /// <summary>
    /// The trigger that keeps the owner's role in use, for rules that keep the roles for the resource
    /// (<see cref="MembershipRules.RolesKept"/>): on the application's role table, it refuses a row made from
    /// the starter role the rules name as the owner's (<see cref="MembershipRules.OwnerRole"/>) that would be
    /// archived, whether a statement adds the row or changes it, as the role refuses to be archived in C#
    /// (<c>owner-role-stays</c>). Nobody could be made an owner after.
    /// <para>
    /// It holds whoever writes the row, the application's own work and the role that owns the table included:
    /// no command of the application's archives that role either. Who may change a role at all stays the
    /// application's rules on its table to say, and so does who may remove one: the package archives roles and
    /// removes none, and this trigger holds archiving, not a statement that deletes the row. Where the role came
    /// from is held by the model: the package maps it as fixed once the row is there.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> OwnerRoleStatements(MemberMapping mapping, MembershipRules rules, string schema, string systemInRole)
    {
        var kept = KeptRoleClass(rules, mapping);
        var name = OwnerRoleLock(rules);
        var function = FunctionIn(schema, name);
        var madeFrom = Column(kept, nameof(KeptRoleAggregate<>.MadeFrom));
        var status = Column(kept, nameof(KeptRoleAggregate<>.Status));
        var active = Braces(RowAccessModel.Stored(kept, nameof(KeptRoleAggregate<>.Status), KeptRoleStatus.Active));
        var message = "A row of " + RowAccessModel.Table(kept) + " made from the starter role '" + rules.OwnerRole + "' is the role every owner of a "
                      + mapping.Resource.DisplayName() + " holds, and is not archived.";
        var written = Lines(
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger",
            "    LANGUAGE plpgsql SET search_path = '' AS " + BodyQuote,
            "BEGIN",
            "    " + Marker(rules, systemInRole),
            "    -- Whoever writes the row: an owner is named into this role, so it stays in use.",
            $"    IF NEW.{madeFrom} = {Text(rules.OwnerRole)} AND NEW.{status} IS DISTINCT FROM {active} THEN",
            $"        RAISE EXCEPTION USING ERRCODE = 'check_violation', MESSAGE = {Text(message)};",
            "    END IF;",
            "    RETURN NEW;",
            "END",
            BodyQuote);

        // As for the lock on the owner column: a third quote of the body would end it early.
        if (written.Split(BodyQuote).Length != 3)
        {
            throw new InvalidOperationException(
                "The trigger that keeps the owner's role of " + mapping.Resource.DisplayName() + " in use cannot be written from the rules '" + rules.Name + "': the owner's role, '"
                + rules.OwnerRole + "', or a name of the model has '" + BodyQuote + "' in it, which would end the function's body early.");
        }

        return
        [
            written,
            $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC",
            $"DROP TRIGGER IF EXISTS {name} ON {Table(kept)}",
            Lines(
                $"CREATE TRIGGER {name} BEFORE INSERT OR UPDATE OF {madeFrom}, {status} ON {Table(kept)}",
                $"    FOR EACH ROW EXECUTE FUNCTION {function}()"),
        ];
    }

    /// <summary>A function of the schema the resource's functions are in, by a name that needs no quotes, as a statement names it.</summary>
    private static string FunctionIn(string schema, string name) => Braces("\"" + schema.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"") + "." + name;

    /// <summary>
    /// The lock on the owner column, for rules that name the key that changes the owner
    /// (<see cref="MembershipRules.ChangeOwnerKey"/>): a trigger on the resource's table that fires when a
    /// statement would leave a row with another owner than it had, and refuses it, as a policy refuses, unless
    /// the caller held that key on the resource before the change. A trigger, because a policy sees the row a
    /// statement leaves behind and not the one it found: only here are both at hand, and nothing a caller can
    /// ask is added for it. It refuses as the toolkit's access guards do (<see cref="RowAccessModel.Refusal"/>), with
    /// the lock's name as the constraint, so a save it refuses is <c>access.refused</c> and the warning names the lock.
    /// <para>
    /// It holds the database roles the rules let ask the functions, and nobody else: the application's own
    /// work and the role that owns the tables are not asked. For rules that name no such key the statements
    /// take the lock away, so one written from earlier rules does not stay behind.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> OwnerColumnStatements(MemberMapping mapping, MembershipRules rules, string owner, string schema, RowAccessRoleNames roles)
    {
        var resource = mapping.Resource;
        if (rules.ChangeOwnerKey is not { } key)
        {
            // Rules whose lock could never have been written, its name being too long, left nothing to take away.
            var left = OwnerLockName(rules);
            return left.Length > LongestName
                ? []
                :
                [
                    $"DROP TRIGGER IF EXISTS {left} ON {Table(resource)}",
                    $"DROP FUNCTION IF EXISTS {FunctionIn(schema, left)}()",
                ];
        }

        var name = OwnerLock(rules);
        var function = FunctionIn(schema, name);

        var id = resource.FindProperty("Id")
            ?? throw new InvalidOperationException(resource.DisplayName() + " has no Id property, so the lock on its owner has nothing to ask about.");
        var locked = new List<string>();
        foreach (var role in rules.GrantTo)
        {
            string resolved;
            try
            {
                resolved = roles.Resolve(role);
            }
            catch (ArgumentException unresolved)
            {
                throw new InvalidOperationException(
                    "The rules '" + rules.Name + "' let the role '" + role + "' ask the resource's functions, and the lock on the owner column cannot be written for it: " + unresolved.Message,
                    unresolved);
            }

            if (!locked.Contains(resolved, StringComparer.Ordinal))
            {
                locked.Add(resolved);
            }
        }

        var column = Column(resource, mapping.Owner.Name);
        var message = "The owner of a row of " + RowAccessModel.Table(resource) + " is changed by a caller that holds " + key + " on it.";
        var written = Lines(
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger",
            "    LANGUAGE plpgsql SET search_path = '' AS " + BodyQuote,
            "BEGIN",
            "    " + Marker(rules, roles.SystemIn),
            "    -- The callers the rules name are held to this. The application's own work and the tables' owner are not.",
            $"    IF CURRENT_USER IN ({string.Join(", ", locked.Select(Text))})",
            $"       AND NOT EXISTS (SELECT 1 FROM {Asked(owner + "/" + rules.Functions.HeldOn)}({Text(key)}) AS held(id) WHERE held.id = OLD.{Column(resource, id.Name)}) THEN",
            "        " + Braces(RowAccessModel.Refusal(name, message)),
            "    END IF;",
            "    RETURN NEW;",
            "END",
            BodyQuote);

        // The body stands between two of these, so a third, in the key, a role or a name of the model, would
        // end it early: what came after would be read as the statement's own.
        if (written.Split(BodyQuote).Length != 3)
        {
            throw new InvalidOperationException(
                "The lock on the owner column of " + resource.DisplayName() + " cannot be written from the rules '" + rules.Name + "': the key that changes the owner, '" + key
                + "', a role the rules name or a name of the model has '" + BodyQuote + "' in it, which would end the function's body early.");
        }

        return
        [
            written,
            $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC",
            $"DROP TRIGGER IF EXISTS {name} ON {Table(resource)}",
            Lines(
                $"CREATE TRIGGER {name} BEFORE UPDATE OF {column} ON {Table(resource)}",
                $"    FOR EACH ROW WHEN (OLD.{column} IS DISTINCT FROM NEW.{column})",
                $"    EXECUTE FUNCTION {function}()"),
        ];
    }

    /// <summary>
    /// The lines that ask for a role, held now in the membership of the row <c>m</c>, that gives the key
    /// <c>$1</c>: of the roles the rules declare, as the rules say which gives which key; of the roles kept
    /// for the resource, as their rows say it; or of the roles kept elsewhere, as the application's function
    /// answers it. Of rows, only for a key a member's role can give at all.
    /// </summary>
    private static IEnumerable<string> RoleThatGives(MembershipRules rules, MemberMapping mapping, IProperty role, string memberOfRole)
    {
        var held = mapping.Roles;
        var heldRole = "h." + Column(held, role.Name);
        if (rules.RolesKept)
        {
            return KeptRoleThatGives(rules, mapping, heldRole, memberOfRole);
        }

        if (rules.RolesKeptElsewhere is not { } elsewhere)
        {
            // Which role gives which key, as the rules say it: only the keys a member's role can give at all.
            var pairs = rules.Roles
                .SelectMany(declared => rules.KeysOf(new NamedRole(declared.Name)).Select(key => "(" + Braces(RowAccessModel.Stored(held, role.Name, new NamedRole(declared.Name))) + ", " + Text(key) + ")"))
                .ToList();
            var gives = pairs.Count > 0
                ? "(VALUES " + string.Join(", ", pairs) + ") AS gives(role, key)"
                : "(SELECT ''::pg_catalog.text AS role, ''::pg_catalog.text AS key WHERE false) AS gives";

            return
            [
                $"  AND EXISTS (SELECT 1 FROM {Table(held)} h",
                $"              JOIN {gives} ON gives.role = {heldRole}",
                $"              WHERE {memberOfRole} AND {Now(held, "h")} AND gives.key = $1)",
            ];
        }

        var function = elsewhere.Function
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' say the roles are kept elsewhere, and name no function that answers which of them give a key in the database. Name it by its "
                + "logical name, rolesKeptElsewhere: new(\"owner/name\"): a function that takes the key as text and answers the ids of the roles that give it.");

        // Whatever a role holds where it is kept, it gives a member only a key the rules let a member's role
        // give: said here as the rules say it, in front of what the application's function answers.
        List<string> lines = [.. WithinMemberKeys(rules)];
        lines.Add($"  AND EXISTS (SELECT 1 FROM {Table(held)} h");
        lines.Add($"              WHERE {memberOfRole} AND {Now(held, "h")}");
        lines.Add($"                AND {heldRole} IN (SELECT giving.role FROM {Asked(function)}($1) AS giving(role)))");
        return lines;
    }

    /// <summary>
    /// The lines that ask for a role kept for the resource, held now in the membership of the row <c>m</c>,
    /// that gives the key <c>$1</c>: a row of the application's role class that is in use and holds the key,
    /// read straight from its table, for a key a member's role can give at all.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model maps no role class for the resource, one known by another id than a member holds, or one whose keys are no array.</exception>
    private static List<string> KeptRoleThatGives(MembershipRules rules, MemberMapping mapping, string heldRole, string memberOfRole)
    {
        var held = mapping.Roles;
        var what = mapping.Resource.DisplayName();
        var kept = KeptRoleClass(rules, mapping);

        var id = kept.FindProperty(nameof(KeptRoleAggregate<>.Id))!;
        var heldBy = held.FindProperty(nameof(MemberRole<,>.RoleId))!;
        if (id.ClrType != heldBy.ClrType)
        {
            throw new InvalidOperationException(
                "The members of " + what + " hold roles known by " + heldBy.ClrType.Name + ", and its role class " + kept.DisplayName() + " is known by " + id.ClrType.Name
                + ". A member holds a role of the resource by that role's id: declare the member class with " + id.ClrType.Name + " as its role.");
        }

        var stored = RowAccessModel.ColumnType(kept, nameof(KeptRoleAggregate<>.Keys)).Trim();
        if (!stored.EndsWith("[]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                kept.DisplayName() + "." + nameof(KeptRoleAggregate<>.Keys) + " is stored as " + stored + ", and the functions read a role's keys from an array, such as text[]. "
                + "Leave the column as IsKeptRole maps it.");
        }

        // What a row holds it may hold from before the rules changed: it gives a member only a key the rules
        // let a member's role give now, said here as the rules say it, in front of what the rows say.
        List<string> lines = [.. WithinMemberKeys(rules)];
        lines.Add($"  AND EXISTS (SELECT 1 FROM {Table(held)} h");
        lines.Add($"              JOIN {Table(kept)} k ON k.{Column(kept, id.Name)} = {heldRole}");
        lines.Add($"              WHERE {memberOfRole} AND {Now(held, "h")}");
        lines.Add(
            $"                AND k.{Column(kept, nameof(KeptRoleAggregate<>.Status))} = "
            + Braces(RowAccessModel.Stored(kept, nameof(KeptRoleAggregate<>.Status), KeptRoleStatus.Active))
            + $" AND $1 = ANY (k.{Column(kept, nameof(KeptRoleAggregate<>.Keys))}))");
        return lines;
    }

    /// <summary>The application's role class of a resource whose roles are kept for it.</summary>
    /// <exception cref="InvalidOperationException">The model maps none for the resource.</exception>
    private static IEntityType KeptRoleClass(MembershipRules rules, MemberMapping mapping)
    {
        var what = mapping.Resource.DisplayName();
        return mapping.RoleClass
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' say the roles of " + what + " are kept, and the model maps no role class for it. Declare one with the role template, "
                + "[KeptRole<TRoleId, " + what + ">], and map it where the context builds its model: modelBuilder.Entity<" + what + "Role>().IsKeptRole().");
    }

    /// <summary>
    /// The line that keeps a role that is a row to the keys a member's role can give, as the rules say it; no
    /// line where the rules let a member's role give every key.
    /// </summary>
    private static IEnumerable<string> WithinMemberKeys(MembershipRules rules)
    {
        var keys = string.Join(", ", rules.MemberKeys.Keys.Select(Text));
        var cut = rules.MemberKeys.Excepts
            ? rules.MemberKeys.Keys.Count == 0 ? null : $"$1 NOT IN ({keys})"
            : rules.MemberKeys.Keys.Count == 0 ? "false" : $"$1 IN ({keys})";

        return cut is null ? [] : [$"  AND {cut}"];
    }

    /// <summary>
    /// For rules that let a resource be reached from above, the column that holds where the resource sits and
    /// the application's function that answers where the caller holds a key; <see langword="null"/> for rules
    /// that do not.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rules name no function, or the model does not say where the resource sits.</exception>
    private static (string At, string Function)? Above(MembershipRules rules, MemberMapping mapping)
    {
        if (rules.Above is not { } above)
        {
            return null;
        }

        var what = mapping.Resource.DisplayName();
        var function = above.Function
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' let " + what + " be reached from above, and name no function that answers where the caller holds a key in the database. Name it by its "
                + "logical name, above: new(\"owner/name\"): a function that takes the key as text and answers the places the caller's hold of it reaches.");
        var at = mapping.At
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' let " + what + " be reached from above, and the model does not say where it sits. Say it where the context maps its members: "
                + "HasMembers(resource => resource.Members, resource => resource.OwnerId, at: resource => resource.PlaceId).");

        return (Column(mapping.Resource, at.Name), Asked(function));
    }

    /// <summary>
    /// Who the caller is as a member, as the script fills it in: the caller's user id, a claim of its token,
    /// or what the application's function answers, compared with the column that holds who a member is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The column does not store what the source answers, or the rules name no function.</exception>
    private static string Caller(MembershipRules rules, MemberMapping mapping)
    {
        var stored = RowAccessModel.ColumnType(mapping.Members, nameof(MemberEntity<,,>.MemberId)).Trim();
        var what = mapping.Resource.DisplayName();

        switch (rules.Members.Kind)
        {
            case MemberSourceKind.CallerId:
                return string.Equals(stored, "uuid", StringComparison.OrdinalIgnoreCase)
                    ? "{caller:uid}"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' take the caller's member id from MemberSource.CallerId, a uuid, and the members of " + what + " are stored as " + stored
                        + ". Known by an id over a Guid, a member is stored as uuid.");

            case MemberSourceKind.Claim:
                return IsText(stored)
                    ? "{caller:claim:" + rules.Members.ClaimPath + "}"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' take the caller's member id from the claim '" + rules.Members.ClaimPath + "', a text, and the members of " + what + " are stored as " + stored
                        + ". Known by an id over a text, a member is stored as text or character varying.");

            default:
                // Asked once for a statement, of the application's own function: whoever it answers nobody for is nobody's member.
                return rules.Members.Function is { } function
                    ? "(SELECT " + Asked(function) + "())"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' have the application resolve who the caller is as a member of " + what + ", and name no function that answers it in the database. "
                        + "Name it by its logical name, MemberSource.Resolved(\"owner/name\"): a function without parameters that answers the caller's member id, as the members are stored, or NULL.");
        }
    }

    /// <summary>A function asked by its logical name, <c>owner/name</c>, as the script fills it in: the name it has where it is defined.</summary>
    private static string Asked(string logical) => "{fn:" + logical + "}";

    private static bool IsText(string storeType)
        => storeType.StartsWith("text", StringComparison.OrdinalIgnoreCase)
           || storeType.StartsWith("character varying", StringComparison.OrdinalIgnoreCase)
           || storeType.StartsWith("varchar", StringComparison.OrdinalIgnoreCase)
           || storeType.StartsWith("citext", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The condition that the period of the row <paramref name="alias"/> applies now: it has started, and has
    /// not ended.
    /// </summary>
    /// <exception cref="InvalidOperationException">A moment of the period is not stored as an instant.</exception>
    private static string Now(IEntityType entity, string alias)
    {
        foreach (var moment in new[] { nameof(MemberEntity<,,>.StartsAt), nameof(MemberEntity<,,>.EndsAt) })
        {
            var stored = RowAccessModel.ColumnType(entity, moment).Trim();
            if (!string.Equals(stored, Instant, StringComparison.OrdinalIgnoreCase) && !string.Equals(stored, "timestamptz", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    entity.DisplayName() + "." + moment + " is stored as " + stored + ", and a period is compared with the database's clock as an instant, " + Instant
                    + ". Leave the column as HasMembers maps it.");
            }
        }

        var starts = alias + "." + Column(entity, nameof(MemberEntity<,,>.StartsAt));
        var ends = alias + "." + Column(entity, nameof(MemberEntity<,,>.EndsAt));
        return starts + " <= pg_catalog.now() AND (" + ends + " IS NULL OR " + ends + " > pg_catalog.now())";
    }

    private static int IndexOf(IReadOnlyList<IProperty> properties, IProperty property)
    {
        for (var index = 0; index < properties.Count; index++)
        {
            if (properties[index] == property)
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            property.DeclaringType.DisplayName() + "." + property.Name + " is not part of the key its members are kept under, so a member's row does not say which resource it is of.");
    }

    /// <summary>The lines of a function's body, each ended by a line feed and nothing else.</summary>
    private static string Lines(params string[] lines) => string.Join('\n', lines);

    /// <summary>A table with its schema, as the body of a function names it.</summary>
    private static string Table(IEntityType entity) => Braces(RowAccessModel.Table(entity));

    /// <summary>A column, quoted.</summary>
    private static string Column(IEntityType entity, string property) => Braces(RowAccessModel.Column(entity, property));

    /// <summary>A permission key as an SQL string literal.</summary>
    private static string Text(string text) => Braces(RowAccessModel.Literal(text));

    /// <summary>
    /// <paramref name="sql"/> with its braces doubled: in the body of a contributed function a brace opens a
    /// place the script fills in, and a name or a key that has one means the brace itself.
    /// </summary>
    private static string Braces(string sql) => sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
}
