using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
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

    [Fact]
    public void Outside_any_scope_there_is_no_caller_and_the_work_is_the_systems()
    {
        Callers.Ambient.Should().BeNull();
        _accessor.Current.Should().BeSameAs(Caller.System);
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
