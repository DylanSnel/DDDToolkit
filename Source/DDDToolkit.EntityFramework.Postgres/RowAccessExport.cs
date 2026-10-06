namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// What a script of policies is written with, beyond the rules themselves: how a policy asks about the
/// caller, and which database roles the rules' symbolic roles become. Every property has the default a
/// Postgres of your own needs; the Supabase export writes Supabase's.
/// <code>
/// var sql = PostgresRowAccess.Script(context, rules, functions, new RowAccessExport
/// {
///     Roles = RowAccessRoleNames.Of(rowLevelSecurityOptions),
/// });
/// </code>
/// </summary>
public sealed class RowAccessExport
{
    private readonly PostgresCallerFunctions _callerFunctions = PostgresCallerFunctions.Toolkit;

    private readonly RowAccessRoleNames _roles = RowAccessRoleNames.Default;

    /// <summary>
    /// How a policy asks about the caller, which is what a rule's <c>caller.UserId</c>, <c>caller.Role</c> and
    /// <c>caller.Claim(...)</c> become. <see cref="PostgresCallerFunctions.Toolkit"/> by default.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public PostgresCallerFunctions CallerFunctions
    {
        get => _callerFunctions;
        init => _callerFunctions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The database roles the rules' symbolic roles are written as. <see cref="RowAccessRoleNames.Default"/>
    /// by default: <c>authenticated</c>, <c>anon</c> and <c>ddd_system_in</c>, with no token role mapped. A host
    /// that maps token roles hands over <see cref="RowAccessRoleNames.Of"/> its options, so a rule for
    /// <c>RowAccessRoles.Token(...)</c> is written for the role the queries run as.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public RowAccessRoleNames Roles
    {
        get => _roles;
        init => _roles = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Where the functions rules ask by a logical name, <c>owner/name</c>, live when another context defines
    /// them: <c>"projects/is_member"</c> to <c>"projects.is_member"</c>. A script finds those of its own context
    /// itself, in the context's schema; <see cref="PostgresRowAccess.Scripts"/> and the Supabase export fill
    /// this in from every context they write, with <see cref="PostgresRowAccess.FunctionNamesOf"/>. Empty by
    /// default. A rule that asks a logical name neither defines is refused, naming the rule.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IReadOnlyDictionary<string, string> FunctionNames
    {
        get => _functionNames;
        init => _functionNames = value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly IReadOnlyDictionary<string, string> _functionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The contributions a script asks, for every context it writes, what they write there: functions,
    /// policies and statements of a package or a module of its own. Empty by default; the Supabase build fills
    /// it in with those the packages the host references write and those it lists with
    /// <c>[assembly: UseRowAccessContribution]</c>. See
    /// <see cref="IRowAccessContribution"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IReadOnlyList<IRowAccessContribution> Contributions
    {
        get => _contributions;
        init => _contributions = value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly IReadOnlyList<IRowAccessContribution> _contributions = [];

    /// <summary>
    /// Whether a script also writes the privileges of the tables it writes policies for, so the policies and
    /// the privileges cannot drift apart: a privilege beyond the policies only turns a refusal into an empty
    /// answer, and a privilege short of them turns a rule that allows into an error. <see langword="false"/>
    /// by default: a 3.x script wrote none, and a host that grants its tables' privileges itself keeps doing so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The privileges come after the policies. Every privilege on each such table is taken back first, from the
    /// user's, the anonymous caller's and the scoped system role, from the role of every mapped token role, from
    /// the bookkeeping role when one is configured, and from <c>PUBLIC</c>. Then a role gets each command a
    /// permissive policy for that role allows, table by table, and <c>USAGE</c> on the tables' schemas. A
    /// restrictive policy gives nothing: it only narrows. <c>UPDATE</c> is granted per column, on every column
    /// but those of a key and those Entity Framework would refuse to change after the row was added
    /// (<c>IsFixedAfterInsert()</c> in <c>DDDToolkit.EntityFramework</c>, a discriminator, a column the database
    /// computes). A role that may add rows gets <c>USAGE</c> on the sequences the table's columns own. A role
    /// the policies let change or remove rows of a table and not read them is refused when the script is
    /// written: Entity Framework finds the row by reading it, so such a rule could never run.
    /// </para>
    /// <para>
    /// The toolkit's own tables have no policies, and get what their use asks. An outbox table and an event log
    /// take <c>INSERT</c> from the user's role, the scoped system role and every role the policies let write a
    /// table of the context, since a save writes the event's rows in the same transaction, and nothing else. An
    /// inbox table gives <c>SELECT</c> and <c>INSERT</c> to the scoped system role, which is what the handlers
    /// of integration events run as. Where <see cref="RowAccessRoleNames.System"/> is set, that role gets what
    /// the toolkit's bookkeeping does: reading, marking and deleting outbox rows, all of an inbox table, reading
    /// the context's migration history, and deleting the rows of an event log that may go. A table of these
    /// that a contribution writes policies for follows its policies instead, like any other.
    /// </para>
    /// <para>
    /// The user's and the anonymous caller's role have to exist where the script runs, as they do on Supabase
    /// and after <see cref="PostgresRowAccess.SetupScript"/>; the script makes the others it names.
    /// </para>
    /// </remarks>
    public bool WriteGrants { get; init; }

    /// <summary>
    /// Whether every table a script turns row level security on for gets it forced as well, <c>FORCE ROW LEVEL
    /// SECURITY</c> after each <c>ENABLE</c>: the tables of the rules, of the aggregates' entities and of the
    /// contributions. A table's owner is then held to its policies too, unless the owner may bypass row level
    /// security. <see langword="false"/> by default, and then a script is byte for byte what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it, Postgres exempts a table's owner from the table's policies, so whoever reaches the owning
    /// role reads and writes every row. With it, the only way past the policies is a role with
    /// <c>BYPASSRLS</c>, or a superuser.
    /// </para>
    /// <para>
    /// The access functions run as their owner so that they answer without the policies of the tables they
    /// read. On a forced table that only holds while the owner may bypass row level security, as the role
    /// Supabase runs migrations as may: <see cref="PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync"/>
    /// checks it at start-up. A contribution that turns row level security on in a statement of its own reads
    /// this flag from the export it is handed. Turning it off again does not take the force off a table that
    /// has it: <c>ALTER TABLE … NO FORCE ROW LEVEL SECURITY</c> does, in a migration of your own.
    /// </para>
    /// </remarks>
    public bool ForceRowLevelSecurity { get; init; }
}
