using DDDToolkit.EntityFramework.Postgres;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// How Entity Framework migrations are turned into Supabase migration files.
/// </summary>
public sealed class SupabaseMigrationOptions
{
    /// <summary>The schema Supabase exposes through its Data API unless a project says otherwise.</summary>
    public const string PublicSchema = "public";

    private ISet<string> _rowLevelSecuritySchemas = new HashSet<string>(StringComparer.Ordinal) { PublicSchema };

    private RowAccessRoleNames _roles = RowAccessRoleNames.Default;

    private PostgresCallerFunctions _callerFunctions = SupabaseRowLevelSecurity.CallerFunctions;

    /// <summary>
    /// The schemas whose new tables get <c>ENABLE ROW LEVEL SECURITY</c> appended to the migration that
    /// creates them. <c>public</c> by default.
    /// <para>
    /// Supabase serves every table in an exposed schema over its Data API, and grants the <c>anon</c> and
    /// <c>authenticated</c> roles access to it. A table Entity Framework creates there without row level
    /// security is therefore readable and writable by anyone holding the project's publishable key, which
    /// is why Supabase's security advisor reports it as an error. With row level security on and no
    /// policy, those roles see nothing, while the application keeps working: it connects as the table's
    /// owner, and an owner is not subject to row level security unless the table forces it.
    /// </para>
    /// <para>
    /// A table created without a schema lands in the connection's <c>search_path</c>, which on Supabase is
    /// <c>public</c>, so such tables are treated as <c>public</c>. Clear the set to turn this off, for
    /// instance when every table lives in a schema the Data API does not expose.
    /// </para>
    /// </summary>
    public ISet<string> RowLevelSecuritySchemas
    {
        get => _rowLevelSecuritySchemas;
        set => _rowLevelSecuritySchemas = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The <c>[RowAccess]</c> rules to write as policies. Each context gets the rules of the aggregates its
    /// model maps, in a file of its module's own, <c>{version}_access.{module}.ddd.sql</c>, written again
    /// whenever the rules change or a migration of the module comes after the last one. The build fills
    /// this in with every rule the modules declare; set it yourself to export from a test or a tool.
    /// </summary>
    public IList<RowAccessRule> RowAccessRules { get; } = [];

    /// <summary>
    /// The <c>[AccessFunction]</c>s the rules call. Each goes into the access file of the context that maps
    /// its aggregate, and nowhere else, before that file's policies; a context is exported before every context
    /// that asks one of its functions, so its file comes before theirs. The build fills this in as it fills
    /// <see cref="RowAccessRules"/>.
    /// </summary>
    public IList<RowAccessFunction> RowAccessFunctions { get; } = [];

    /// <summary>
    /// The row access contributions to ask, for every context, what they write into its access file: SQL
    /// functions, policies and statements of a package or a module. A context whose only row access is a
    /// contribution gets an access file too, and its migrations start by taking its policies off. The build
    /// fills this in with the contributions the host lists with <c>[assembly: UseRowAccessContribution]</c>,
    /// and no others; see <see cref="IRowAccessContribution"/>.
    /// </summary>
    public IList<IRowAccessContribution> RowAccessContributions { get; } = [];

    /// <summary>
    /// The database roles the rules' symbolic roles are written as: <c>RowAccessRoles.User</c>,
    /// <c>Anonymous</c> and <c>SystemIn</c>, and <c>RowAccessRoles.Token(...)</c> for each token role in
    /// <see cref="RowAccessRoleNames.TokenRoles"/>, and <c>RowAccessRoles.System</c> for the bookkeeping role,
    /// <see cref="RowAccessRoleNames.System"/>, where one is set. <see cref="RowAccessRoleNames.Default"/> by
    /// default, Supabase's <c>authenticated</c> and <c>anon</c>, and <c>ddd_system_in</c>, with no token role
    /// mapped and no bookkeeping role. The build takes them from the <c>SupabaseRowAccessRoles</c> property of
    /// the project that runs the export.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public RowAccessRoleNames Roles
    {
        get => _roles;
        set => _roles = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// How a policy asks about the caller: <see cref="SupabaseRowLevelSecurity.CallerFunctions"/> by default,
    /// <c>auth.uid()</c>, <c>auth.role()</c> and <c>auth.jwt()</c>. The build takes them from the
    /// <c>SupabaseCallerFunctions</c> property of the project that runs the export.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public PostgresCallerFunctions CallerFunctions
    {
        get => _callerFunctions;
        set => _callerFunctions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Whether the access files also write the privileges of the tables they write policies for, read from
    /// those policies, and of each module's outbox, inbox and event log: <see cref="RowAccessExport.WriteGrants"/>.
    /// <see langword="false"/> by default, and then every access file is what it was. A module with an outbox,
    /// an inbox or an event log and no rule gets an access file for them once this is on. The build takes it
    /// from the <c>SupabaseRowAccessGrants</c> property of the project that runs the export, <c>Write</c> or
    /// <c>None</c>.
    /// </summary>
    public bool WriteGrants { get; set; }

    /// <summary>
    /// Whether every table an access file turns row level security on for gets it forced as well, so the
    /// table's owner is held to the policies too: <see cref="RowAccessExport.ForceRowLevelSecurity"/>.
    /// <see langword="false"/> by default, and then every access file is what it was. It is about the access
    /// files alone: a migration's own <c>ENABLE ROW LEVEL SECURITY</c> for a new table of an exposed schema
    /// (<see cref="RowLevelSecuritySchemas"/>) stays as it is, since an exported migration is never rewritten.
    /// The build takes it from the <c>SupabaseForceRowLevelSecurity</c> property of the project that runs the
    /// export.
    /// </summary>
    public bool ForceRowLevelSecurity { get; set; }

    /// <summary>The clock a new access file takes its version from: the time it is written, in UTC.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
