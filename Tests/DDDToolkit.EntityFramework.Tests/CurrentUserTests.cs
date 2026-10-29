using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The current user, and a user begun in code: what a host without requests uses, and how any code runs something as
/// somebody on purpose. What is begun follows the flow of work the way an <see cref="AsyncLocal{T}"/> does.
/// </summary>
public sealed class CurrentUserTests
{
    private static readonly CurrentUserSnapshot Alice = CurrentUserSnapshot.FromClaims(ClaimsOf(SupabaseRowLevelSecurityDatabase.Alice));
    private static readonly CurrentUserSnapshot Bob = CurrentUserSnapshot.FromClaims(ClaimsOf(SupabaseRowLevelSecurityDatabase.Bob));

    private readonly CurrentUser _accessor = new();

    private readonly AccessManager _access = new(new CurrentUser());

    private readonly CurrentUser _strict = new(new CurrentUserOptions { RequireExplicitCurrentUser = true });

    [Fact]
    public void Outside_any_scope_nothing_is_begun_and_the_work_is_system_work()
    {
        BegunUser.Should().BeNull();
        _accessor.IsBegun.Should().BeFalse();
        _accessor.Snapshot.Should().BeSameAs(CurrentUserSnapshot.System);
    }

    [Fact]
    public void The_current_user_answers_what_was_begun_each_time_it_is_asked()
    {
        var ada = Guid.Parse("ada00000-0000-4000-8000-000000000001");

        using (_access.BeginAs(ada))
        {
            _accessor.IsBegun.Should().BeTrue();
            _accessor.Kind.Should().Be(CurrentUserKind.User);
            _accessor.Id.Should().Be(ada);
            _accessor.IsAuthenticated.Should().BeTrue();
            _accessor.IsSystem.Should().BeFalse();
            _accessor.SystemScope.Should().BeNull();
            _accessor.Role.Should().Be("authenticated", "a user begun by id has the role a signed-in user's token carries");
            _accessor.FindClaim("email").Should().BeNull("a user begun by id has no other claims");

            using (_access.BeginSystem("billing"))
            {
                _accessor.Kind.Should().Be(CurrentUserKind.SystemIn);
                _accessor.Id.Should().BeNull("system work has no user id, whoever began it");
                _accessor.IsAuthenticated.Should().BeFalse();
                _accessor.IsSystem.Should().BeTrue();
                _accessor.SystemScope.Should().Be("billing");
                _accessor.Role.Should().BeNull();
            }

            _accessor.Id.Should().Be(ada, "once the system work ends, the user before it is current again");
        }

        using (new AccessManager(_strict).BeginAs(Alice))
        {
            _accessor.FindClaim("email").Should().Be("someone@example.com", "every instance sees what was begun for the flow");
        }

        ((Func<IDisposable>)(() => _access.BeginSystem("Billing"))).Should().Throw<ArgumentException>("a scope is lower case").WithParameterName("scope");
        BegunUser.Should().BeNull("a scope that is no scope begins nothing");
        ((Func<IDisposable>)(() => _access.BeginAs(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SystemIn_is_system_work_in_a_scope_and_not_System()
    {
        var projects = CurrentUserSnapshot.SystemIn("projects");

        projects.Kind.Should().Be(CurrentUserKind.SystemIn);
        projects.IsSystem.Should().BeTrue("it is system work, which the kind tells apart from system work without a scope");
        projects.Kind.Should().NotBe(CurrentUserKind.System, "its work stays inside the policies, which the system's does not");
        projects.SystemScope.Should().Be("projects");
        projects.Id.Should().BeNull();
        projects.IsAuthenticated.Should().BeFalse();
        projects.Role.Should().BeNull("which role it runs as is the host's to configure");
        projects.Claims.Should().BeNull();
        projects.ToString().Should().Be("system in projects");

        CurrentUserSnapshot.System.SystemScope.Should().BeNull();
        CurrentUserSnapshot.System.Kind.Should().Be(CurrentUserKind.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Projects")]
    [InlineData("two words")]
    [InlineData("projects.crews")]
    [InlineData("projects'")]
    [InlineData("pröjects")]
    public void SystemIn_needs_a_scope(string scope)
    {
        var make = () => CurrentUserSnapshot.SystemIn(scope);

        make.Should().Throw<ArgumentException>().WithParameterName("scope");
        ((Func<CurrentUserSnapshot>)(() => CurrentUserSnapshot.SystemIn(null!))).Should().Throw<ArgumentException>().WithParameterName("scope");
        CurrentUserSnapshot.SystemIn("crew_leads-2").SystemScope.Should().Be("crew_leads-2", "lower case letters, digits, '_' and '-' make a scope");
    }

    [Fact]
    public void With_an_explicit_current_user_required_asking_outside_any_scope_throws()
    {
        var ask = () => _strict.Snapshot;

        _strict.IsBegun.Should().BeFalse("asking whether anything was begun never throws");
        ask.Should().Throw<NoCurrentUserException>()
            .WithMessage("*RequireExplicitCurrentUser*BeginSystem(scope)*BeginSystemBypassingRowAccess()*")
            .Which.Should().BeAssignableTo<InvalidOperationException>("code that caught what the toolkit threw before still catches it");
    }

    [Fact]
    public void A_begun_user_is_answered_either_way()
    {
        using (TestAccess.BeginAs(Alice))
        {
            _accessor.Snapshot.Should().BeSameAs(Alice);
            _strict.Snapshot.Should().BeSameAs(Alice);
        }

        using (TestAccess.BeginSystemBypassingRowAccess())
        {
            _strict.Snapshot.Should().BeSameAs(CurrentUserSnapshot.System, "the system's work is allowed, once somebody said so");
        }
    }

    [Fact]
    public void BeginSystem_always_has_a_scope_and_the_bypass_has_a_name_of_its_own()
    {
        using (_access.BeginSystemBypassingRowAccess())
        {
            BegunUser.Should().BeSameAs(CurrentUserSnapshot.System, "it is the system work the host answers where nothing was begun");
            _accessor.Kind.Should().Be(CurrentUserKind.System, "no row access rule holds it");
            _accessor.SystemScope.Should().BeNull();

            using (_access.BeginSystem("billing"))
            {
                _accessor.Kind.Should().Be(CurrentUserKind.SystemIn, "BeginSystem begins system work the row access rules hold");
                _accessor.SystemScope.Should().Be("billing");
            }

            _accessor.Kind.Should().Be(CurrentUserKind.System, "once the scoped work ends, the work before it is current again");
        }

        BegunUser.Should().BeNull();
        ((Func<IDisposable>)(() => _access.BeginSystem(null!))).Should().Throw<ArgumentNullException>("the scope is required").WithParameterName("scope");
        BegunUser.Should().BeNull("a missing scope begins nothing");
    }

    [Fact]
    public void BeginNone_hides_the_begun_user_until_disposed()
    {
        using (TestAccess.BeginAs(Alice))
        {
            using (TestAccess.BeginNone())
            {
                BegunUser.Should().BeNull();
                _accessor.Snapshot.Should().BeSameAs(CurrentUserSnapshot.System, "without the option nobody is still the system");
                ((Func<CurrentUserSnapshot>)(() => _strict.Snapshot)).Should().Throw<NoCurrentUserException>();

                using (TestAccess.BeginAs(Bob))
                {
                    _strict.Snapshot.Should().BeSameAs(Bob, "a caller begun inside it is answered");
                }

                BegunUser.Should().BeNull();
            }

            BegunUser.Should().BeSameAs(Alice);
        }

        BegunUser.Should().BeNull();
    }

    [Fact]
    public void RequireExplicitCurrentUser_is_what_the_current_user_asks()
    {
        var services = new ServiceCollection();
        services.AddCurrentUser();
        services.AddCurrentUser();
        services.RequireExplicitCurrentUser();
        services.RequireExplicitCurrentUser();

        using var provider = services.BuildServiceProvider();

        services.Count(descriptor => descriptor.ServiceType == typeof(CurrentUserOptions)).Should().Be(1, "registering it twice is harmless");
        services.Count(descriptor => descriptor.ServiceType == typeof(ICurrentUser)).Should().Be(1, "and so is registering the current user twice");
        services.Single(descriptor => descriptor.ServiceType == typeof(ICurrentUser)).Lifetime.Should().Be(ServiceLifetime.Singleton, "it reads the flow of work each time, so a context pool's interceptor can hold it");
        services.Count(descriptor => descriptor.ServiceType == typeof(AccessManager)).Should().Be(1, "the access manager is registered beside it, once");
        services.Single(descriptor => descriptor.ServiceType == typeof(AccessManager)).Lifetime.Should().Be(ServiceLifetime.Singleton, "it keeps no state, as the current user keeps none");
        provider.GetRequiredService<AccessManager>().CurrentUser.Should().BeSameAs(provider.GetRequiredService<ICurrentUser>(), "its current user is the application's");
        provider.GetRequiredService<CurrentUserOptions>().RequireExplicitCurrentUser.Should().BeTrue();
        ((Func<CurrentUserSnapshot>)(() => provider.GetRequiredService<ICurrentUser>().Snapshot)).Should().Throw<NoCurrentUserException>();

        using var plain = new ServiceCollection().AddCurrentUser().BuildServiceProvider();
        plain.GetRequiredService<ICurrentUser>().Snapshot.Should().BeSameAs(CurrentUserSnapshot.System, "without it a 3.x host answers as it always did");
    }

    [Fact]
    public void A_begun_user_wins_over_the_hosts_answer_and_the_host_answers_where_nothing_is_begun()
    {
        var host = Answering(Bob);

        host.Snapshot.Should().BeSameAs(Bob, "where nothing is begun, the host's source answers, as for a request");
        host.IsBegun.Should().BeFalse("the host's answer is nothing anybody began");

        using (TestAccess.BeginAs(Alice))
        {
            host.Snapshot.Should().BeSameAs(Alice, "a user begun for the flow wins over the request's");
            host.IsBegun.Should().BeTrue();

            using (TestAccess.BeginNone())
            {
                host.Snapshot.Should().BeSameAs(Bob, "BeginNone hides the begun user, and the host answers again");
            }
        }
    }

    [Fact]
    public void A_scope_makes_its_user_current_and_puts_back_the_one_before_it()
    {
        using (TestAccess.BeginAs(Alice))
        {
            _accessor.Snapshot.Should().BeSameAs(Alice);

            using (TestAccess.BeginAs(Bob))
            {
                _accessor.Snapshot.Should().BeSameAs(Bob);
            }

            _accessor.Snapshot.Should().BeSameAs(Alice);
        }

        BegunUser.Should().BeNull();
    }

    [Fact]
    public async Task A_begun_user_follows_the_work_into_awaits_and_tasks_but_not_back_out_of_them()
    {
        using (TestAccess.BeginAs(Alice))
        {
            await Task.Yield();
            _accessor.Snapshot.Should().BeSameAs(Alice, "an await does not lose it");

            (await Task.Run(() => _accessor.Snapshot)).Should().BeSameAs(Alice, "a task started inside the scope has it");
        }

        await BeginWithoutEndingAsync(Bob);
        BegunUser.Should().BeNull("a user begun inside an awaited method stays inside it");
    }

    [Fact]
    public void Ending_a_scope_twice_changes_nothing_the_second_time()
    {
        var outer = TestAccess.BeginAs(Alice);
        var inner = TestAccess.BeginAs(Bob);

        inner.Dispose();
        inner.Dispose();

        _accessor.Snapshot.Should().BeSameAs(Alice);
        outer.Dispose();
    }

    [Fact]
    public void A_token_is_a_user_with_its_sub_its_role_and_every_claim_as_it_was_signed()
    {
        var claims = ClaimsOf(SupabaseRowLevelSecurityDatabase.Alice, email: "alice@example.com");

        var alice = CurrentUserSnapshot.FromClaims(claims);

        alice.Kind.Should().Be(CurrentUserKind.User);
        alice.Id.Should().Be(SupabaseRowLevelSecurityDatabase.Alice);
        alice.Role.Should().Be("authenticated");
        alice.FindClaim("email").Should().Be("alice@example.com");
        alice.FindClaim("app_metadata.provider").Should().Be("email", "a path reads into nested claims");
        alice.FindClaim("app_metadata.teams").Should().Be("[\"north\"]", "what is not text comes as its JSON");
        alice.FindClaim("app_metadata.missing").Should().BeNull();
        alice.Claims.Should().Be(claims, "the database gets the claims as they were signed");
    }

    [Theory]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001"}""", null)]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":null}""", null)]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":"analyst"}""", "analyst")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":""}""", "")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":["authenticated"]}""", """["authenticated"]""")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":{"name":"authenticated"}}""", """{"name":"authenticated"}""")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":true}""", "true")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":7}""", "7")]
    [InlineData("""{"sub":"a11ce000-0000-4000-8000-000000000001","role":"authenticated","role":"analyst"}""", "analyst")]
    public void A_tokens_role_is_its_role_claim_and_a_claim_that_is_not_text_is_no_missing_claim(string claims, string? role)
    {
        // A user without a role is a token that says nothing about one. A role claim of another shape says
        // something, which is kept as written rather than read as nothing; and of a claim signed twice the last
        // one counts, as it does for the database.
        var caller = CurrentUserSnapshot.FromClaims(claims);

        caller.Role.Should().Be(role);
        caller.FindClaim("role").Should().Be(role, "the role is the claim, read as any claim is");
    }

    [Fact]
    public void A_subject_that_is_not_a_guid_is_still_a_user_but_nobody_rules_about_a_user_match()
    {
        var auth0 = CurrentUserSnapshot.FromClaims("""{"sub":"auth0|64f1c2","role":"authenticated"}""");

        auth0.Kind.Should().Be(CurrentUserKind.User);
        auth0.Id.Should().BeNull("\"ddd\".current_user_id() answers null for it as well");
        auth0.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Claims_that_are_not_a_json_object_are_refused(string claims)
    {
        var make = () => CurrentUserSnapshot.FromClaims(claims);

        make.Should().Throw<ArgumentException>().WithParameterName("claims");
    }

    [Fact]
    public void System_work_and_somebody_who_has_not_signed_in_are_not_users()
    {
        CurrentUserSnapshot.System.IsSystem.Should().BeTrue();
        CurrentUserSnapshot.System.IsAuthenticated.Should().BeFalse();
        CurrentUserSnapshot.Anonymous.IsSystem.Should().BeFalse();
        CurrentUserSnapshot.Anonymous.IsAuthenticated.Should().BeFalse();
        CurrentUserSnapshot.System.Id.Should().BeNull();
        CurrentUserSnapshot.Anonymous.Kind.Should().Be(CurrentUserKind.Anonymous);
        CurrentUserSnapshot.Anonymous.Role.Should().Be("anon");
    }

    private static async Task BeginWithoutEndingAsync(CurrentUserSnapshot caller)
    {
        TestAccess.BeginAs(caller);
        await Task.Yield();
    }
}
