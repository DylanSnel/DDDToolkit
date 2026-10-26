using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// One Postgres for the tests of pooled contexts under row level security, which run one class after the
/// other: they count connections and time a wait, and neither wants company.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PooledContextPostgres : ICollectionFixture<SupabaseRowLevelSecurityDatabase>
{
    public const string Name = "Pooled contexts on Postgres";
}

/// <summary>
/// A host, in miniature, whose <see cref="NotesContext"/> comes from a context pool and runs under row level
/// security: the options callback calls <c>UseDDDToolkit</c> and <c>UsePostgresRowLevelSecurity</c>, once, with
/// the root provider, and every flow of work says who it runs as (<c>RequireExplicitCallers</c>). One
/// interceptor, one set of options and one settings provider serve every context of the pool, so whatever
/// differs per caller has to be read per use.
/// </summary>
public sealed class PooledNotesHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    /// <param name="connectionString">The application's connection string, as the role that may only switch roles.</param>
    public PooledNotesHost(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddPostgresRowLevelSecurity(options => options.SystemRole = SupabaseRowLevelSecurity.ServiceRole);
        services.RequireExplicitCallers();
        services.AddRowLevelSecuritySettings<TeamOfTheCaller>();
        services.AddPooledDbContextFactory<NotesContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseDDDToolkit(provider)
            .UsePostgresRowLevelSecurity(provider));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Rents from the pool, with no scope.</summary>
    public IDbContextFactory<NotesContext> Factory => _provider.GetRequiredService<IDbContextFactory<NotesContext>>();

    /// <summary>A signed-in user, optionally in a team, which <see cref="TeamOfTheCaller"/> turns into a setting.</summary>
    public static Caller User(Guid id, string? team = null)
        => Callers.FromClaims(team is null
            ? $$"""{"sub":"{{id}}","role":"authenticated"}"""
            : $$"""{"sub":"{{id}}","role":"authenticated","team":"{{team}}"}""");

    /// <summary>
    /// Who the database says is running <paramref name="context"/>'s commands: the user's id or <c>none</c>, the
    /// role, and the team setting, as <c>id|role|team</c>. With <paramref name="hold"/> the statement takes that
    /// long on the server, so statements of several flows overlap.
    /// </summary>
    public static async Task<string> WhoAsync(NotesContext context, TimeSpan? hold = null, CancellationToken cancellationToken = default)
    {
        // SQL the tests write themselves; nothing in it comes from outside.
        var from = hold is { } time ? FormattableString.Invariant($" FROM pg_sleep({time.TotalSeconds:0.###})") : string.Empty;
        var sql = $"""SELECT coalesce(auth.uid()::text, 'none') || '|' || current_user || '|' || coalesce(current_setting('{TeamOfTheCaller.Setting}', true), '') AS "Value"{from}""";

        return await context.Database.SqlQueryRaw<string>(sql).SingleAsync(cancellationToken);
    }

    /// <summary>The same connection string with other limits on its Npgsql pool, under a name of its own in <c>pg_stat_activity</c>.</summary>
    public static string Budgeted(string connectionString, string applicationName, int maximum, int timeoutSeconds)
        => new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName,
            MinPoolSize = 0,
            MaxPoolSize = maximum,
            Timeout = timeoutSeconds,
        }.ConnectionString;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>
/// A setting that follows the caller: the team their token names, or nothing. A singleton, like every
/// settings provider, that keeps nothing between two calls.
/// </summary>
public sealed class TeamOfTheCaller : IRowLevelSecuritySettings
{
    public const string Setting = "test.team";

    public IReadOnlyCollection<string> Names { get; } = [Setting];

    public IEnumerable<KeyValuePair<string, string>> For(Caller caller)
        => caller.Claim("team") is { } team ? [new(Setting, team)] : [];
}
