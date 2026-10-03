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
/// A caller made current in code: what a host without requests uses, and how any code runs something as
/// somebody on purpose. It follows the flow of work the way an <see cref="AsyncLocal{T}"/> does.
/// </summary>
public sealed class CallerTests
{
    private static readonly Caller Alice = Callers.FromClaims(ClaimsOf(SupabaseRowLevelSecurityDatabase.Alice));
    private static readonly Caller Bob = Callers.FromClaims(ClaimsOf(SupabaseRowLevelSecurityDatabase.Bob));

    private readonly AmbientCallerAccessor _accessor = new();

    private readonly AmbientCallerAccessor _strict = new(new CallerOptions { RequireExplicitCallers = true });

    [Fact]
    public void Outside_any_scope_there_is_no_caller_and_the_work_is_the_systems()
    {
        Callers.Ambient.Should().BeNull();
        _accessor.Current.Should().BeSameAs(Caller.System);
    }

    [Fact]
    public void SystemIn_is_scoped_system_work_and_not_System()
    {
        var projects = Caller.SystemIn("projects");

        projects.Kind.Should().Be(CallerKind.SystemIn);
        projects.IsSystemIn.Should().BeTrue();
        projects.IsSystem.Should().BeFalse("its work stays inside the policies, which the system's does not");
        projects.Scope.Should().Be("projects");
        projects.UserId.Should().BeNull();
        projects.IsSignedIn.Should().BeFalse();
        projects.Role.Should().BeNull("which role it runs as is the host's to configure");
        projects.Claims.Should().BeNull();
        projects.ToString().Should().Be("system in projects");

        Caller.System.Scope.Should().BeNull();
        Caller.System.IsSystemIn.Should().BeFalse();
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
        var make = () => Caller.SystemIn(scope);

        make.Should().Throw<ArgumentException>().WithParameterName("scope");
        ((Func<Caller>)(() => Caller.SystemIn(null!))).Should().Throw<ArgumentException>().WithParameterName("scope");
        Caller.SystemIn("crew_leads-2").Scope.Should().Be("crew_leads-2", "lower case letters, digits, '_' and '-' make a scope");
    }

    [Fact]
    public void With_explicit_callers_required_asking_outside_any_scope_throws()
    {
        var ask = () => _strict.Current;

        ask.Should().Throw<NoCallerException>()
            .WithMessage("*RequireExplicitCallers*Callers.Begin(Caller.System)*")
            .Which.Should().BeAssignableTo<InvalidOperationException>("code that caught what the toolkit threw before still catches it");
    }

    [Fact]
    public void A_begun_caller_is_answered_either_way()
    {
        using (Callers.Begin(Alice))
        {
            _accessor.Current.Should().BeSameAs(Alice);
            _strict.Current.Should().BeSameAs(Alice);
        }

        using (Callers.Begin(Caller.System))
        {
            _strict.Current.Should().BeSameAs(Caller.System, "the system's work is allowed, once somebody said so");
        }
    }

    [Fact]
    public void BeginNone_hides_the_ambient_caller_until_disposed()
    {
        using (Callers.Begin(Alice))
        {
            using (Callers.BeginNone())
            {
                Callers.Ambient.Should().BeNull();
                _accessor.Current.Should().BeSameAs(Caller.System, "without the option nobody is still the system");
                ((Func<Caller>)(() => _strict.Current)).Should().Throw<NoCallerException>();

                using (Callers.Begin(Bob))
                {
                    _strict.Current.Should().BeSameAs(Bob, "a caller begun inside it is answered");
                }

                Callers.Ambient.Should().BeNull();
            }

            Callers.Ambient.Should().BeSameAs(Alice);
        }

        Callers.Ambient.Should().BeNull();
    }

    [Fact]
    public void RequireExplicitCallers_is_what_the_accessor_asks()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICallerAccessor, AmbientCallerAccessor>();
        services.RequireExplicitCallers();
        services.RequireExplicitCallers();

        using var provider = services.BuildServiceProvider();

        services.Count(descriptor => descriptor.ServiceType == typeof(CallerOptions)).Should().Be(1, "registering it twice is harmless");
        provider.GetRequiredService<CallerOptions>().RequireExplicitCallers.Should().BeTrue();
        ((Func<Caller>)(() => provider.GetRequiredService<ICallerAccessor>().Current)).Should().Throw<NoCallerException>();

        using var plain = new ServiceCollection().AddSingleton<ICallerAccessor, AmbientCallerAccessor>().BuildServiceProvider();
        plain.GetRequiredService<ICallerAccessor>().Current.Should().BeSameAs(Caller.System, "without it a 3.x host answers as it always did");
    }

    [Fact]
    public void A_scope_makes_its_caller_current_and_puts_back_the_one_before_it()
    {
        using (Callers.Begin(Alice))
        {
            _accessor.Current.Should().BeSameAs(Alice);

            using (Callers.Begin(Bob))
            {
                _accessor.Current.Should().BeSameAs(Bob);
            }

            _accessor.Current.Should().BeSameAs(Alice);
        }

        Callers.Ambient.Should().BeNull();
    }

    [Fact]
    public async Task A_caller_follows_the_work_into_awaits_and_tasks_but_not_back_out_of_them()
    {
        using (Callers.Begin(Alice))
        {
            await Task.Yield();
            _accessor.Current.Should().BeSameAs(Alice, "an await does not lose it");

            (await Task.Run(() => _accessor.Current)).Should().BeSameAs(Alice, "a task started inside the scope has it");
        }

        await BeginWithoutEndingAsync(Bob);
        Callers.Ambient.Should().BeNull("a caller begun inside an awaited method stays inside it");
    }

    [Fact]
    public void Ending_a_scope_twice_changes_nothing_the_second_time()
    {
        var outer = Callers.Begin(Alice);
        var inner = Callers.Begin(Bob);

        inner.Dispose();
        inner.Dispose();

        _accessor.Current.Should().BeSameAs(Alice);
        outer.Dispose();
    }

    [Fact]
    public void A_token_is_a_user_with_its_sub_its_role_and_every_claim_as_it_was_signed()
    {
        var claims = ClaimsOf(SupabaseRowLevelSecurityDatabase.Alice, email: "alice@example.com");

        var alice = Callers.FromClaims(claims);

        alice.Kind.Should().Be(CallerKind.User);
        alice.UserId.Should().Be(SupabaseRowLevelSecurityDatabase.Alice);
        alice.Role.Should().Be("authenticated");
        alice.Claim("email").Should().Be("alice@example.com");
        alice.Claim("app_metadata.provider").Should().Be("email", "a path reads into nested claims");
        alice.Claim("app_metadata.teams").Should().Be("[\"north\"]", "what is not text comes as its JSON");
        alice.Claim("app_metadata.missing").Should().BeNull();
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
        // A caller without a role is a token that says nothing about one. A role claim of another shape says
        // something, which is kept as written rather than read as nothing; and of a claim signed twice the last
        // one counts, as it does for the database.
        var caller = Callers.FromClaims(claims);

        caller.Role.Should().Be(role);
        caller.Claim("role").Should().Be(role, "the role is the claim, read as any claim is");
    }

    [Fact]
    public void A_subject_that_is_not_a_guid_is_still_a_user_but_nobody_rules_about_a_user_match()
    {
        var auth0 = Callers.FromClaims("""{"sub":"auth0|64f1c2","role":"authenticated"}""");

        auth0.Kind.Should().Be(CallerKind.User);
        auth0.UserId.Should().BeNull("ddd.caller_id() answers null for it as well");
        auth0.IsSignedIn.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Claims_that_are_not_a_json_object_are_refused(string claims)
    {
        var make = () => Callers.FromClaims(claims);

        make.Should().Throw<ArgumentException>().WithParameterName("claims");
    }

    [Fact]
    public void The_system_and_somebody_who_has_not_signed_in_are_not_users()
    {
        Caller.System.IsSystem.Should().BeTrue();
        Caller.System.UserId.Should().BeNull();
        Caller.Anonymous.Kind.Should().Be(CallerKind.Anonymous);
        Caller.Anonymous.Role.Should().Be("anon");
    }

    private static async Task BeginWithoutEndingAsync(Caller caller)
    {
        Callers.Begin(caller);
        await Task.Yield();
    }
}
