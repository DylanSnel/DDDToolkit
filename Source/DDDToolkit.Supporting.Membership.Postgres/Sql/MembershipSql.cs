using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// The SQL of one resource's membership: its four set functions, written from the resource's rules and from
/// the names the application's model gives the resource's tables and columns, and, where the rules name the
/// permissions that change the members and the owner, the lock that holds a caller to them. The same questions the
/// access questions answer in C#, for the caller of the connection.
/// <para>
/// What the rules have the application answer, who the caller is as a member, which roles give a permission, where
/// the caller holds a permission above, is asked of functions by their logical names: those the rules name, or those
/// another package's contribution answers for the type of the member or of the place
/// (<see cref="ICallerFunctions"/>), an organization package's for its seat and its unit. Nothing here refers to whatever defines
/// them, and the script that these functions are written into refuses a name nothing defines.
/// </para>
/// <para>
/// Roles a customer makes are read where they are: the table of the application's role class, in the same
/// context, joined to the roles a member holds. No function is asked for them, the application's or anybody's.
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
    internal const string SqlForm = "7";

    /// <summary>What the line of each function's body that names the roles that may execute it starts with.</summary>
    internal const string ExecutedBy = "-- Executed by: ";

    private const string Instant = "timestamp with time zone";

    /// <summary>
    /// What says, in the body of each function, which rules it was written from, and in which form: the first
    /// line, a comment. A start-up check looks for it, so a database whose functions were written from other
    /// rules than the application runs with, or by another version of the package, is found before it answers
    /// anybody. It names the resource's class beside its name, so the export can tell a resource whose name changed.
    /// </summary>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="systemInRole">The database role scoped system work runs as, which rules that name scopes are written with.</param>
    public static string Marker(MembershipRules rules, MemberMapping mapping, string systemInRole)
        => "-- Membership of " + rules.Name + " (" + rules.Resource.Name + "), in form " + SqlForm + ", written from the rules " + Fingerprint(rules, mapping, systemInRole);

    /// <summary>
    /// A fingerprint of everything of <paramref name="rules"/> the functions are written from: who the caller is as
    /// a member, and, where that follows from the member id, what that id is; the permission every member holds;
    /// the permissions the owner holds; where the roles come from, with each fixed role and the permissions it gives, or
    /// which permissions a role can give and the function of roles kept elsewhere; the functions' names; reach from above,
    /// with its function or the place it follows from, and the permissions it does not reach; the permissions the
    /// lock asks for; and the scopes whose work is the application's own, with the database role that work runs as.
    /// Who may execute the functions is not part of it: that follows from the row access rules of the resource, and
    /// is said in a line of its own (<see cref="ExecutedBy"/>). And so is the form of the SQL itself
    /// (<see cref="SqlForm"/>).
    /// </summary>
    public static string Fingerprint(MembershipRules rules, MemberMapping mapping, string systemInRole)
    {
        var said = new StringBuilder();
        void Say(string part) => said.Append(part.Length).Append(':').Append(part).Append(';');

        // What only some rules say goes under a mark of its own. A part starts with its length, so a mark is
        // never taken for one.
        void Mark(string what) => said.Append('#').Append(what).Append('#');

        void SayRolePermissions()
        {
            Say(rules.RolePermissions.Excepts ? "all-but" : "only");
            foreach (var permission in rules.RolePermissions.Permissions)
            {
                Say(permission);
            }
        }

        Mark("form");
        Say(SqlForm);
        Say(rules.Members.Kind.ToString());
        Say(rules.Members.ClaimPath ?? string.Empty);
        if (rules.Members.Kind == MemberSourceKind.Inferred)
        {
            Say(mapping.Members.FindProperty(nameof(MemberEntity<,,>.MemberId))?.ClrType.FullName ?? string.Empty);
        }

        Say(rules.EveryMemberPermission ?? string.Empty);
        foreach (var permission in rules.OwnerPermissions)
        {
            Say(permission);
        }

        Mark("roles");
        Say(rules.Roles.Kind.ToString());
        if (rules.Roles.Kind == RolesKind.Fixed)
        {
            foreach (var role in rules.Roles.Declared)
            {
                Say(role.Name);
                foreach (var permission in rules.PermissionsOf(new NamedRole(role.Name)))
                {
                    Say(permission);
                }

                Say(string.Empty);
            }
        }
        else
        {
            Say(rules.Roles.Function?.LogicalName ?? string.Empty);
            SayRolePermissions();
        }

        foreach (var name in new[] { rules.Functions.AsMember, rules.Functions.AsMemberWith, rules.Functions.Seen, rules.Functions.HeldOn })
        {
            Say(name);
        }

        if (rules.Members.Kind == MemberSourceKind.Resolved)
        {
            Mark("member");
            Say(rules.Members.Function?.LogicalName ?? string.Empty);
        }

        if (rules.ReachFromAbove is { } above)
        {
            Mark("above");
            Say(above.Function?.LogicalName ?? (mapping.At?.ClrType.FullName ?? string.Empty));
            foreach (var permission in rules.Permissions.Where(permission => !permission.FromAbove))
            {
                Say(permission.Name);
            }
        }

        if (rules.MembersLockPermission is { } membersPermission)
        {
            Mark("members-permission");
            Say(membersPermission);
        }

        if (rules.OwnerLockPermission is { } ownerPermission)
        {
            Mark("owner-permission");
            Say(ownerPermission);
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

    /// <summary>
    /// The roles that may execute a resource's functions, as the export works them out: every role a row access rule
    /// of the resource is written for, the user's, a token role's, the application's own, but never the anonymous
    /// caller's, who is nobody's member; and the role scoped system work runs as, where the rules name a scope whose
    /// work is the application's own. Each once, by the name the rules use, in the order they come.
    /// </summary>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="export">What the script is written with: the row access rules, and the roles they become.</param>
    public static IReadOnlyList<string> Grantees(MembershipRules rules, RowAccessExport export)
    {
        var anonymous = export.Roles.Anonymous;
        var grantees = new List<string>();
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        void Add(string role)
        {
            string name;
            try
            {
                name = export.Roles.Resolve(role);
            }
            catch (ArgumentException)
            {
                // A role the export cannot write a policy for is refused where the rule's policy is written.
                name = role;
            }

            if (!string.Equals(name, anonymous, StringComparison.Ordinal) && resolved.Add(name))
            {
                grantees.Add(role);
            }
        }

        foreach (var rule in export.Rules.Where(rule => string.Equals(rule.AggregateTypeName, rules.Resource.FullName, StringComparison.Ordinal)))
        {
            foreach (var role in rule.Roles)
            {
                Add(role);
            }
        }

        if (rules.SystemScopes.Count > 0)
        {
            Add(RowAccessRoles.SystemIn);
        }

        return grantees;
    }

    /// <summary>The four functions of the resource <paramref name="mapping"/> maps, under the names <paramref name="rules"/> give them.</summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <param name="written">What the functions are written with beyond the rules.</param>
    /// <exception cref="InvalidOperationException">The rules, or the way the model stores the members, are not ones these functions can be written for.</exception>
    public static IReadOnlyList<ContributedFunction> Functions(MemberMapping mapping, MembershipRules rules, string owner, Written written)
    {
        var resource = mapping.Resource;
        var members = mapping.Members;
        var held = mapping.Roles;
        var what = resource.DisplayName();

        var role = held.FindProperty(nameof(MemberRole<,>.RoleId))!;
        if (rules.Roles.Kind == RolesKind.Fixed && role.ClrType != typeof(NamedRole))
        {
            throw new InvalidOperationException(
                "The members of " + what + " hold roles known by " + role.ClrType.Name + ", and the rules '" + rules.Name + "' have fixed roles, which a member holds by name. "
                + "Declare the member class with NamedRole as its role, or say in the rules where the roles are rows: Roles.Custom() for a role class of the resource, "
                + "or Roles.External() with the function that answers which of them give a permission.");
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

        var caller = Caller(rules, mapping, written.Members);
        var memberNow = "m." + Column(members, nameof(MemberEntity<,,>.MemberId)) + " = " + caller + " AND " + Now(members, "m");
        var marker = Marker(rules, mapping, written.SystemInRole) + "\n" + ExecutedBy + string.Join(", ", written.Executors);
        var returns = "SETOF " + RowAccessModel.ColumnType(resource, id.Name);
        string Own(string name) => Asked(owner + "/" + name);

        // Where the caller holds a permission above, for a resource that is reached from above: the resources that
        // sit there. Asked with the function's own argument, the application's function is not asked at all
        // when there is no permission: a condition without a column gates the whole part.
        var above = Above(rules, mapping, written.Places);
        var notFromAbove = rules.Permissions.Where(permission => !permission.FromAbove).Select(permission => Text(permission.Name)).ToList();
        string ReachedFromAbove(string permission)
            => $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE {(permission == "$1" ? "$1 IS NOT NULL AND " : string.Empty)}"
               + (permission == "$1" && notFromAbove.Count > 0 ? $"$1 NOT IN ({string.Join(", ", notFromAbove)}) AND " : string.Empty)
               + $"r.{above!.Value.At} IN (SELECT reached.place FROM {above.Value.Function}({permission}) AS reached(place))";

        // Every resource, for the application's own work in a scope the rules name: the caller's claims say the
        // role scoped work runs as, and one of those scopes. Nobody else's token says that role.
        var ownWork = rules.SystemScopes.Count == 0
            ? null
            : $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE {{caller:role}} = {Text(written.SystemInRole)} AND {{caller:claim:scope}} IN ({string.Join(", ", rules.SystemScopes.Select(Text))})";

        // Every body is put together line by line, with the line ending said here: a function is the same text
        // whichever machine wrote the access file, and whatever line endings this file was checked out with.
        var asMember = Lines(
            marker,
            $"SELECT DISTINCT {resourceOfMember} FROM {Table(members)} m",
            $"WHERE {memberNow}");

        // A role counts only inside its membership: both periods apply now. Without a permission nothing is asked,
        // of the member rows or of a function of the application's: the first condition has no column, and
        // gates the rest.
        var asMemberWith = Lines(
        [
            marker,
            $"SELECT DISTINCT {resourceOfMember} FROM {Table(members)} m",
            $"WHERE $1 IS NOT NULL AND {memberNow}",
            .. RoleThatGives(rules, mapping, role, memberOfRole),
        ]);

        // Seen by its members; by its owner, by the owner column, who holds the owner's permissions by owning it;
        // and, where a resource is reached from above, by whoever holds the permission every member holds where the
        // resource hangs. The owner by the column, and not by its member row alone: a resource is
        // written before its owner's row, in the same statement or transaction, and a rule that reads the
        // resource through this function would otherwise keep the owner from writing that row.
        List<string> seen =
        [
            marker,
            $"SELECT joined.id FROM {Own(rules.Functions.AsMember)}() AS joined(id)",
            "UNION",
            $"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE r.{Column(resource, mapping.Owner.Name)} = {caller}",
        ];
        if (above is not null && rules.EveryMemberPermission is { } seenWith)
        {
            seen.Add("UNION");
            seen.Add(ReachedFromAbove(Text(seenWith)));
        }

        if (ownWork is not null)
        {
            seen.Add("UNION");
            seen.Add(ownWork);
        }

        // Through a role that gives the permission; by being a member, for the one permission that gives; by owning the
        // resource, for the owner's permissions; and from above, but for a permission kept from above.
        List<string> heldOn =
        [
            marker,
            $"SELECT holding.id FROM {Own(rules.Functions.AsMemberWith)}($1) AS holding(id)",
        ];
        if (rules.EveryMemberPermission is { } seePermission)
        {
            heldOn.Add("UNION");
            heldOn.Add($"SELECT joined.id FROM {Own(rules.Functions.AsMember)}() AS joined(id) WHERE $1 = {Text(seePermission)}");
        }

        if (rules.OwnerPermissions.Count > 0)
        {
            heldOn.Add("UNION");
            heldOn.Add($"SELECT r.{Column(resource, id.Name)} FROM {Table(resource)} r WHERE $1 IN ({string.Join(", ", rules.OwnerPermissions.Select(Text))}) AND r.{Column(resource, mapping.Owner.Name)} = {caller}");
        }

        if (above is not null)
        {
            heldOn.Add("UNION");
            heldOn.Add(ReachedFromAbove("$1"));
        }

        if (ownWork is not null)
        {
            // The application's own work holds every permission there is, on every resource.
            heldOn.Add("UNION");
            heldOn.Add(ownWork + " AND $1 IS NOT NULL");
        }

        // Each runs as its owner, so it reads the member rows without the policies that ask it, and is
        // executable by the roles the row access rules of the resource are for, and no other. The two that answer access say so, by the type of
        // the resource's id: a rule asks them through a [ResourceAccessContract] of that id, and never by name.
        return
        [
            new ContributedFunction(rules.Functions.AsMember, "", returns, asMember, SecurityDefiner: true, GrantTo: written.GrantTo),
            new ContributedFunction(rules.Functions.AsMemberWith, "text", returns, asMemberWith, SecurityDefiner: true, GrantTo: written.GrantTo),
            new ContributedFunction(rules.Functions.Seen, "", returns, Lines([.. seen]), SecurityDefiner: true, GrantTo: written.GrantTo, Answers: new(id.ClrType, ResourceAccessSet.Seen)),
            new ContributedFunction(rules.Functions.HeldOn, "text", returns, Lines([.. heldOn]), SecurityDefiner: true, GrantTo: written.GrantTo, Answers: new(id.ClrType, ResourceAccessSet.HeldOn)),
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
                "A permission of " + rules.Resource.Name + " locks the owner, and the lock on the owner column would be called '" + name
                + "', which is longer than the 63 characters Postgres keeps of a name. Give the resource a shorter name, with [Members(\"...\")].");
    }

    /// <summary>The longest name Postgres keeps whole.</summary>
    private const int LongestName = 63;

    /// <summary>What the body of the function the lock's trigger runs stands between.</summary>
    private const string BodyQuote = "$body$";

    /// <summary>The lock's name as the rules' name gives it, however long: letters, digits and underscores, so it needs no quotes.</summary>
    private static string OwnerLockName(MembershipRules rules) => rules.Name.Replace('.', '_').Replace('-', '_') + "_owner_stays";

    /// <summary>
    /// The lock on the two member tables, for rules with a permission that locks the members
    /// (<see cref="MembershipRules.MembersLockPermission"/>): for each database role that may ask the
    /// functions, a restrictive policy for adding, changing and removing a row of either table, which lets a
    /// row be written only on a resource the caller holds that permission on. Restrictive, so it narrows whatever the
    /// application's own rules allow, and allows nothing itself: reading the rows stays what those rules say.
    /// None for rules that name no such permission: the tables are then written as the application's rules let a
    /// caller change the resource.
    /// <para>
    /// Where the rules name the permission that changes the owner as well, whoever holds that permission on a resource
    /// writes its member rows too: naming an owner puts the new owner on the list and takes or keeps the one
    /// before, and whoever may name the owner could name itself, so nothing is given that it did not have.
    /// </para>
    /// <para>
    /// Where a customer makes the roles, a second restrictive policy on the table of the roles a
    /// member holds, for adding and changing a row: the role it holds is one the caller sees in the role
    /// table (<see cref="RoleSeen"/>). The functions find a role by its id, whoever's it is, so a role of
    /// another customer of the application's, written onto a member list past the application, would give
    /// its permissions there. The package knows no customer and no column for one; the application's own rules on
    /// its role table do, and the question is asked as the caller, under them: a role they keep from the
    /// caller is no role it may give.
    /// </para>
    /// </summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <param name="written">What the lock is written with beyond the rules: the roles it holds.</param>
    /// <exception cref="InvalidOperationException">A customer makes the roles, and the model maps no role class for the resource.</exception>
    public static IReadOnlyList<ContributedPolicy> Policies(MemberMapping mapping, MembershipRules rules, string owner, Written written)
    {
        if (rules.MembersLockPermission is not { } permission)
        {
            return [];
        }

        var id = mapping.Resource.FindProperty("Id")
            ?? throw new InvalidOperationException(mapping.Resource.DisplayName() + " has no Id property, so the lock on its members has nothing to ask about.");

        // The column of a member's row that says which resource it is of, and the same for a role's row, which
        // carries its member's permission and so the resource with it.
        var toResource = mapping.Members.FindOwnership()!;
        var ofMember = toResource.Properties[IndexOf(toResource.PrincipalKey.Properties, id)];
        var toMember = mapping.Roles.FindOwnership()!;
        var ofRole = toMember.Properties[IndexOf(toMember.PrincipalKey.Properties, ofMember)];

        // Asked once for a statement: the resources the caller holds a permission on, as the functions answer it.
        string Held(string held) => "(SELECT held.id FROM " + Asked(owner + "/" + rules.Functions.HeldOn) + "(" + Text(held) + ") AS held(id))";
        var handsOn = rules.OwnerLockPermission is { } ownerPermission && !string.Equals(ownerPermission, permission, StringComparison.Ordinal) ? ownerPermission : null;
        var policies = new List<ContributedPolicy>();
        foreach (var (table, column) in new[] { (mapping.Members, ofMember), (mapping.Roles, ofRole) })
        {
            var resource = Column(table, column.Name);
            var holds = handsOn is null
                ? resource + " IN " + Held(permission)
                : "(" + resource + " IN " + Held(permission) + " OR " + resource + " IN " + Held(handsOn) + ")";
            foreach (var role in written.GrantTo)
            {
                policies.Add(new ContributedPolicy(table, MemberLock, "INSERT", role, null, holds, Restrictive: true));
                policies.Add(new ContributedPolicy(table, MemberLock, "UPDATE", role, holds, holds, Restrictive: true));
                policies.Add(new ContributedPolicy(table, MemberLock, "DELETE", role, holds, null, Restrictive: true));
            }
        }

        if (rules.Roles.Kind == RolesKind.Custom)
        {
            // Asked as the caller, so the application's rules on its role table decide which roles are there to
            // see. Removing a row asks nothing of its role, so one that should not be there can be taken away.
            var kept = CustomRoleClass(rules, mapping);
            var held = mapping.Roles;
            var seen = $"EXISTS (SELECT 1 FROM {Table(kept)} k WHERE k.{Column(kept, nameof(CustomRoleAggregate<>.Id))} = "
                       + $"{Table(held)}.{Column(held, nameof(MemberRole<,>.RoleId))})";
            foreach (var role in written.GrantTo)
            {
                policies.Add(new ContributedPolicy(held, RoleSeen, "INSERT", role, null, seen, Restrictive: true));
                policies.Add(new ContributedPolicy(held, RoleSeen, "UPDATE", role, "true", seen, Restrictive: true));
            }
        }

        return policies;
    }

    /// <summary>What a policy of the lock on the member tables is about, which its name starts with.</summary>
    public const string MemberLock = "Members change with the permission";

    /// <summary>
    /// What the policy of the lock that holds a role given to one the caller sees is about, which its name
    /// starts with: written on the table of the roles a member holds, where a customer makes the roles and a
    /// permission locks the members.
    /// </summary>
    public const string RoleSeen = "Members hold roles the caller sees";

    /// <summary>
    /// The trigger of a resource's membership: the lock on the owner column (<see cref="OwnerColumnStatements"/>).
    /// Where a customer makes the roles, the trigger an earlier form wrote to keep an owner's role in use is taken
    /// away: the owner holds no role, and a role is archived like any other.
    /// </summary>
    /// <param name="mapping">The resource and its member tables, as the context's model maps them.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <param name="owner">The owner the functions' logical names start with.</param>
    /// <param name="schema">The schema the resource's functions are in, as SQL names it: <see cref="RowAccessModel.Schema(Microsoft.EntityFrameworkCore.Metadata.IModel)"/>.</param>
    /// <param name="roles">The database roles the rules' symbolic roles are written as.</param>
    /// <param name="written">What the lock is written with beyond the rules: the roles it holds.</param>
    /// <exception cref="InvalidOperationException">
    /// A role the lock holds is one no policy can be for, a trigger's name is too long, or what a trigger is written
    /// from has the quote of its function's body in it.
    /// </exception>
    public static IReadOnlyList<string> Statements(MemberMapping mapping, MembershipRules rules, string owner, string schema, RowAccessRoleNames roles, Written written)
    {
        List<string> statements = [.. OwnerColumnStatements(mapping, rules, owner, schema, roles, written)];
        if (rules.Roles.Kind == RolesKind.Custom && mapping.RoleClass is { } roleClass)
        {
            var left = rules.Name.Replace('.', '_').Replace('-', '_') + "_owner_role_stays";
            if (left.Length <= LongestName)
            {
                statements.Add($"DROP TRIGGER IF EXISTS {left} ON {Table(roleClass)}");
                statements.Add($"DROP FUNCTION IF EXISTS {FunctionIn(schema, left)}()");
            }
        }

        return statements;
    }

    /// <summary>
    /// A function of the schema the resource's functions are in, <paramref name="schema"/> as SQL names it, by a
    /// name that needs no quotes, as a statement names it.
    /// </summary>
    private static string FunctionIn(string schema, string name) => Braces(schema) + "." + name;

    /// <summary>
    /// The lock on the owner column, for rules with a permission that locks the owner
    /// (<see cref="MembershipRules.OwnerLockPermission"/>): a trigger on the resource's table that fires when a
    /// statement would leave a row with another owner than it had, and refuses it, as a policy refuses, unless
    /// the caller held that permission on the resource before the change. A trigger, because a policy sees the row a
    /// statement leaves behind and not the one it found: only here are both at hand, and nothing a caller can
    /// ask is added for it. It refuses as the toolkit's access guards do (<see cref="RowAccessModel.Refusal"/>), with
    /// the lock's name as the constraint, so a save it refuses is <c>access.refused</c> and the warning names the lock.
    /// <para>
    /// It holds the database roles that may ask the functions, and nobody else: scoped system work among them, which
    /// passes in a scope the rules let through, and the role that owns the tables not. For rules with no such
    /// permission, or with no role that may ask the functions, the statements take the lock away, so one written from
    /// earlier rules does not stay behind.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> OwnerColumnStatements(MemberMapping mapping, MembershipRules rules, string owner, string schema, RowAccessRoleNames roles, Written written)
    {
        var resource = mapping.Resource;

        // No role may ask the functions, so no caller is held to the lock: what reaches the table is the tables' owner.
        if (rules.OwnerLockPermission is not { } permission || written.GrantTo.Count == 0)
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
        foreach (var role in written.GrantTo)
        {
            string resolved;
            try
            {
                resolved = roles.Resolve(role);
            }
            catch (ArgumentException unresolved)
            {
                throw new InvalidOperationException(
                    "A row access rule of " + rules.Resource.Name + " is for the role '" + role + "', which may ask its functions, and the lock on the owner column cannot be written for it: " + unresolved.Message,
                    unresolved);
            }

            if (!locked.Contains(resolved, StringComparer.Ordinal))
            {
                locked.Add(resolved);
            }
        }

        var column = Column(resource, mapping.Owner.Name);
        var message = "The owner of a row of " + RowAccessModel.Table(resource) + " is changed by a caller that holds " + permission + " on it.";
        var trigger = Lines(
            $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger",
            "    LANGUAGE plpgsql SET search_path = '' AS " + BodyQuote,
            "BEGIN",
            "    " + Marker(rules, mapping, roles.SystemIn),
            "    -- The callers who may ask the functions are held to this, scoped system work among them. The tables' owner is not.",
            $"    IF CURRENT_USER IN ({string.Join(", ", locked.Select(Text))})",
            $"       AND NOT EXISTS (SELECT 1 FROM {Asked(owner + "/" + rules.Functions.HeldOn)}({Text(permission)}) AS held(id) WHERE held.id = OLD.{Column(resource, id.Name)}) THEN",
            "        " + Braces(RowAccessModel.Refusal(name, message)),
            "    END IF;",
            "    RETURN NEW;",
            "END",
            BodyQuote);

        // The body stands between two of these, so a third, in the permission, a role or a name of the model, would
        // end it early: what came after would be read as the statement's own.
        if (trigger.Split(BodyQuote).Length != 3)
        {
            throw new InvalidOperationException(
                "The lock on the owner column of " + resource.DisplayName() + " cannot be written from the rules '" + rules.Name + "': the permission that locks the owner, '" + permission
                + "', a role or a name of the model has '" + BodyQuote + "' in it, which would end the function's body early.");
        }

        return
        [
            trigger,
            $"REVOKE ALL ON FUNCTION {function}() FROM PUBLIC",
            $"DROP TRIGGER IF EXISTS {name} ON {Table(resource)}",
            Lines(
                $"CREATE TRIGGER {name} BEFORE UPDATE OF {column} ON {Table(resource)}",
                $"    FOR EACH ROW WHEN (OLD.{column} IS DISTINCT FROM NEW.{column})",
                $"    EXECUTE FUNCTION {function}()"),
        ];
    }

    /// <summary>
    /// The lines that ask for a role, held now in the membership of the row <c>m</c>, that gives the permission
    /// <c>$1</c>: of fixed roles, as the rules say which gives which permission; of the roles a customer makes, as
    /// their rows say it; or of the roles kept elsewhere, as the application's function answers it. Of rows,
    /// only for a permission a member's role can give at all.
    /// </summary>
    private static IEnumerable<string> RoleThatGives(MembershipRules rules, MemberMapping mapping, IProperty role, string memberOfRole)
    {
        var held = mapping.Roles;
        var heldRole = "h." + Column(held, role.Name);
        if (rules.Roles.Kind == RolesKind.Custom)
        {
            return CustomRoleThatGives(rules, mapping, heldRole, memberOfRole);
        }

        if (rules.Roles.Kind == RolesKind.Fixed)
        {
            // Which role gives which permission, as the rules say it: only the permissions a member's role can give at all.
            var pairs = rules.Roles.Declared
                .SelectMany(declared => rules.PermissionsOf(new NamedRole(declared.Name)).Select(permission => "(" + Braces(RowAccessModel.Stored(held, role.Name, new NamedRole(declared.Name))) + ", " + Text(permission) + ")"))
                .ToList();
            var gives = pairs.Count > 0
                ? "(VALUES " + string.Join(", ", pairs) + ") AS gives(role, permission)"
                : "(SELECT ''::pg_catalog.text AS role, ''::pg_catalog.text AS permission WHERE false) AS gives";

            return
            [
                $"  AND EXISTS (SELECT 1 FROM {Table(held)} h",
                $"              JOIN {gives} ON gives.role = {heldRole}",
                $"              WHERE {memberOfRole} AND {Now(held, "h")} AND gives.permission = $1)",
            ];
        }

        var function = rules.Roles.Function
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' say the roles are kept elsewhere, and name no function that answers which of them give a permission in the database. Name it, "
                + "the organization package's roles_with_permission say, or a DatabaseFunction of the application's: a function that takes the permission as text and answers the ids of the roles that give it.");

        // Whatever a role holds where it is kept, it gives a member only a permission the rules let a member's role
        // give: said here as the rules say it, in front of what the application's function answers.
        List<string> lines = [.. WithinMemberPermissions(rules)];
        lines.Add($"  AND EXISTS (SELECT 1 FROM {Table(held)} h");
        lines.Add($"              WHERE {memberOfRole} AND {Now(held, "h")}");
        lines.Add($"                AND {heldRole} IN (SELECT giving.role FROM {Asked(function)}($1) AS giving(role)))");
        return lines;
    }

    /// <summary>
    /// The lines that ask for a role a customer made, held now in the membership of the row <c>m</c>,
    /// that gives the permission <c>$1</c>: a row of the application's role class that is in use and holds the permission,
    /// read straight from its table, for a permission a member's role can give at all.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model maps no role class for the resource, one known by another id than a member holds, or one whose permissions are no array.</exception>
    private static List<string> CustomRoleThatGives(MembershipRules rules, MemberMapping mapping, string heldRole, string memberOfRole)
    {
        var held = mapping.Roles;
        var what = mapping.Resource.DisplayName();
        var kept = CustomRoleClass(rules, mapping);

        var id = kept.FindProperty(nameof(CustomRoleAggregate<>.Id))!;
        var heldBy = held.FindProperty(nameof(MemberRole<,>.RoleId))!;
        if (id.ClrType != heldBy.ClrType)
        {
            throw new InvalidOperationException(
                "The members of " + what + " hold roles known by " + heldBy.ClrType.Name + ", and its role class " + kept.DisplayName() + " is known by " + id.ClrType.Name
                + ". A member holds a role of the resource by that role's id: declare the member class with " + id.ClrType.Name + " as its role.");
        }

        var stored = RowAccessModel.ColumnType(kept, nameof(CustomRoleAggregate<>.Permissions)).Trim();
        if (!stored.EndsWith("[]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                kept.DisplayName() + "." + nameof(CustomRoleAggregate<>.Permissions) + " is stored as " + stored + ", and the functions read a role's permissions from an array, such as text[]. "
                + "Leave the column as IsCustomRole maps it.");
        }

        // What a row holds it may hold from before the rules changed: it gives a member only a permission the rules
        // let a member's role give now, said here as the rules say it, in front of what the rows say.
        List<string> lines = [.. WithinMemberPermissions(rules)];
        lines.Add($"  AND EXISTS (SELECT 1 FROM {Table(held)} h");
        lines.Add($"              JOIN {Table(kept)} k ON k.{Column(kept, id.Name)} = {heldRole}");
        lines.Add($"              WHERE {memberOfRole} AND {Now(held, "h")}");
        lines.Add(
            $"                AND k.{Column(kept, nameof(CustomRoleAggregate<>.Status))} = "
            + Braces(RowAccessModel.Stored(kept, nameof(CustomRoleAggregate<>.Status), CustomRoleStatus.Active))
            + $" AND $1 = ANY (k.{Column(kept, nameof(CustomRoleAggregate<>.Permissions))}))");
        return lines;
    }

    /// <summary>The application's role class of a resource whose roles a customer makes.</summary>
    /// <exception cref="InvalidOperationException">The model maps none for the resource.</exception>
    private static IEntityType CustomRoleClass(MembershipRules rules, MemberMapping mapping)
    {
        var what = mapping.Resource.DisplayName();
        return mapping.RoleClass
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' say a customer makes the roles of " + what + ", and the model maps no role class for it. Declare one with the role template, "
                + "[CustomRole<TRoleId, " + what + ">], and map it where the context builds its model: modelBuilder.Entity<" + what + "Role>().IsCustomRole().");
    }

    /// <summary>
    /// The line that keeps a role that is a row to the permissions a member's role can give, as the rules say it; no
    /// line where the rules let a member's role give every permission.
    /// </summary>
    private static IEnumerable<string> WithinMemberPermissions(MembershipRules rules)
    {
        var permissions = string.Join(", ", rules.RolePermissions.Permissions.Select(Text));
        var cut = rules.RolePermissions.Excepts
            ? rules.RolePermissions.Permissions.Count == 0 ? null : $"$1 NOT IN ({permissions})"
            : rules.RolePermissions.Permissions.Count == 0 ? "false" : $"$1 IN ({permissions})";

        return cut is null ? [] : [$"  AND {cut}"];
    }

    /// <summary>
    /// For rules that let a resource be reached from above, the column that holds where the resource hangs and
    /// the function that answers where the caller holds a permission; <see langword="null"/> for rules that do not.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource marks no place it hangs at.</exception>
    private static (string At, string Function)? Above(MembershipRules rules, MemberMapping mapping, DatabaseFunction? places)
    {
        if (rules.ReachFromAbove is null || places is null)
        {
            return null;
        }

        var at = mapping.At
            ?? throw new InvalidOperationException(
                "The rules '" + rules.Name + "' let " + mapping.Resource.DisplayName() + " be reached from above, and it marks no place it hangs at. "
                + "Mark the property that says where it hangs [At], and map it with HasMembers.");

        return (Column(mapping.Resource, at.Name), Asked(places));
    }

    /// <summary>
    /// Who the caller is as a member, as the script fills it in: the caller's user id, a claim of its token,
    /// or what the application's function answers, compared with the column that holds who a member is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The column does not store what the source answers, or the rules name no function.</exception>
    private static string Caller(MembershipRules rules, MemberMapping mapping, MemberSource members)
    {
        var stored = RowAccessModel.ColumnType(mapping.Members, nameof(MemberEntity<,,>.MemberId)).Trim();
        var what = mapping.Resource.DisplayName();

        switch (members.Kind)
        {
            case MemberSourceKind.CallerId:
                return string.Equals(stored, "uuid", StringComparison.OrdinalIgnoreCase)
                    ? "{caller:uid}"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' take the caller's member id from MemberSource.CallerId, a uuid, and the members of " + what + " are stored as " + stored
                        + ". Known by an id over a Guid, a member is stored as uuid.");

            case MemberSourceKind.Claim:
                return IsText(stored)
                    ? "{caller:claim:" + members.ClaimPath + "}"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' take the caller's member id from the claim '" + members.ClaimPath + "', a text, and the members of " + what + " are stored as " + stored
                        + ". Known by an id over a text, a member is stored as text or character varying.");

            default:
                // Asked once for a statement, of the application's own function: whoever it answers nobody for is nobody's member.
                return members.Function is { } function
                    ? "(SELECT " + Asked(function) + "())"
                    : throw new InvalidOperationException(
                        "The rules '" + rules.Name + "' have the application resolve who the caller is as a member of " + what + ", and name no function that answers it in the database. "
                        + "Name it, the organization package's caller_seat say, or a DatabaseFunction of the application's: a function without parameters that answers the caller's member id, as the members are stored, or NULL.");
        }
    }

    /// <summary>A function asked by its logical name, <c>owner/name</c>, as the script fills it in: the name it has where it is defined.</summary>
    private static string Asked(DatabaseFunction function) => Asked(function.LogicalName);

    /// <summary>A function asked by its logical name, <c>owner/name</c>, as the script fills it in.</summary>
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

    /// <summary>A permission as an SQL string literal.</summary>
    private static string Text(string text) => Braces(RowAccessModel.Literal(text));

    /// <summary>
    /// <paramref name="sql"/> with its braces doubled: in the body of a contributed function a brace opens a
    /// place the script fills in, and a name or a permission that has one means the brace itself.
    /// </summary>
    private static string Braces(string sql) => sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
}
