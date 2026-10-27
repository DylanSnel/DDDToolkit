using DDDToolkit.Auth.Supabase;
using DDDToolkit.Auth.Supabase.AzureFunctions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseTestTokens;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// An Azure Function on the isolated worker, through <see cref="SupabaseAuthMiddleware"/>: the invocation's
/// token decides who the function's queries run as, against the same Postgres and the same policy as the
/// ASP.NET Core tests. The worker has no ASP.NET Core pipeline, so the caller is made current with
/// <see cref="ICurrentUser.BeginAs(CurrentUserSnapshot)"/>, and the context asks <see cref="CurrentUser"/>.
/// </summary>
public sealed class SupabaseFunctionsTests(SupabaseRowLevelSecurityDatabase database)
    : IClassFixture<SupabaseRowLevelSecurityDatabase>, IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly SupabaseAuthMiddleware _middleware = new(new SupabaseTokenValidator(new SupabaseAuthOptions
    {
        ProjectUrl = LocalProjectUrl,
        JwtSecret = LocalJwtSecret,
    }));

    public async ValueTask InitializeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        await database.ClearAsync();

        // Two notes, written as their owners.
        foreach (var (owner, text) in new[] { (Alice, "Alice's"), (Bob, "Bob's") })
        {
            await using var context = database.CreateContext(new CallerOfTheTest { Current = CurrentUserSnapshot.FromClaims(ClaimsOf(owner)) });
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = text });
            await context.SaveChangesAsync(Cancellation);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task An_http_function_runs_as_the_user_its_request_names()
    {
        database.Require();
        var invocation = WorkerInvocation.Http("Bearer " + Local(Alice));

        var seen = await InvokeAsync(invocation);

        seen.Should().Equal("Alice's");
        invocation.GetSupabaseUser().Kind.Should().Be(CurrentUserKind.User);
        BegunUser.Should().BeNull("the caller ends with the invocation");
    }

    [Fact]
    public async Task An_http_function_without_a_valid_token_runs_as_anon()
    {
        database.Require();

        (await InvokeAsync(WorkerInvocation.Http(authorization: null))).Should().BeEmpty();
        (await InvokeAsync(WorkerInvocation.Http("Bearer " + Local(Alice, secret: "somebody-else's-secret-that-is-also-32-characters-long")))).Should().BeEmpty();
        WorkerInvocation.Http(authorization: null).GetSupabaseUser().Should().BeSameAs(CurrentUserSnapshot.System, "nothing ran through the middleware yet");
    }

    [Fact]
    public async Task A_queue_function_runs_as_the_system_unless_it_begins_a_caller_itself()
    {
        database.Require();
        var options = new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole };

        (await InvokeAsync(WorkerInvocation.Queue(), options)).Should().BeEquivalentTo(["Alice's", "Bob's"]);

        // A message queued on Bob's behalf, carrying his claims: the function makes him the caller.
        var asBob = await InvokeAsync(WorkerInvocation.Queue(), options, async read =>
        {
            using (TestUser.BeginAs(CurrentUserSnapshot.FromClaims(ClaimsOf(Bob))))
            {
                return await read();
            }
        });

        asBob.Should().Equal("Bob's");
    }

    [Fact]
    public void Outside_an_http_trigger_with_explicit_callers_required_GetSupabaseCaller_throws()
    {
        using var strict = new ServiceCollection().RequireExplicitCurrentUser().BuildServiceProvider();
        using var plain = new ServiceCollection().BuildServiceProvider();

        var queued = WorkerInvocation.Queue();
        queued.InstanceServices = strict;

        ((Func<CurrentUserSnapshot>)(() => queued.GetSupabaseUser())).Should().Throw<NoCurrentUserException>()
            .WithMessage("*not an HTTP request and began no user*RequireExplicitCurrentUser*");

        using (TestUser.BeginSystem())
        {
            queued.GetSupabaseUser().Should().BeSameAs(CurrentUserSnapshot.System, "a function that begins a caller runs as it");
        }

        var unstrict = WorkerInvocation.Queue();
        unstrict.InstanceServices = plain;
        unstrict.GetSupabaseUser().Should().BeSameAs(CurrentUserSnapshot.System, "without the option it answers the system, as it always did");
    }

    /// <summary>Runs a function that reads the notes through a context on <see cref="CurrentUser"/>.</summary>
    private async Task<IReadOnlyList<string>> InvokeAsync(
        FunctionContext invocation,
        PostgresRowLevelSecurityOptions? options = null,
        Func<Func<Task<IReadOnlyList<string>>>, Task<IReadOnlyList<string>>>? function = null)
    {
        IReadOnlyList<string> seen = [];

        async Task<IReadOnlyList<string>> ReadAsync()
        {
            await using var context = database.CreateContext(new CurrentUser(), options);
            return await context.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync(Cancellation);
        }

        await _middleware.Invoke(invocation, async _ => seen = function is null ? await ReadAsync() : await function(ReadAsync));
        return seen;
    }
}
