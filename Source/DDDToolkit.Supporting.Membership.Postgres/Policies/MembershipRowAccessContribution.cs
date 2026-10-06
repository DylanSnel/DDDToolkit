using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.Postgres;

/// <summary>
/// The membership of one kind of resource in the database: the four set functions that answer, for the caller
/// of the connection, the questions the access questions answer in C#, written from the resource's rules and
/// from the model of the context that maps it; and, where the rules name the keys that change the members
/// and the owner, the lock that holds a caller that reaches the database to those keys. The application
/// lists a class of its own for each kind of resource, derived from this one closed over that resource's
/// member class, in the project that runs the export:
/// <code>
/// [assembly: UseRowAccessContribution(typeof(DocumentsMembershipFunctions))]
///
/// public sealed class DocumentsMembershipFunctions() : MembershipRowAccessContribution&lt;DocumentShare&gt;(DocumentMembership.Rules);
/// </code>
/// The rules are the ones the resource is registered with: the functions say what the rules say, and a
/// start-up check holds the database to it
/// (<see cref="MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync"/>). The export makes the class with
/// <c>new</c> before the application starts, so the rules are a declaration the class can reach without the
/// application's services.
/// </summary>
/// <remarks>
/// <para>
/// For the context that maps the resource with <c>HasMembers</c>, in its default schema, under the names the
/// rules give them (<see cref="MembershipFunctions"/>):
/// </para>
/// <list type="bullet">
/// <item><c>as member</c>: the ids of the resources the caller is a member of now.</item>
/// <item><c>as member with (key)</c>: those where the caller is a member now and holds a role now that gives
/// the key, as the rules say which role gives which key and which keys a member's role can give at all.</item>
/// <item><c>seen</c>: the resources the caller sees: those it is a member of now; those it owns, by the owner
/// column, from the moment a resource's row names it, so a rule that reads a resource through this function
/// lets its owner write the member rows of a resource it has just opened; and, where the rules name a key for
/// seeing and let a resource be reached from above, those that sit where the caller holds that key.</item>
/// <item><c>held on (key)</c>: the resources the caller holds the key on, however the rules let it be held:
/// through a role, by being a member for the one key that gives, by owning the resource, whose owner holds
/// every key of it, or from above.</item>
/// </list>
/// <para>
/// Each runs as its owner with an empty search path, so it reads the member rows without the policies that ask
/// it, and is executable by the database roles the rules name (<see cref="MembershipRules.GrantTo"/>) and by
/// nobody else. Who the caller is as a member is what the rules say: the caller's user id, a claim of its
/// token, or what a function of the application's answers. Whether a period applies is asked of the
/// database's clock.
/// </para>
/// <para>
/// What the rules have the application answer is asked of functions the application defines, each named in
/// the rules by its logical name, <c>owner/name</c>: who the caller is as a member
/// (<see cref="MemberSource.Resolved"/>), which of the roles kept elsewhere give a key
/// (<see cref="MembershipRules.RolesKeptElsewhere"/>), and where the caller holds a key above
/// (<see cref="MembershipRules.Above"/>). A logical name is a text, so nothing here refers to whatever defines
/// the function, and the script finds it wherever it is defined: in this context's script or another's it is
/// written with. A name nothing defines there is refused when the script is written, and rules that leave a
/// function they need unnamed are refused here. What a role kept elsewhere gives a member is cut in these
/// functions as it is in C#: only a key the rules let a member's role give.
/// </para>
/// <para>
/// Roles kept for the resource (<see cref="MembershipRules.RolesKept"/>) are read from the table of the
/// application's role class, which the context maps with <c>IsKeptRole</c>: a role in use that holds the
/// key, cut the same way. No function is asked for them, and the functions read those rows as they read the
/// member rows, as their owner: which rows a caller may read of that table is the application's own rule,
/// and the functions do not lean on it. The row of the owner's role, made from the starter role the rules
/// name as the owner's, is kept in use by a trigger on that table, which refuses to archive it whoever writes
/// it, as the role refuses in C#; and the starter role a row was made from is fixed once the row is there.
/// </para>
/// <para>
/// The application's own work in a scope the rules name (<see cref="MembershipRules.SystemScopes"/>) is
/// answered every resource by <c>seen</c> and <c>held on</c>, as it is in C#: its claims say the role scoped
/// work runs as, and that scope. It asks them only where the rules let that role
/// (<see cref="RowAccessRoles.SystemIn"/> among <see cref="MembershipRules.GrantTo"/>). Work in any other
/// scope is nobody's member, and is answered nothing.
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
/// only edits a resource could give itself a role on it, or make itself its owner. So rules name the keys
/// the application's commands require for that, and the lock is written from them, for the database roles
/// the rules name (<see cref="MembershipRules.GrantTo"/>):
/// </para>
/// <list type="bullet">
/// <item><see cref="MembershipRules.ChangeMembersKey"/>: a restrictive policy on both member tables for
/// adding, changing and removing a row. A row is written only on a resource the caller holds that key on, or
/// the key that changes the owner, where the rules name one: naming an owner writes member rows too. It
/// narrows what the application's rules allow and allows nothing itself. Where the roles are kept for the
/// resource, a second one on the table of the roles a member holds, for adding and changing a row: the role
/// is one the caller sees in the role table, asked as the caller, so the application's own rules on that
/// table decide it. The functions find a role by its id, whoever's it is; this keeps a role the caller does
/// not see, another customer's, off a member list it writes.</item>
/// <item><see cref="MembershipRules.ChangeOwnerKey"/>: a trigger on the resource's table that refuses, as a
/// policy does, a statement that would leave a row with another owner, unless the caller held that key on
/// the resource before.</item>
/// </list>
/// <para>
/// The lock asks what the caller holds when each statement runs, as every policy does, and a change of a
/// resource's members is several statements. Two things follow. A resource is opened with its owner on the
/// list, and those first rows pass the lock like any other: an owner holds every key the rules state
/// (<see cref="MembershipRules.Keys"/>), so the key that changes the members is one of them wherever a
/// caller opens a resource as itself. And an owner that hands its own resource on, holding the keys by
/// owning it alone, holds them no longer once the owner column has changed: the rows written after that are
/// not its to write, under the lock or under an application's own rule that asks what the caller holds.
/// Somebody who holds the key through a role, or from above, hands a resource on as itself; an owner's own
/// hand-over is saved as the application's own work, once the command's check let it through.
/// </para>
/// <para>
/// The lock holds the database roles that can hold a key, and no other. A role the rules do not name cannot
/// ask the functions, so it holds no key and nothing is written for it: where a rule of the application's
/// lets such a role change the resource, that role writes the member rows and the owner column as that rule
/// lets it. The application's own work and the role that owns the tables are not held either.
/// </para>
/// <para>
/// The lock is for a resource the
/// application gave row access rules: on a resource without any, the policies would be the only ones on
/// the member tables, and nobody would read a row of them. Rules that name neither key leave the tables
/// to the resource's own rules, and the start-up check says so
/// (<see cref="MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync"/>).
/// </para>
/// <para>
/// A context that does not map the member class is none of its business, so an application with several
/// kinds of resource lists one class for each, and each writes for the context of its own resource.
/// </para>
/// </remarks>
/// <typeparam name="TMember">The application's member class of the resource.</typeparam>
public class MembershipRowAccessContribution<TMember> : IRowAccessContribution
    where TMember : class
{
    /// <summary>The functions of the resource whose members are of <typeparamref name="TMember"/>, written from <paramref name="rules"/>.</summary>
    /// <param name="rules">The resource's rules: the ones it is registered with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    public MembershipRowAccessContribution(MembershipRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Rules = rules;
        Owner = rules.Name.Replace('.', '-');
    }

    /// <summary>The rules the functions are written from.</summary>
    public MembershipRules Rules { get; }

    /// <inheritdoc />
    /// <remarks>The rules' name, a dot in it written as a dash: what a question an application declares for these functions names as its owner.</remarks>
    public string Owner { get; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="export"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The rules have the application answer something and name no function for it; they let the resource be
    /// reached from above, and the model does not say where it sits; they declare the roles, and the members
    /// hold roles that are not <see cref="NamedRole"/>; they say the roles are kept, and the model maps no
    /// role class for the resource, or one a member does not hold by its id; or the model stores a member, a
    /// moment of a period or a role's keys as something the functions cannot compare; or the rules name the
    /// key that changes the owner and a role the lock cannot be written for; or a trigger's name would be
    /// longer than Postgres keeps one.
    /// </exception>
    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(export);

        if (MembershipModel.Of(context.Model, typeof(TMember)) is not { } mapping)
        {
            return null;
        }

        // Where the functions are: the context's default schema, as every contributed function's is.
        var schema = context.Model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema;
        return new RowAccessContributionResult(
            MembershipSql.Functions(mapping, Rules, Owner, export.Roles.SystemIn),
            MembershipSql.Policies(mapping, Rules, Owner),
            MembershipSql.Statements(mapping, Rules, Owner, schema, export.Roles));
    }
}
