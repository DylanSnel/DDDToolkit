using System.ComponentModel;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The export the build runs. The build step starts the freshly built application with
/// <see cref="ModeVariable"/> set; a module initializer the generator wrote into it calls
/// <see cref="RunIfRequested(Func{IReadOnlyList{SupabaseMigrationSource}}, Func{IReadOnlyList{RowAccessRule}}, Func{IReadOnlyList{RowAccessFunction}}, Func{IReadOnlyList{IRowAccessContribution}})">RunIfRequested</see>
/// before <c>Main</c>, which exports every source it was handed and ends the
/// process. The application's own start-up, its configuration included, never runs.
/// <para>
/// Without the variable nothing happens, which is every start of the application outside that build step.
/// </para>
/// <para>
/// Because the export runs before <c>Main</c>, the host's configuration reaches it only through the
/// environment the build sets: the roles and the caller functions the policies are written with come from
/// <see cref="RolesVariable"/> and <see cref="CallerFunctionsVariable"/>, which the build fills from the
/// project's <c>SupabaseRowAccessRoles</c> and <c>SupabaseCallerFunctions</c> properties. The roles of the
/// token roles the host mapped come the same way, as <c>token:&lt;role&gt;</c> pairs of the first, and so does
/// the role its own bookkeeping runs as, <c>system=&lt;role&gt;</c>. Whether the access files write the tables'
/// privileges and force row level security comes from <see cref="GrantsVariable"/> and
/// <see cref="ForceVariable"/>, the project's <c>SupabaseRowAccessGrants</c> and
/// <c>SupabaseForceRowLevelSecurity</c>. The role the application logs in as, whose migration the export writes
/// where the project names one, comes from <see cref="LoginRoleVariable"/>, the project's <c>SupabaseLoginRole</c>.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class SupabaseMigrationBuild
{
    /// <summary><c>Write</c> writes missing files; <c>Check</c> only compares. Unset means do nothing.</summary>
    public const string ModeVariable = "DDDTOOLKIT_SUPABASE_EXPORT";

    /// <summary>The migrations directory. Unset means find it upwards from <see cref="StartVariable"/>.</summary>
    public const string DirectoryVariable = "DDDTOOLKIT_SUPABASE_DIRECTORY";

    /// <summary>Where to start looking for <c>supabase/config.toml</c>: the project directory.</summary>
    public const string StartVariable = "DDDTOOLKIT_SUPABASE_START";

    /// <summary>
    /// The roles the rules' symbolic roles become, from the <c>SupabaseRowAccessRoles</c> property:
    /// <c>user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system</c>, each pair optional, and a
    /// pair <c>token:analyst=desk_analyst</c> for each token role the host mapped, which is what a rule for
    /// <c>RowAccessRoles.Token("analyst")</c> is written for. <c>system</c> is the role the application's own
    /// bookkeeping runs as, <c>RowAccessRoles.System</c>; <c>none</c> there, or one of the platform's roles such as
    /// <c>service_role</c>, leaves the files without a bookkeeping role. A pair left out is
    /// <see cref="SupabaseRowLevelSecurity.DefaultRoles"/>'s, with no token role mapped.
    /// </summary>
    public const string RolesVariable = "DDDTOOLKIT_SUPABASE_ROLES";

    /// <summary>
    /// How a policy asks about the caller, from the <c>SupabaseCallerFunctions</c> property:
    /// <c>uid=auth.uid()|role=auth.role()|claims=auth.jwt()</c>, each pair optional and each value a function
    /// called without arguments. Unset means <see cref="SupabaseRowLevelSecurity.CallerFunctions"/>.
    /// </summary>
    public const string CallerFunctionsVariable = "DDDTOOLKIT_SUPABASE_CALLER_FUNCTIONS";

    /// <summary>
    /// Whether the access files write the tables' privileges from the policies, from the
    /// <c>SupabaseRowAccessGrants</c> property: <c>Write</c> or unset writes them, <c>None</c> leaves them to
    /// the host. See <see cref="SupabaseMigrationOptions.WriteGrants"/>.
    /// </summary>
    public const string GrantsVariable = "DDDTOOLKIT_SUPABASE_GRANTS";

    /// <summary>
    /// Whether the access files force row level security on every table they turn it on for, from the
    /// <c>SupabaseForceRowLevelSecurity</c> property: <c>true</c> or unset forces it, <c>false</c> does not. See
    /// <see cref="SupabaseMigrationOptions.ForceRowLevelSecurity"/>.
    /// </summary>
    public const string ForceVariable = "DDDTOOLKIT_SUPABASE_FORCE";

    /// <summary>
    /// The role the application logs in as, from the <c>SupabaseLoginRole</c> property: the export writes the
    /// migration that makes it, a member of the roles <see cref="RolesVariable"/> names and of nothing else.
    /// Unset means no such migration. See <see cref="SupabaseMigrationOptions.LoginRole"/>.
    /// </summary>
    public const string LoginRoleVariable = "DDDTOOLKIT_SUPABASE_LOGIN_ROLE";

    /// <summary>The project property <see cref="RolesVariable"/> comes from, as an error names it.</summary>
    private const string RolesProperty = SupabaseCallerRoles.Property;

    /// <summary>The project property <see cref="CallerFunctionsVariable"/> comes from, as an error names it.</summary>
    private const string CallerFunctionsProperty = "SupabaseCallerFunctions";

    /// <summary>The project property <see cref="GrantsVariable"/> comes from, as an error names it.</summary>
    private const string GrantsProperty = "SupabaseRowAccessGrants";

    /// <summary>The project property <see cref="ForceVariable"/> comes from, as an error names it.</summary>
    private const string ForceProperty = "SupabaseForceRowLevelSecurity";

    /// <summary>The project property <see cref="LoginRoleVariable"/> comes from, as an error names it.</summary>
    private const string LoginRoleProperty = "SupabaseLoginRole";

    /// <summary>
    /// Exports and ends the process when the build asked for it, and returns at once otherwise. Called
    /// by generated code only.
    /// </summary>
    /// <param name="sources">Every source found at compile time; only evaluated when asked.</param>
    public static void RunIfRequested(Func<IReadOnlyList<SupabaseMigrationSource>> sources)
        => RunIfRequested(sources, static () => []);

    /// <summary>
    /// Exports and ends the process when the build asked for it, and returns at once otherwise: the
    /// migrations of every source and the <c>[RowAccess]</c> rules of their aggregates. Called by generated
    /// code only.
    /// </summary>
    /// <param name="sources">Every source found at compile time; only evaluated when asked.</param>
    /// <param name="rules">Every rule found at compile time; only evaluated when asked.</param>
    public static void RunIfRequested(Func<IReadOnlyList<SupabaseMigrationSource>> sources, Func<IReadOnlyList<RowAccessRule>> rules)
        => RunIfRequested(sources, rules, static () => []);

    /// <summary>
    /// Exports and ends the process when the build asked for it, and returns at once otherwise: the
    /// migrations of every source, the <c>[RowAccess]</c> rules of their aggregates and the
    /// <c>[AccessFunction]</c>s those call. Called by generated code only.
    /// </summary>
    /// <param name="sources">Every source found at compile time; only evaluated when asked.</param>
    /// <param name="rules">Every rule found at compile time; only evaluated when asked.</param>
    /// <param name="functions">Every access function found at compile time; only evaluated when asked.</param>
    public static void RunIfRequested(
        Func<IReadOnlyList<SupabaseMigrationSource>> sources,
        Func<IReadOnlyList<RowAccessRule>> rules,
        Func<IReadOnlyList<RowAccessFunction>> functions)
        => RunIfRequested(sources, rules, functions, static () => []);

    /// <summary>
    /// Exports and ends the process when the build asked for it, and returns at once otherwise: the
    /// migrations of every source, the <c>[RowAccess]</c> rules of their aggregates, the
    /// <c>[AccessFunction]</c>s those call, and what the row access contributions the host uses write. Called
    /// by generated code only.
    /// </summary>
    /// <param name="sources">Every source found at compile time; only evaluated when asked.</param>
    /// <param name="rules">Every rule found at compile time; only evaluated when asked.</param>
    /// <param name="functions">Every access function found at compile time; only evaluated when asked.</param>
    /// <param name="contributions">
    /// The contributions the packages the host references write, made from what the application marks, and those
    /// the host lists with <c>[assembly: UseRowAccessContribution]</c>, created only when
    /// asked.
    /// </param>
    public static void RunIfRequested(
        Func<IReadOnlyList<SupabaseMigrationSource>> sources,
        Func<IReadOnlyList<RowAccessRule>> rules,
        Func<IReadOnlyList<RowAccessFunction>> functions,
        Func<IReadOnlyList<IRowAccessContribution>> contributions)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(contributions);

        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        var exitCode = Run(
            mode,
            sources(),
            rules(),
            functions(),
            contributions(),
            Environment.GetEnvironmentVariable(DirectoryVariable),
            Environment.GetEnvironmentVariable(StartVariable),
            Environment.GetEnvironmentVariable(RolesVariable),
            Environment.GetEnvironmentVariable(CallerFunctionsVariable),
            Environment.GetEnvironmentVariable(GrantsVariable),
            Environment.GetEnvironmentVariable(ForceVariable),
            Environment.GetEnvironmentVariable(LoginRoleVariable),
            Console.Out);

        Console.Out.Flush();
        Environment.Exit(exitCode);
    }

    /// <summary>
    /// Runs the export and describes it on <paramref name="output"/>, problems in the
    /// <c>error : message</c> form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(string mode, IReadOnlyList<SupabaseMigrationSource> sources, string? directory, string? start, TextWriter output)
        => Run(mode, sources, [], directory, start, output);

    /// <summary>
    /// Runs the export, row access rules included, and describes it on <paramref name="output"/>, problems in
    /// the <c>error : message</c> form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(string mode, IReadOnlyList<SupabaseMigrationSource> sources, IReadOnlyList<RowAccessRule> rules, string? directory, string? start, TextWriter output)
        => Run(mode, sources, rules, [], directory, start, output);

    /// <summary>
    /// Runs the export, row access rules and the access functions they call included, and describes it on
    /// <paramref name="output"/>, problems in the <c>error : message</c> form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="functions">The access functions to write, each with the context that maps its aggregate.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(
        string mode,
        IReadOnlyList<SupabaseMigrationSource> sources,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction> functions,
        string? directory,
        string? start,
        TextWriter output)
        => Run(mode, sources, rules, functions, directory, start, roles: null, callerFunctions: null, output);

    /// <summary>
    /// Runs the export with the roles and the caller functions the build passed, and describes it on
    /// <paramref name="output"/>, problems in the <c>error : message</c> form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="functions">The access functions to write, each with the context that maps its aggregate.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="roles">The <see cref="RolesVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="callerFunctions">The <see cref="CallerFunctionsVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(
        string mode,
        IReadOnlyList<SupabaseMigrationSource> sources,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction> functions,
        string? directory,
        string? start,
        string? roles,
        string? callerFunctions,
        TextWriter output)
        => Run(mode, sources, rules, functions, [], directory, start, roles, callerFunctions, output);

    /// <summary>
    /// Runs the export with the row access contributions the host uses and the roles and the caller functions
    /// the build passed, and describes it on <paramref name="output"/>, problems in the <c>error : message</c>
    /// form MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="functions">The access functions to write, each with the context that maps its aggregate.</param>
    /// <param name="contributions">The row access contributions to ask what they write for each context.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="roles">The <see cref="RolesVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="callerFunctions">The <see cref="CallerFunctionsVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(
        string mode,
        IReadOnlyList<SupabaseMigrationSource> sources,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction> functions,
        IReadOnlyList<IRowAccessContribution> contributions,
        string? directory,
        string? start,
        string? roles,
        string? callerFunctions,
        TextWriter output)
        => Run(mode, sources, rules, functions, contributions, directory, start, roles, callerFunctions, grants: null, force: null, output);

    /// <summary>
    /// Runs the export with everything the build passed: the row access contributions the host uses, the roles
    /// and the caller functions, and whether the access files write the tables' privileges and force row level
    /// security. It describes the run on <paramref name="output"/>, problems in the <c>error : message</c> form
    /// MSBuild shows as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="functions">The access functions to write, each with the context that maps its aggregate.</param>
    /// <param name="contributions">The row access contributions to ask what they write for each context.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="roles">The <see cref="RolesVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="callerFunctions">The <see cref="CallerFunctionsVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="grants">The <see cref="GrantsVariable"/>'s value: <c>Write</c>, null or empty to write the privileges, or <c>None</c> to write none.</param>
    /// <param name="force">The <see cref="ForceVariable"/>'s value: <c>true</c>, null or empty to force row level security, or <c>false</c> not to.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(
        string mode,
        IReadOnlyList<SupabaseMigrationSource> sources,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction> functions,
        IReadOnlyList<IRowAccessContribution> contributions,
        string? directory,
        string? start,
        string? roles,
        string? callerFunctions,
        string? grants,
        string? force,
        TextWriter output)
        => Run(mode, sources, rules, functions, contributions, directory, start, roles, callerFunctions, grants, force, loginRole: null, output);

    /// <summary>
    /// Runs the export with everything the build passed: the row access contributions the host uses, the roles
    /// and the caller functions, whether the access files write the tables' privileges and force row level
    /// security, and the role the application logs in as, whose migration it writes after every other file. It
    /// describes the run on <paramref name="output"/>, problems in the <c>error : message</c> form MSBuild shows
    /// as build errors.
    /// </summary>
    /// <param name="mode"><c>Write</c> or <c>Check</c>.</param>
    /// <param name="sources">The contexts to export.</param>
    /// <param name="rules">The rules to write as policies, each with the context that maps its aggregate.</param>
    /// <param name="functions">The access functions to write, each with the context that maps its aggregate.</param>
    /// <param name="contributions">The row access contributions to ask what they write for each context.</param>
    /// <param name="directory">The migrations directory, or null or empty to find it.</param>
    /// <param name="start">Where to start looking when <paramref name="directory"/> is not given.</param>
    /// <param name="roles">The <see cref="RolesVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="callerFunctions">The <see cref="CallerFunctionsVariable"/>'s value, or null or empty for the defaults.</param>
    /// <param name="grants">The <see cref="GrantsVariable"/>'s value: <c>Write</c>, null or empty to write the privileges, or <c>None</c> to write none.</param>
    /// <param name="force">The <see cref="ForceVariable"/>'s value: <c>true</c>, null or empty to force row level security, or <c>false</c> not to.</param>
    /// <param name="loginRole">The <see cref="LoginRoleVariable"/>'s value: the role the application logs in as, or null or empty to write no migration for it.</param>
    /// <param name="output">Where to report.</param>
    /// <returns>0 when everything is in sync, 1 when something needs attention, 2 when the export could not run.</returns>
    public static int Run(
        string mode,
        IReadOnlyList<SupabaseMigrationSource> sources,
        IReadOnlyList<RowAccessRule> rules,
        IReadOnlyList<RowAccessFunction> functions,
        IReadOnlyList<IRowAccessContribution> contributions,
        string? directory,
        string? start,
        string? roles,
        string? callerFunctions,
        string? grants,
        string? force,
        string? loginRole,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(output);

        var write = string.Equals(mode, "Write", StringComparison.OrdinalIgnoreCase);
        if (!write && !string.Equals(mode, "Check", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"error : SupabaseMigrationsExport is '{mode}'. Use Write to write missing files, or Check to only compare them.");
            return 2;
        }

        var options = new SupabaseMigrationOptions();
        if (!TryConfigure(options, roles, callerFunctions, grants, force, out var callerRoles, out var problem)
            || !TryConfigureLoginRole(options, loginRole, out problem))
        {
            output.WriteLine($"error : {problem}");
            return 2;
        }

        if (BookkeepingRoleNobodyMakes(options, callerRoles) is { } unmade)
        {
            output.WriteLine($"warning : {unmade}");
        }

        if (sources.Count == 0)
        {
            output.WriteLine("warning : No factory marked [SupabaseMigrations] is referenced by this project, so there is nothing to export.");
            return 0;
        }

        try
        {
            var target = string.IsNullOrWhiteSpace(directory)
                ? SupabaseMigrations.FindDirectory(string.IsNullOrWhiteSpace(start) ? null : start)
                : directory;

            foreach (var rule in rules)
            {
                options.RowAccessRules.Add(rule);
            }

            foreach (var function in functions)
            {
                options.RowAccessFunctions.Add(function);
            }

            foreach (var contribution in contributions)
            {
                options.RowAccessContributions.Add(contribution);
            }

            var reports = write ? SupabaseMigrations.Export(sources, target, options) : SupabaseMigrations.Compare(sources, target, options);

            foreach (var entry in reports.SelectMany(report => report.Entries))
            {
                output.WriteLine($"Supabase migrations: {entry.Status,-12} {Path.GetFileName(entry.Path)}");
            }

            if (reports.All(report => report.IsInSync))
            {
                return 0;
            }

            var problems = new SupabaseMigrationsOutOfSyncException(reports).Message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in problems.Skip(1))
            {
                output.WriteLine($"error : {line.Trim()}");
            }

            if (!write)
            {
                output.WriteLine("error : This build only checks. Build it locally, where SupabaseMigrationsExport is Write, and commit the files it writes.");
            }

            return 1;
        }
        catch (Exception exception)
        {
            output.WriteLine($"error : The Supabase migrations could not be exported: {exception.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Sets <paramref name="options"/>' roles and caller functions, and whether the access files write
    /// privileges and force row level security, from the build's properties, or says what is wrong with them. The
    /// roles are read as <c>AddSupabaseRowLevelSecurity</c> reads what the build recorded of them, so the export and
    /// the application take the same defaults.
    /// </summary>
    private static bool TryConfigure(SupabaseMigrationOptions options, string? roles, string? callerFunctions, string? grants, string? force, out SupabaseCallerRoles callerRoles, out string problem)
    {
        if (!SupabaseCallerRoles.TryParse(roles, out callerRoles, out problem)
            || !TryReadPairs(CallerFunctionsProperty, callerFunctions, ["uid", "role", "claims"], "uid=auth.uid()|role=auth.role()|claims=auth.jwt()", prefix: null, out var caller, out _, out problem))
        {
            return false;
        }

        // Unset, each keeps the options' default, which writes the privileges and forces row level security: a
        // project says only what it turns off.
        switch (grants?.Trim())
        {
            case null or "":
                break;
            case var none when string.Equals(none, "None", StringComparison.OrdinalIgnoreCase):
                options.WriteGrants = false;
                break;
            case var written when string.Equals(written, "Write", StringComparison.OrdinalIgnoreCase):
                options.WriteGrants = true;
                break;
            default:
                problem = $"{GrantsProperty} is '{grants}'. Use Write, the default, to have the access files write the tables' privileges from the policies, or None to grant them yourself.";
                return false;
        }

        switch (force?.Trim())
        {
            case null or "":
                break;
            case var off when string.Equals(off, "false", StringComparison.OrdinalIgnoreCase):
                options.ForceRowLevelSecurity = false;
                break;
            case var on when string.Equals(on, "true", StringComparison.OrdinalIgnoreCase):
                options.ForceRowLevelSecurity = true;
                break;
            default:
                problem = $"{ForceProperty} is '{force}'. Use true, the default, to force row level security on every table an access file turns it on for, or false to leave the tables' owner outside the policies.";
                return false;
        }

        options.Roles = callerRoles.Names;

        foreach (var (key, function) in caller)
        {
            if (!CallWithoutArguments().IsMatch(function))
            {
                problem = $"{CallerFunctionsProperty} has '{key}={function}', which is not a function called without arguments. Each value is one, with its schema or without, such as auth.uid(), and the policies call it as it is written.";
                return false;
            }
        }

        options.CallerFunctions = new PostgresCallerFunctions(
            caller.GetValueOrDefault("uid") ?? options.CallerFunctions.UserId,
            caller.GetValueOrDefault("role") ?? options.CallerFunctions.Role,
            caller.GetValueOrDefault("claims") ?? options.CallerFunctions.Claims);

        return true;
    }

    /// <summary>
    /// What the build says where the access files write no privileges and <c>SupabaseRowAccessRoles</c> leaves the
    /// system caller its default, the bookkeeping role <c>ddd_system</c>: no access file then makes that role or gives
    /// it the toolkit's tables, unless a rule is for it, so the application's system caller would switch to a role the
    /// files never made. <see langword="null"/> where there is nothing to say.
    /// </summary>
    private static string? BookkeepingRoleNobodyMakes(SupabaseMigrationOptions options, SupabaseCallerRoles roles)
        => options.WriteGrants || roles.SystemSaid || roles.Names.System is not { } system
            ? null
            : $"{GrantsProperty} is None, so no access file makes the bookkeeping role {system} or gives it the outbox, the inbox and the migration history, and the system caller switches to it unless {RolesProperty} says otherwise. " +
              $"Add system={SupabaseCallerRoles.LoginRole} to {RolesProperty} where the system caller runs as the role the application logs in as, or system=<role> for a role a migration of your own makes and grants.";

    /// <summary>
    /// Sets <paramref name="options"/>' login role from the build's property, once the roles callers run as are
    /// set, or says what is wrong with it: a name the migration cannot write as it is, one of Postgres's or
    /// Supabase's own roles, or one of the roles callers run as. White space is no login role at all.
    /// </summary>
    private static bool TryConfigureLoginRole(SupabaseMigrationOptions options, string? loginRole, out string problem)
    {
        problem = "";
        if (string.IsNullOrWhiteSpace(loginRole))
        {
            return true;
        }

        var login = loginRole.Trim();
        if (SupabaseMigrations.NotALoginRole(login) is { } notALogin)
        {
            problem = $"{LoginRoleProperty} is '{login}'. {notALogin}";
            return false;
        }

        if (SupabaseMigrations.CallerRoleNamed(login, options.Roles) is { } key)
        {
            problem = $"{LoginRoleProperty} is '{login}', the role of {key} in {RolesProperty}: a role callers run as. " +
                      "The role the application logs in as only switches to those, and is a role of its own; name another, such as sample_api.";
            return false;
        }

        options.LoginRole = login;
        return true;
    }

    /// <summary>
    /// The message of <paramref name="exception"/> without the name of the parameter .NET appends to it,
    /// which means nothing in a build's error line.
    /// </summary>
    internal static string Reason(ArgumentException exception)
    {
        // The suffix is whatever the runtime appends for that parameter, in whatever language it speaks.
        var suffix = new ArgumentException("", exception.ParamName).Message;
        return exception.Message.EndsWith(suffix, StringComparison.Ordinal) ? exception.Message[..^suffix.Length] : exception.Message;
    }

    /// <summary>
    /// A function called without arguments, with its schema or without: <c>auth.uid()</c>. A policy writes a
    /// caller function into its condition as it is, so anything else could change what the condition says.
    /// </summary>
    [GeneratedRegex(@"^(?:[A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*\(\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CallWithoutArguments();

    /// <summary>
    /// The <c>key=value</c> pairs of a build property, separated by <c>|</c>, because MSBuild splits the
    /// variables it passes at every <c>;</c>, so no value can hold a <c>|</c>. Empty means none; an unknown or
    /// repeated key, or a pair without a key or a value, is a problem that names the property and what it
    /// takes.
    /// </summary>
    /// <param name="property">The property, as a problem names it.</param>
    /// <param name="value">Its value.</param>
    /// <param name="keys">The keys it takes, matched without regard to case.</param>
    /// <param name="example">A value that sets every key, for a problem to show.</param>
    /// <param name="prefix">
    /// What the key of a pair with a name of its own starts with, <c>token:</c>, or null when the property has
    /// none. What follows it is the name, kept as it is spelled.
    /// </param>
    /// <param name="pairs">The pairs of <paramref name="keys"/>.</param>
    /// <param name="prefixed">The pairs whose key starts with <paramref name="prefix"/>, by the name after it.</param>
    /// <param name="problem">What is wrong, when something is.</param>
    internal static bool TryReadPairs(
        string property,
        string? value,
        string[] keys,
        string example,
        string? prefix,
        out Dictionary<string, string> pairs,
        out Dictionary<string, string> prefixed,
        out string problem)
    {
        pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        prefixed = new Dictionary<string, string>(StringComparer.Ordinal);
        problem = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var takes = $"It takes {string.Join(", ", keys.Take(keys.Length - 1))} and {keys[^1]}, each at most once and separated by '|', as in {example}"
            + (prefix is null ? "." : $", and {prefix}<role> for each token role the host mapped, as in {prefix}analyst=desk_analyst.");
        foreach (var pair in value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals].Trim();
            var setting = equals < 0 ? "" : pair[(equals + 1)..].Trim();

            if (equals <= 0 || setting.Length == 0)
            {
                problem = $"{property} has '{pair}', which is not a key=value pair. {takes}";
                return false;
            }

            if (prefix is not null && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // The name after the prefix is a token's own spelling of its role, so its case is kept.
                var name = key[prefix.Length..].Trim();
                if (name.Length == 0)
                {
                    problem = $"{property} has '{pair}', which names no token role after '{prefix}'. {takes}";
                    return false;
                }

                if (!prefixed.TryAdd(name, setting))
                {
                    problem = $"{property} has the key '{key}' twice. {takes}";
                    return false;
                }

                continue;
            }

            if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                problem = $"{property} has the key '{key}', which it does not know. {takes}";
                return false;
            }

            if (!pairs.TryAdd(key, setting))
            {
                problem = $"{property} has the key '{key}' twice. {takes}";
                return false;
            }
        }

        return true;
    }
}
