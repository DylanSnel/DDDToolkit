using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the one call means on a real Postgres with Supabase's roles, asked of the database itself: once row level
/// security is registered, a context wired with <c>UseDDDToolkit</c> alone runs every command as its caller, as the
/// chain written out with <c>UseSupabaseRowLevelSecurity</c> did, also where the context sets its provider itself, in
/// <c>OnConfiguring</c>, after the one call; and a context given the base alone, <c>UseDDDToolkitCore</c>, runs as the
/// role the application logged in as.
/// </summary>
public sealed class UseDDDToolkitPostgresTests(SupabaseRowLevelSecurityDatabase database) : IClassFixture<SupabaseRowLevelSecurityDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_one_call_runs_a_context_as_its_caller_wherever_its_provider_is_set_and_the_base_alone_runs_one_as_the_login_role()
    {
        database.Require();

        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddSupabaseRowLevelSecurity<CallerOfTheTest>();
        registered.AddDbContext<NotesContext>((provider, options) => options.UseNpgsql(database.ApplicationConnectionString).UseDDDToolkit(provider));
        registered.AddDbContext<GuardedNotesContext>((provider, options) => options.UseNpgsql(database.ApplicationConnectionString).UseDDDToolkitCore(provider));
        registered.AddSingleton(new NotesConnection(database.ApplicationConnectionString));
        registered.AddDbContext<SelfConfiguredNotesContext>((provider, options) => options.UseDDDToolkit(provider));
        await using var services = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var callers = (CallerOfTheTest)services.GetRequiredService<ICallerAccessor>();

        callers.Current = Caller.Anonymous;
        await using (var scope = services.CreateAsyncScope())
        {
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<NotesContext>())).Should().Be("anon", "nothing but the one call put the caller on its connection");
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<SelfConfiguredNotesContext>())).Should().Be(
                "anon", "the provider was set in OnConfiguring, after the one call, which put row level security on all the same");
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<GuardedNotesContext>())).Should().Be(LoginRole, "the base alone leaves the connection to the login role");
        }

        callers.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using (var scope = services.CreateAsyncScope())
        {
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<NotesContext>())).Should().Be("authenticated");
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<SelfConfiguredNotesContext>())).Should().Be("authenticated");
            (await CurrentUserAsync(scope.ServiceProvider.GetRequiredService<GuardedNotesContext>())).Should().Be(LoginRole);
        }
    }

    /// <summary>The role the context's connection runs as, as the database says it.</summary>
    private static Task<string> CurrentUserAsync(DbContext context)
        => context.Database.SqlQueryRaw<string>("SELECT current_user AS \"Value\"").SingleAsync(Cancellation);

    /// <summary>Where <see cref="SelfConfiguredNotesContext"/> connects to.</summary>
    public sealed record NotesConnection(string Value);

    /// <summary>A context that sets its provider itself, in <c>OnConfiguring</c>, which Entity Framework runs after the options callback.</summary>
    public sealed class SelfConfiguredNotesContext(DbContextOptions<SelfConfiguredNotesContext> options, NotesConnection connection) : DbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(connection.Value);
            }
        }
    }
}
