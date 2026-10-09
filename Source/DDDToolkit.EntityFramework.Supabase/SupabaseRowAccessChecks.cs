using System.Text;
using System.Transactions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Supabase;

/// <summary>
/// The start-up check of what is Supabase's about row level security: that the roles the application switches to are
/// the ones the access files in its database were written for. <c>AddSupabaseRowLevelSecurity</c> registers it over
/// every registered context on Postgres that runs as its caller, which a host runs with
/// <c>services.RunStartupChecks()</c>; the method stays for a host that runs it by hand.
/// <code>
/// await SupabaseRowAccessChecks.EnsureRolesMatchAccessFilesAsync(context, cancellationToken);
/// </code>
/// <para>
/// The roles are said in two places when the project that exports is not the host: the exporter's
/// <c>SupabaseRowAccessRoles</c> writes the policies, and the host's <c>AddSupabaseRowLevelSecurity</c>, from its own
/// project file or its code, switches to roles. Every access file records the roles it was written for on the
/// <c>ddd</c> schema, as its comment, and this compares the two, so a host that differs stops before its first request
/// rather than finding nothing, or <c>permission denied</c>, at the first query of the caller whose role differs.
/// </para>
/// </summary>
public static class SupabaseRowAccessChecks
{
    /// <summary>
    /// The start-up check that the roles the application switches to are the ones the access files in its database were
    /// written for (<see cref="EnsureRolesMatchAccessFilesAsync"/>). It asks as the role the application logs in as,
    /// in the stage of the login, before the check that the login role may switch to them: a role the files were
    /// never written for is missing there too, and this says why.
    /// </summary>
    public const string RolesMatchAccessFilesCheck = "supabase.roles-match-access-files";

    /// <summary>The name Npgsql's provider for Entity Framework gives itself: what a context on Postgres reports.</summary>
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// The comment on the <c>ddd</c> schema, where the access files record the roles they were written for; no row
    /// where the database has no such schema. The catalogs answer every role, so the login role reads it without a
    /// privilege.
    /// </summary>
    private const string RecordQuery =
        "SELECT pg_catalog.obj_description(n.oid, 'pg_namespace') FROM pg_catalog.pg_namespace n WHERE n.nspname = 'ddd'";

    /// <summary>
    /// Throws unless the roles <paramref name="context"/> switches to, as its row level security interceptor has them,
    /// are the ones the access files in its database were written for: the user's, the anonymous caller's, the scoped
    /// system role, the system caller's and the role of every mapped token role. It passes where the database holds no
    /// record, a database the Supabase export wrote no access file for, or one applied by a version that recorded none.
    /// <para>
    /// The scoped system role is compared where the options have one. The system caller's role is compared with the
    /// bookkeeping role the files made; where they made none, a system caller that runs as the login role, or as one of
    /// Postgres's or Supabase's own roles such as <c>service_role</c>, is what the files expect.
    /// </para>
    /// <para>
    /// It asks as the role the application logs in as, on the context's connection opened past the interceptor, so a
    /// role it could not switch to does not stop the question; the catalogs answer every role, and nothing is set on
    /// the connection.
    /// </para>
    /// </summary>
    /// <param name="context">A context on the application's connection, with its row level security interceptor.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The context has no row level security interceptor, or a role differs; the message names each, with what the files
    /// were written for, what the host runs as, and the ways to make them one: the host's own
    /// <c>SupabaseRowAccessRoles</c> or its options in code, or <c>SupabaseRowAccessRoles</c> in the project that exports.
    /// </exception>
    public static async Task EnsureRolesMatchAccessFilesAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = InterceptorOf(context)?.Options ?? throw NotWired(context);
        if (SupabaseCallerRoles.ReadRecord(await RecordAsync(context, cancellationToken).ConfigureAwait(false)) is not { } recorded)
        {
            return;
        }

        var differences = Differences(recorded, options);
        if (differences.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .Append("The access files in the database of '").Append(context.GetType().Name)
            .Append("' were written for other roles than the ones this host switches to:");
        foreach (var difference in differences)
        {
            message.Append("\n- ").Append(difference);
        }

        message
            .Append("\nA policy for a role no caller runs as lets nobody in, and a caller whose role the files never named finds nothing, or is refused. ")
            .Append("Where the project that exports says these roles already, the database has not had the access files its last build wrote: apply them.");
        throw new InvalidOperationException(message.ToString());
    }

    /// <summary>
    /// Registers the check above as a start-up check: once, however many times row level security is registered. It
    /// takes every context the services register that is on Postgres and runs as its caller, each in a scope of its own.
    /// </summary>
    internal static void AddStartupChecks(IServiceCollection services)
        => services.AddStartupCheck(new StartupCheck(RolesMatchAccessFilesCheck, StartupCheckStage.Login, EachContextAsync)
        {
            RunsBefore = [PostgresRowAccessChecks.LoginRoleMaySwitchToCallersCheck],
        });

    /// <summary>
    /// Each difference between what the files were written for and what <paramref name="options"/> switch to, as a
    /// line of the message: what differs, and the fixes. The host's side is said first in its project file, where the
    /// roles are said once, and then in code; the exporter's side last.
    /// </summary>
    private static List<string> Differences(SupabaseCallerRoles.RecordedRoles recorded, PostgresRowLevelSecurityOptions options)
    {
        var differences = new List<string>();
        if (!string.Equals(recorded.User, options.UserRole, StringComparison.Ordinal))
        {
            differences.Add(
                $"a signed-in user: the policies are for {recorded.User}, and this host runs one as {options.UserRole}. " +
                $"Fix: {InThisHost($"user={recorded.User}", $"options.UserRole = \"{recorded.User}\"")}; or {InTheExporter($"user={options.UserRole}")}.");
        }

        if (!string.Equals(recorded.Anonymous, options.AnonymousRole, StringComparison.Ordinal))
        {
            differences.Add(
                $"a caller without a token: the policies are for {recorded.Anonymous}, and this host runs one as {options.AnonymousRole}. " +
                $"Fix: {InThisHost($"anonymous={recorded.Anonymous}", $"options.AnonymousRole = \"{recorded.Anonymous}\"")}; or {InTheExporter($"anonymous={options.AnonymousRole}")}.");
        }

        // A host without a scoped system role does no work inside the policies, whatever they were written for.
        if (options.SystemInRole is { } systemIn && !string.Equals(recorded.SystemIn, systemIn, StringComparison.Ordinal))
        {
            differences.Add(
                $"the application's own work inside the policies: they are for {recorded.SystemIn}, and this host runs it as {systemIn}. " +
                $"Fix: {InThisHost($"system-in={recorded.SystemIn}", $"options.SystemInRole = \"{recorded.SystemIn}\"")}; or {InTheExporter($"system-in={systemIn}")}.");
        }

        if (SystemDifference(recorded.System, options.SystemRole) is { } system)
        {
            differences.Add(system);
        }

        foreach (var tokenRole in recorded.TokenRoles.Keys.Union(options.TokenRoles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var written = recorded.TokenRoles.GetValueOrDefault(tokenRole);
            var runs = options.TokenRoles.TryGetValue(tokenRole, out var mapped) ? mapped : null;
            if (string.Equals(written, runs, StringComparison.Ordinal))
            {
                continue;
            }

            var key = SupabaseCallerRoles.TokenKey + tokenRole;
            differences.Add((written, runs) switch
            {
                (not null, null) =>
                    $"the token role '{tokenRole}': the policies are for {written}, and this host maps it to no role. " +
                    $"Fix: {InThisHost($"{key}={written}", $"options.TokenRoles[\"{tokenRole}\"] = \"{written}\"")}; or take {key}={written} out of SupabaseRowAccessRoles of the project that exports.",
                (null, not null) =>
                    $"the token role '{tokenRole}': this host runs it as {runs}, and the files write nothing for it, so no policy is for that role and no file made it. " +
                    $"Fix: {InTheExporter($"{key}={runs}")}; or take {key}={runs} out of this host's SupabaseRowAccessRoles, or options.TokenRoles[\"{tokenRole}\"] out of its code.",
                _ =>
                    $"the token role '{tokenRole}': the policies are for {written}, and this host runs it as {runs}. " +
                    $"Fix: {InThisHost($"{key}={written}", $"options.TokenRoles[\"{tokenRole}\"] = \"{written}\"")}; or {InTheExporter($"{key}={runs}")}.",
            });
        }

        return differences;
    }

    /// <summary>
    /// What differs about the system caller, or <see langword="null"/>: the files gave the toolkit's bookkeeping to
    /// <paramref name="written"/>, or to no role of the application's own, and the host's system caller runs as
    /// <paramref name="runs"/>, or as the role it logs in as. Where the files made no bookkeeping role, a system caller
    /// that runs as the login role or as one of the platform's roles is what they expect.
    /// </summary>
    private static string? SystemDifference(string? written, string? runs)
    {
        if (string.Equals(written, runs, StringComparison.Ordinal) || (written is null && runs is not null && SupabaseMigrations.IsPlatformRole(runs)))
        {
            return null;
        }

        var files = written is null
            ? "the files make no bookkeeping role (system=none)"
            : $"the files give the outbox, the inbox and the migration history to {written}";
        var host = runs is null
            ? "this host's system caller runs as the role it logs in as"
            : $"this host's system caller runs as {runs}";
        var inHost = InThisHost(
            $"system={written ?? SupabaseCallerRoles.LoginRole}",
            written is null ? "options.SystemRole = null" : $"options.SystemRole = \"{written}\"");
        var inExporter = InTheExporter($"system={runs ?? SupabaseCallerRoles.LoginRole}");

        return $"the system caller: {files}, and {host}. Fix: {inHost}; or {inExporter}.";
    }

    /// <summary>The fix on the host's side: <paramref name="pair"/> in its own project file, where the roles are said once, or <paramref name="code"/>.</summary>
    private static string InThisHost(string pair, string code)
        => $"{pair} in this host's SupabaseRowAccessRoles, or {code} in its code";

    /// <summary>The fix on the side of the project that exports: <paramref name="pair"/> in its <c>SupabaseRowAccessRoles</c>.</summary>
    private static string InTheExporter(string pair)
        => $"{pair} in SupabaseRowAccessRoles of the project that exports";

    /// <summary>
    /// Runs the check on every context <paramref name="services"/> register that is on Postgres and runs as its caller,
    /// by the name of its type, in a scope of its own. A context without the interceptor, one that runs as the login
    /// role on purpose or one wired wrong, is left to <c>postgres.row-level-security-wired</c>.
    /// </summary>
    private static async Task EachContextAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var contextTypes = scope.ServiceProvider.GetServices<DbContextOptions>()
                .Select(contextOptions => contextOptions.ContextType)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var contextType in contextTypes)
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                if (context.Database.ProviderName == NpgsqlProvider && InterceptorOf(context) is not null)
                {
                    await EnsureRolesMatchAccessFilesAsync(context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// The row level security interceptor among <paramref name="context"/>'s, whose options are the roles the context
    /// really switches to, or null where it has none.
    /// </summary>
    private static PostgresRowLevelSecurityInterceptor? InterceptorOf(DbContext context)
        => context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?.OfType<PostgresRowLevelSecurityInterceptor>().FirstOrDefault();

    /// <summary>What a context without the interceptor is told: it switches to no caller's role, so there is nothing to compare.</summary>
    private static InvalidOperationException NotWired(DbContext context)
        => new(
            $"'{context.GetType().Name}' does not run its commands as the caller: its options have no {nameof(PostgresRowLevelSecurityInterceptor)}, so it switches to no role the access files could have been written for. " +
            $"Configure it with options.UseDDDToolkit(serviceProvider), which adds it to every context on Postgres once row level security is registered, or with options.{nameof(DependencyInjection.UseSupabaseRowLevelSecurity)}(serviceProvider).");

    /// <summary>
    /// The comment on the <c>ddd</c> schema, asked as the role the application logged in as: on the context's
    /// connection, opened past Entity Framework and so past the interceptor where it is closed, outside any ambient
    /// transaction, and closed again. A connection the context holds open already is asked as it is.
    /// </summary>
    private static async Task<string?> RecordAsync(DbContext context, CancellationToken cancellationToken)
    {
        using var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);

        var connection = context.Database.GetDbConnection();
        var opened = connection.State == System.Data.ConnectionState.Closed;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = RecordQuery;
                command.Transaction = opened ? null : context.Database.CurrentTransaction?.GetDbTransaction();
                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
