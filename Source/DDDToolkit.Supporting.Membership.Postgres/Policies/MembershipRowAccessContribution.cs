using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// The membership of one kind of resource in the database: the five set functions that answer, for the caller
/// of the connection, the questions the access questions answer in C#, written from the resource's rules and
/// from the model of the context that maps it; and, where the rules name the permissions that change the members
/// and the owner, the lock that holds a caller that reaches the database to those permissions. This package declares
/// it with <c>[assembly: RowAccessContribution]</c>, so the Supabase export of every application that references
/// the package writes it, once for each kind of resource whose rules the application marks, closed over the
/// member class the mark names:
/// <code>
/// [MembershipRules&lt;DocumentShare&gt;]
/// public static MembershipRules Rules { get; } = MembershipRules.For&lt;Document&gt;(Roles.Fixed([...]));
/// </code>
/// The rules are the ones the resource is registered with: the functions say what the rules say, and a
/// start-up check holds the database to it
/// (<see cref="MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync"/>). The export makes the class before
/// the application starts, so the rules are a declaration it can reach without the application's services, and
/// the mark is how it finds them (<see cref="MembershipRulesAttribute{TMember}"/>). A script written by hand, or
/// a test, makes it with <c>new MembershipRowAccessContribution&lt;DocumentShare&gt;(DocumentMembership.Rules)</c>.
/// </summary>
/// <remarks>
/// <para>
/// For the context that maps the resource with <c>HasMembers</c>, in its default schema, under the names the
/// rules give them (<see cref="MembershipFunctions"/>):
/// </para>
/// <list type="bullet">
/// <item><c>as member</c>: the ids of the resources the caller is a member of now.</item>
/// <item><c>as member with (permission)</c>: those where the caller is a member now and holds a role now that gives
/// the permission, as the rules say which role gives which permission and which permissions a member's role can give at all.</item>
/// <item><c>seen</c>: the resources the caller sees: those it is a member of now; those it owns, by the owner
/// column, from the moment a resource's row names it, so a rule that reads a resource through this function
/// lets its owner write the member rows of a resource it has just opened; and, where the rules name a permission for
/// seeing and let a resource be reached from above, those that sit where the caller holds that permission.</item>
/// <item><c>held on (permission)</c>: the resources the caller holds the permission on, however the rules let it be held:
/// through a role, by being a member for the one permission that gives, by owning the resource, whose owner holds
/// every permission of it, or from above.</item>
/// </list>
/// <para>
/// Each runs as its owner with an empty search path, so it reads the member rows without the policies that ask
/// it, and is executable by the database roles the resource's row access rules are written for, and by nobody
/// else: the user's, a token role's, the application's own, never the anonymous caller's, who is nobody's member;
/// and the role scoped system work runs as, where the rules name a scope whose work is the application's own. So a
/// signed-in user asks them through the rules for users, an operator's token role whose own rule asks them is
/// granted them with that rule, and an import that runs as system work in the module's scope asks them as that role.
/// Each records the roles in a line of its body, which the start-up check holds the database's grants to. Who the
/// caller is as a member is what the rules say: what follows from the member id, the caller's seat for the seat id of
/// a package that keeps an organization's seats or the caller's user id for an id over a Guid, a claim of its token, or what a function of the
/// application's answers. Whether a period applies is asked of the database's clock.
/// </para>
/// <para>
/// What the rules have somebody else answer is asked of functions by their logical names, <c>owner/name</c>: who
/// the caller is as a member, and where the caller holds a permission above, of the function a contribution of the export
/// answers for the type of the member or of the place (<see cref="ICallerFunctions"/>), an organization package's for
/// its seat and its unit, or of the one the rules name (<see cref="MemberSource.Resolved"/>, <see cref="MembershipRules.ReachFromAbove"/>);
/// which of the roles kept elsewhere give a permission, of the one <see cref="Roles.External"/> names. The script finds a
/// function wherever it is defined: in this context's script or another's it is written with. A name nothing
/// defines there is refused when the script is written, and rules that leave a function they need to nobody are
/// refused here. What a role kept elsewhere gives a member is cut in these functions as it is in C#: only a permission the
/// rules let a role give.
/// </para>
/// <para>
/// Roles a customer makes (<see cref="Roles.Custom"/>) are read from the table of the application's role class,
/// which the context maps with <c>IsCustomRole</c>: a role in use that holds the permission, cut the same way. No function
/// is asked for them, and the functions read those rows as they read the member rows, as their owner: which rows a
/// caller may read of that table is the application's own rule, and the functions do not lean on it. The owner
/// holds no role, so no role is kept in use for it.
/// </para>
/// <para>
/// The application's own work in a scope the rules name (<see cref="MembershipRules.SystemScopes"/>) is
/// answered every resource by <c>seen</c> and <c>held on</c>, as it is in C#: its claims say the role scoped
/// work runs as, and that scope, which may ask them wherever the rules name a scope. Work in any other scope is
/// nobody's member, and is answered nothing.
/// </para>
/// <para>
/// It keeps no table to itself, and allows nothing. What a caller may read and change of a resource is the
/// application's own row access rules to say, and they ask <c>seen</c> and <c>held on</c> by the resource's id,
/// through a contract the application declares for each, with no function's name in it:
/// <c>[ResourceAccessContract&lt;DocumentId&gt;(ResourceAccessSet.Seen)]</c> on an empty static partial class. The
/// two functions say which set they answer for the resource (<see cref="ResourceAccessAnswer"/>), and the script
/// writes such a rule with them, whatever the rules call them. The member tables follow the resource's rules, as
/// every table of an aggregate's entities does. The functions' owner is the rules' name, a dot in it written as a
/// dash, so their logical names are <c>documents/documents_i_see</c> and so on, which SQL of the application's own
/// may ask as well.
/// </para>
/// <para>
/// That the member tables follow the resource's rules means whoever those rules let change a resource
/// writes its member rows and its owner column, by any statement that reaches the database: a caller that
/// only edits a resource could give itself a role on it, or make itself its owner. So the permissions the
/// application's commands require for that are marked <c>ManagesMembers</c> and <c>NamesOwner</c>, and the lock is
/// written from them, for the database roles that may ask the functions:
/// </para>
/// <list type="bullet">
/// <item><see cref="MembershipRules.ManagesMembersPermission"/>: a restrictive policy on both member tables for
/// adding, changing and removing a row. A row is written only on a resource the caller holds that permission on, or
/// the one that names the owner, where there is one: naming an owner writes member rows too. It
/// narrows what the application's rules allow and allows nothing itself. Where a customer makes the
/// roles, a second one on the table of the roles a member holds, for adding and changing a row: the role
/// is one the caller sees in the role table, asked as the caller, so the application's own rules on that
/// table decide it. The functions find a role by its id, whoever's it is; this keeps a role the caller does
/// not see, another customer's, off a member list it writes.</item>
/// <item><see cref="MembershipRules.NamesOwnerPermission"/>: a trigger on the resource's table that refuses, as a
/// policy does, a statement that would leave a row with another owner, unless the caller held that permission on
/// the resource before.</item>
/// </list>
/// <para>
/// The lock asks what the caller holds when each statement runs, as every policy does, and a change of a
/// resource's members is several statements. Two things follow. A resource is opened with its owner on the
/// list, and those first rows pass the lock like any other: an owner holds the permissions marked <c>Owner</c>
/// (<see cref="MembershipRules.OwnerPermissions"/>), so the one that manages the members is one of them wherever a
/// caller opens a resource as itself. And an owner that hands its own resource on, holding the permissions by
/// owning it alone, holds them no longer once the owner column has changed: the rows written after that are
/// not its to write, under the lock or under an application's own rule that asks what the caller holds.
/// Somebody who holds the permission through a role, or from above, hands a resource on as itself; an owner's own
/// hand-over is saved as the application's own work, once the command's check let it through.
/// </para>
/// <para>
/// The lock holds the database roles that may ask the functions, and no other: a role no rule of the resource is
/// for holds no permission, and nothing is written for it. The role scoped system work runs as is held as well, so work in a
/// scope the rules do not let through writes no member row; work in one they do holds every permission, and passes. The role
/// that owns the tables is not held, and where no role may ask the functions no lock is written.
/// </para>
/// <para>
/// The lock is for a resource the
/// application gave row access rules: on a resource without any, the policies would be the only ones on
/// the member tables, and nobody would read a row of them. Rules with neither permission leave the tables
/// to the resource's own rules, and the start-up check says so
/// (<see cref="MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync"/>).
/// </para>
/// <para>
/// A context that does not map the member class is none of its business, so an application with several
/// kinds of resource marks the rules of each, the export makes one contribution for each, and each writes for
/// the context of its own resource.
/// </para>
/// </remarks>
/// <typeparam name="TMember">The application's member class of the resource.</typeparam>
public class MembershipRowAccessContribution<TMember> : IRowAccessContribution
    where TMember : class
{
    /// <summary>The functions of the resource whose members are of <typeparamref name="TMember"/>, written from <paramref name="rules"/>.</summary>
    /// <param name="rules">
    /// The resource's rules: the ones it is registered with, which the export takes from the member the application
    /// marks <c>[MembershipRules&lt;TMember&gt;]</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public MembershipRowAccessContribution([FromApplication(typeof(MembershipRulesAttribute<>))] MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Rules = rules;
        Owner = rules.Name.Replace('.', '-');
    }

    /// <summary>
    /// The kinds of answer the export's contributions may give about the caller: an organization package's for its seat and its unit. A
    /// package's contribution reaches the export inside the class the build writes for it, so that is looked into.
    /// </summary>
    private static IEnumerable<ICallerFunctions> Answering(RowAccessExport export)
        => export.Contributions.Select(static contribution => contribution is IPackageRowAccessContribution package ? package.Contribution : contribution).OfType<ICallerFunctions>();

    /// <summary>The rules the functions are written from.</summary>
    public MembershipRules Rules { get; }

    /// <inheritdoc />
    /// <remarks>The rules' name, a dot in it written as a dash: what a question an application declares for these functions names as its owner.</remarks>
    public string Owner { get; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Who the caller is as a member, or where it holds a permission above, is answered by nobody: the rules name no
    /// function, and no contribution answers one for the type of the member or of the place; the rules let the
    /// resource be reached from above, and it marks no place it hangs at; the roles are fixed, and the members
    /// hold roles that are not <see cref="NamedRole"/>; a customer makes the roles, and the model maps no role class
    /// for the resource, or one a member does not hold by its id; or the model stores a member, a moment of a period
    /// or a role's permissions as something the functions cannot compare; or a permission names the owner and a role the lock
    /// cannot be written for may ask the functions; or a trigger's name would be longer than Postgres keeps one.
    /// </exception>
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        if (MembershipModel.For(context.Model, typeof(TMember)) is not { } mapping)
        {
            return null;
        }

        // Where the functions are: the context's default schema, as every contributed function's is, quoted.
        var schema = RowAccessModel.Schema(context.Model);
        var written = WrittenWith(context, mapping, export);
        return new RowAccessContributionResult(
            MembershipSql.Functions(mapping, Rules, Owner, written),
            MembershipSql.Policies(mapping, Rules, Owner, written),
            MembershipSql.Statements(mapping, Rules, Owner, schema, export.Roles, written));
    }

    /// <summary>What the functions are written with beyond the rules, for <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">Who the caller is as a member, or where it holds a permission above, is answered by nobody.</exception>
    private Written WrittenWith(DbContext context, MemberMapping mapping, RowAccessExport export)
    {
        var what = mapping.Resource.DisplayName();
        var members = Rules.Members;
        if (members.Kind == MemberSourceKind.Inferred)
        {
            // Follows from the member id: a contribution that keeps such an id answers who the caller is as one; an id
            // over a Guid is the caller's own; anything else nobody can tell.
            var memberId = mapping.Members.FindProperty(nameof(MemberEntity<,,>.MemberId))!.ClrType;
            members = Answering(export).Select(answers => answers.CallerAs(context, memberId)).FirstOrDefault(function => function is not null) is { } function
                ? MemberSource.Resolved(function)
                : string.Equals(RowAccessModel.ColumnType(mapping.Members, nameof(MemberEntity<,,>.MemberId)).Trim(), "uuid", StringComparison.OrdinalIgnoreCase)
                    ? MemberSource.CallerId
                    : throw new InvalidOperationException(
                        "Who the caller is as a member of " + what + " follows from what a member is known by, " + memberId.Name + ", and nothing answers it in the database: "
                        + "no contribution keeps such an id, and it is no id over a Guid, the caller's own. Map the read model of the package that keeps such ids where "
                        + "the context maps " + what + " for a seat, or say who a member is in the rules: MemberSource.Claim(path), or MemberSource.Resolved(function).");
        }

        DatabaseFunction? places = null;
        if (Rules.ReachFromAbove is { } above)
        {
            var at = mapping.At
                ?? throw new InvalidOperationException(
                    "The rules '" + Rules.Name + "' let " + what + " be reached from above, and it marks no place it hangs at. "
                    + "Mark the property that says where it hangs [At], and map it with HasMembers.");
            places = above.Function
                ?? Answering(export).Select(answers => answers.PlacesWhereCallerHolds(context, at.ClrType)).FirstOrDefault(function => function is not null)
                ?? throw new InvalidOperationException(
                    "The rules '" + Rules.Name + "' let " + what + " be reached from above, where it hangs at a " + at.ClrType.Name + ", and nothing answers in the database where "
                    + "the caller holds a permission at one: no contribution keeps such a place. Map the read model of the package that keeps such places where the context maps "
                    + what + ", or name the application's own function: reachFromAbove: DepotFunctions.BaysWhereIHold.");
        }

        var grantTo = MembershipSql.Grantees(Rules, export);
        List<string> executors = [];
        foreach (var role in grantTo)
        {
            string resolved;
            try
            {
                resolved = export.Roles.Resolve(role);
            }
            catch (ArgumentException unresolved)
            {
                throw new InvalidOperationException(
                    "A row access rule of " + what + " is for the role '" + role + "', which may ask its functions, and they cannot be granted to it: " + unresolved.Message,
                    unresolved);
            }

            executors.Add(resolved);
        }

        return new Written(members, places, grantTo, [.. executors.Order(StringComparer.Ordinal)], export.Roles.SystemIn);
    }
}
