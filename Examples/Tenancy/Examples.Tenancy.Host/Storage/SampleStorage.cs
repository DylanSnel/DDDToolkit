using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using Examples.Hosting;

namespace Examples.Tenancy.Host.Storage;

/// <summary>
/// Where the modules' tables live: Postgres, as Supabase runs it, at the connection string the host is given
/// (<see cref="ConnectionString"/>). There is no other database and no fallback: a host without that setting
/// stops before it is built, and says how to get one.
/// </summary>
/// <remarks>
/// The host logs in as a role that owns nothing and holds no privilege on any table, and every command runs as
/// its caller: a signed-in user, system work in a tenant, an operator, or the toolkit's own bookkeeping, each a
/// database role with exactly the privileges its policies give it. The tables, the policies and those privileges
/// come from the files under <c>Examples/Tenancy/supabase/migrations</c>, applied by whoever owns the database;
/// the host applies none, and refuses to start while one is missing.
/// <para>
/// The host knows no module's storage: it hands the modules a <see cref="ModuleHost"/> with its connections, and
/// each registers its own context on them.
/// </para>
/// </remarks>
public static class SampleStorage
{
    /// <summary>
    /// The connection string of the host's database: <c>ConnectionStrings:Supabase</c>, for the role the host
    /// logs in as. Never the role that owns the database: the host stops at start-up when it is.
    /// </summary>
    public const string ConnectionString = "Supabase";

    /// <summary>
    /// The section that budgets the host's connections, per purpose: <c>Requests</c> and
    /// <c>Background</c>, and <c>IdleLifetime</c>, <c>Lifetime</c> and <c>Timeout</c> in seconds.
    /// </summary>
    public const string PoolsSection = "Sample:Pools";

    /// <summary>
    /// The database role the toolkit's own bookkeeping runs as: the outbox pollers, and the check that every
    /// migration was applied. It reads and marks outbox rows and reads the migration history, and no row of a
    /// tenant. The exported files make it and give it those privileges, under this name.
    /// </summary>
    public const string BookkeepingRole = "ddd_system";

    /// <summary>
    /// How long one statement may run when it runs for a signed-in user, a seat or an operator: long enough for
    /// anything a route or a field of the sample asks, and short enough that a request shaped to be slow holds a
    /// connection of the request pool no longer than this.
    /// </summary>
    public static readonly TimeSpan UserStatementTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What a host without <see cref="ConnectionString"/> is told as it stops: the two ways to a database it can
    /// run on, each of which makes the roles and applies the exported files.
    /// </summary>
    public const string NoConnectionString =
        "ConnectionStrings:Supabase is not set, and the host runs on no other database. Start Examples.Tenancy.AppHost, " +
        "which starts Supabase's images and sets it, or run `supabase start` in Examples/Tenancy and pass the " +
        "connection string of the role tenancy_api (Examples/README.md).";

    /// <summary>
    /// Registers where the modules' tables live, and answers what the host hands each module: row level security
    /// with the roles the exported policies are written for, and the host's connections, which stop with the host.
    /// </summary>
    /// <param name="builder">The host being built.</param>
    /// <exception cref="InvalidOperationException">The configuration has no <see cref="ConnectionString"/>.</exception>
    public static ModuleHost AddSampleStorage(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // One database, and no fallback: a host that started without it would have to store somewhere else, and
        // nothing else has the roles and the policies the sample's rules rest on.
        var postgres = builder.Configuration.GetConnectionString(ConnectionString) is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException(NoConnectionString);

        // Every connection says who is calling, as a database role and its claims, before its first command. The
        // roles are the ones the exported policies name: Supabase's own for a signed-in user and an anonymous
        // caller, the scoped system role for work in a tenant, a role of its own for the toolkit's bookkeeping,
        // since the role the host logs in as holds nothing, and one for the operators' token role, which the
        // tokens and the database spell alike. A token with any other role is refused before it reaches a query.
        //
        // A statement that runs on a signed-in user's behalf is stopped when it takes longer than a request
        // should: a user's statements are the ones a client shapes. The application's own work, seeding and the
        // outbox's bookkeeping, keeps the login role's own timeout.
        builder.Services.AddSupabaseRowLevelSecurity(options =>
        {
            options.SystemRole = BookkeepingRole;
            options.TokenRoles[SampleTokenRoles.Operator] = SampleTokenRoles.Operator;
            options.StatementTimeouts[CallerKind.User] = UserStatementTimeout;
        });

        // Two bounded sets of connections for the one role the host logs in as: what answers requests, and what
        // runs in the background. Every module's context draws on them, and the host's container closes them
        // when the host stops.
        var pools = PostgresPools.Create(postgres, PostgresPoolBudget.From(builder.Configuration.GetSection(PoolsSection), applicationName: "tenancy"));
        return ModuleHost.OnPostgres(pools, postgres);
    }
}
