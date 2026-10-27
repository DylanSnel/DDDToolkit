using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>
/// Requirements that combine cases: <see cref="AccessRequirement.All"/>, every case holds, and a refusal is the first
/// failing case's own; <see cref="AccessRequirement.Any"/>, the first case that holds lets the caller through, and where
/// none does one <c>access.refused</c> carries each case's reason in order. Each case is decided by the check that
/// decides it alone, so cases of different owners combine, and a combination holds another.
/// </summary>
public class CombinedRequirementTests
{
    /// <summary>What every request of the test's module implements.</summary>
    public interface IBoardRequest : IRequireAccess;

    /// <summary>A request that requires what it is given.</summary>
    public sealed record ExportBoard(AccessRequirement Requires) : IBoardRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>A case of the boards' own: met when <see cref="Met"/>, refused with the board's code otherwise.</summary>
    public sealed record OnBoard(string Name, bool Met) : AccessRequirement;

    /// <summary>A case of billing's, which another check decides: met when <see cref="Met"/>.</summary>
    public sealed record OnPlan(string Plan, bool Met) : AccessRequirement;

    /// <summary>A case nobody decides.</summary>
    public sealed record OnNothing : AccessRequirement;

    /// <summary>A case whose check fails with something that is no refusal.</summary>
    public sealed record Racing : AccessRequirement;

    /// <summary>What the checks of a test were asked, in order.</summary>
    public sealed class Asked
    {
        public List<string> Entries { get; } = [];
    }

    /// <summary>Decides the boards' cases, keeps what it read for the handler, and says what it was asked.</summary>
    public sealed class BoardCheck(Asked asked, Checked<string> kept) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is OnBoard or Racing;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            switch (requirement)
            {
                case OnBoard board:
                    asked.Entries.Add("board " + board.Name);
                    if (!board.Met)
                    {
                        throw new RefusalException("boards.not-permitted", RefusalKind.NotPermitted, "Not on that board.", new Dictionary<string, object?> { ["Board"] = board.Name });
                    }

                    kept.KeepFor(request, board.Name);
                    return ValueTask.CompletedTask;

                case Racing:
                    asked.Entries.Add("racing");
                    throw new ConcurrencyConflictException(typeof(ExportBoard), 7);

                default:
                    throw new InvalidOperationException("Not a case of the boards'.");
            }
        }
    }

    /// <summary>Decides billing's cases, asynchronously, as a check that reads does.</summary>
    public sealed class PlanCheck(Asked asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is OnPlan;

        public async ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var plan = (OnPlan)requirement;
            asked.Entries.Add("plan " + plan.Plan);
            if (!plan.Met)
            {
                throw new RefusalException("billing.plan-required", RefusalKind.NotPermitted, "That takes another plan.", new Dictionary<string, object?> { ["Plan"] = plan.Plan });
            }
        }
    }

    /// <summary>An accessor that answers the caller a test gives it.</summary>
    private sealed class Answering(CurrentUserSnapshot caller) : ICurrentUserSource
    {
        public CurrentUserSnapshot? Current => caller;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static OnBoard Met(string name) => new(name, Met: true);

    private static OnBoard Refuses(string name) => new(name, Met: false);

    // ------------------------------------------------------------------ All

    [Fact]
    public async Task All_holds_when_every_case_does_asked_in_their_order()
    {
        var (checks, asked) = Checks();

        await checks.RequireAsync(new ExportBoard(AccessRequirement.All(Met("a"), new OnPlan("pro", Met: true), Met("b"))), Cancellation);

        asked.Entries.Should().Equal("board a", "plan pro", "board b");
    }

    [Fact]
    public async Task All_is_refused_with_the_first_failing_case_s_own_reason_and_asks_nothing_after_it()
    {
        var (checks, asked) = Checks();

        var refusal = await RefusedAsync(checks, AccessRequirement.All(Met("a"), new OnPlan("pro", Met: false), Refuses("b")));

        refusal.Code.Should().Be("billing.plan-required", "the refusal is the failing case's own, not one of the toolkit's");
        refusal.Arguments["Plan"].Should().Be("pro");
        asked.Entries.Should().Equal(["board a", "plan pro"], "what follows the first refusal is not asked");
    }

    // ------------------------------------------------------------------ Any

    [Fact]
    public async Task Any_lets_the_first_case_that_holds_through_and_asks_no_case_after_it()
    {
        var (checks, asked) = Checks();

        await checks.RequireAsync(new ExportBoard(AccessRequirement.Any(Refuses("a"), new OnPlan("pro", Met: true), Refuses("b"))), Cancellation);

        asked.Entries.Should().Equal(["board a", "plan pro"], "the second case let the caller through, so the third was never asked");
    }

    [Fact]
    public async Task Any_that_no_case_lets_through_is_one_access_refused_with_each_case_s_reason_in_order()
    {
        var (checks, asked) = Checks();

        var refusal = await RefusedAsync(checks, AccessRequirement.Any(Refuses("a"), new OnPlan("pro", Met: false)));

        refusal.Code.Should().Be(ToolkitRefusals.Refused, "one refusal, of the toolkit's, for a request none of whose ways was met");
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        Reasons(refusal).Should().Equal(
            new RefusalReason("boards.not-permitted", new Dictionary<string, object?> { ["Board"] = "a" }),
            new RefusalReason("billing.plan-required", new Dictionary<string, object?> { ["Plan"] = "pro" }));
        refusal.Message.Should().Be("Access to this was refused: boards.not-permitted (Board = a); billing.plan-required (Plan = pro).", "a log line names every reason");
        asked.Entries.Should().Equal("board a", "plan pro");
    }

    [Fact]
    public async Task Anything_but_a_refusal_ends_any_as_it_is()
    {
        // A lost race says the resource moved on, not that the caller may not: it is the answer, and no case after it is asked.
        var (checks, asked) = Checks();

        await FluentActions.Awaiting(() => checks.RequireAsync(new ExportBoard(AccessRequirement.Any(Refuses("a"), new Racing(), Met("b"))), Cancellation).AsTask())
            .Should().ThrowAsync<ConcurrencyConflictException>();
        asked.Entries.Should().Equal("board a", "racing");
    }

    // ------------------------------------------------------------------ nested, and of different owners

    [Fact]
    public async Task A_combination_holds_another_and_each_case_goes_to_its_own_check()
    {
        var (checks, asked) = Checks(CurrentUserSnapshot.User(Guid.NewGuid()));

        // Either both of a board and a plan, or the other board: the first holds.
        await checks.RequireAsync(new ExportBoard(AccessRequirement.Any(AccessRequirement.All(Met("a"), new OnPlan("pro", Met: true)), Refuses("b"))), Cancellation);
        asked.Entries.Should().Equal("board a", "plan pro");

        // A signed-in user, of the core's, and one of two boards: the core's case and the module's in one requirement.
        asked.Entries.Clear();
        await checks.RequireAsync(new ExportBoard(AccessRequirement.All(AccessRequirement.SignedIn(), AccessRequirement.Any(Refuses("a"), Met("b")))), Cancellation);
        asked.Entries.Should().Equal("board a", "board b");

        // The reason of a nested combination is its own refusal, with its own reasons.
        var refusal = await RefusedAsync(checks, AccessRequirement.Any(AccessRequirement.All(Met("a"), new OnPlan("pro", Met: false)), AccessRequirement.Any(Refuses("b"), Refuses("c"))));
        Reasons(refusal).Select(reason => reason.Code).Should().Equal("billing.plan-required", ToolkitRefusals.Refused);
        ((IReadOnlyList<RefusalReason>)Reasons(refusal)[1].Arguments[ToolkitRefusals.ReasonsArgument]!).Select(reason => reason.Code)
            .Should().Equal("boards.not-permitted", "boards.not-permitted");
    }

    [Fact]
    public async Task A_nested_combination_s_reasons_are_named_in_the_log_line_and_two_that_say_the_same_are_equal()
    {
        var (checks, _) = Checks();
        var requirement = AccessRequirement.Any(Refuses("a"), AccessRequirement.Any(Refuses("b"), new OnPlan("pro", Met: false)));

        var first = await RefusedAsync(checks, requirement);
        var second = await RefusedAsync(checks, requirement);

        first.Message.Should().Be(
            "Access to this was refused: boards.not-permitted (Board = a); access.refused (Reasons = [boards.not-permitted (Board = b); billing.plan-required (Plan = pro)]).",
            "a log line names the reasons of a nested combination too, not the type of the list they are kept in");
        Reasons(first).Should().Equal(Reasons(second), "two refusals of the same caller for the same request say the same, a nested combination's reasons compared one by one");
        Reasons(first)[1].GetHashCode().Should().Be(Reasons(second)[1].GetHashCode());
        Reasons(first)[1].Should().NotBe(
            new RefusalReason(ToolkitRefusals.Refused, new Dictionary<string, object?> { [ToolkitRefusals.ReasonsArgument] = new[] { new RefusalReason("boards.not-permitted") } }),
            "other reasons are another reason");
    }

    // ------------------------------------------------------------------ what the cases keep

    [Fact]
    public async Task A_refused_all_leaves_nothing_kept_of_a_case_that_passed_before_the_one_that_refused()
    {
        await using var provider = Provider(CurrentUserSnapshot.User(Guid.NewGuid()));
        await using var scope = provider.CreateAsyncScope();
        var request = new ExportBoard(AccessRequirement.All(Met("north"), new OnPlan("pro", Met: false)));

        await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardRequest>>().RequireAsync(request, Cancellation).AsTask())
            .Should().ThrowAsync<RefusalException>();

        FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<Checked<string>>().TakeFor(request))
            .Should().Throw<InvalidOperationException>("a request its checks refused has nothing kept for it, whichever of its cases passed before the one that refused");
        Checked<string>.FindAllInHand().Should().BeEmpty("and it is in nobody's hand");
    }

    [Fact]
    public async Task Any_keeps_what_the_case_that_let_the_caller_through_kept_and_nothing_of_a_branch_that_refused()
    {
        await using var provider = Provider(CurrentUserSnapshot.User(Guid.NewGuid()));
        await using var scope = provider.CreateAsyncScope();

        // The first branch passes its board and is refused its plan: what its board kept goes with it.
        var request = new ExportBoard(AccessRequirement.Any(AccessRequirement.All(Met("north"), new OnPlan("pro", Met: false)), Met("south")));
        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardRequest>>().RequireAsync(request, Cancellation);

        Checked<string>.FindAllInHand().Should().Equal(["south"], "the save of the handling is held to what the way the caller came through read");
        scope.ServiceProvider.GetRequiredService<Checked<string>>().TakeFor(request).Should().Be("south");
    }

    [Fact]
    public async Task A_passing_all_keeps_what_each_case_kept_and_hands_them_out_together_not_one_of_several()
    {
        await using var provider = Provider(CurrentUserSnapshot.User(Guid.NewGuid()));
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardRequest>>();
        var kept = scope.ServiceProvider.GetRequiredService<Checked<string>>();
        var request = new ExportBoard(AccessRequirement.All(Met("north"), Met("south")));

        // Sent twice, one pass after the other: what the later pass kept, both of it, and nothing of the first.
        await checks.RequireAsync(request, Cancellation);
        await checks.RequireAsync(request, Cancellation);

        Checked<string>.FindAllInHand().Should().Equal("north", "south");
        Checked<string>.TryFindInHand(board => board == "south", out var south).Should().BeTrue();
        south.Should().Be("south", "code of the handling finds the one it is about, the hold on the resource a save changes say");
        Checked<string>.TryFindInHand(out var first).Should().BeTrue();
        first.Should().Be("north", "without a question, the first that was kept");

        FluentActions.Invoking(() => kept.TakeFor(request)).Should().Throw<InvalidOperationException>()
            .WithMessage("2 of *were kept for this*TakeAllFor*", "which of two the handler means is not for the checks to guess");
        kept.TakeAllFor(request).Should().Equal(["north", "south"], "each case's, in the order the cases were asked");
        FluentActions.Invoking(() => kept.TakeAllFor(request)).Should().Throw<InvalidOperationException>("what was kept is handed out once");
    }

    // ------------------------------------------------------------------ who is calling, first

    [Fact]
    public async Task In_a_host_that_requires_explicit_callers_the_door_asks_who_is_calling_before_any_case_of_a_module_s_own()
    {
        var services = new ServiceCollection();
        services.RequireExplicitCurrentUser();
        services.AddSingleton(new Asked());
        services.AddAccessCheck<IBoardRequest, BoardCheck>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardRequest>>();

        // The boards' check never asks who is calling: the door does, once, before it, and for a request anyone may send.
        await FluentActions.Awaiting(() => checks.RequireAsync(new ExportBoard(Met("north")), Cancellation).AsTask())
            .Should().ThrowAsync<NoCurrentUserException>("work nobody began a caller for is a mistake to fix, whatever its request requires");
        await FluentActions.Awaiting(() => checks.RequireAsync(new ExportBoard(AccessRequirement.AllowAnonymous()), Cancellation).AsTask())
            .Should().ThrowAsync<NoCurrentUserException>();
        scope.ServiceProvider.GetRequiredService<Asked>().Entries.Should().BeEmpty("no case is asked of such work");

        using (TestUser.BeginAs(CurrentUserSnapshot.Anonymous))
        {
            await checks.RequireAsync(new ExportBoard(Met("north")), Cancellation);
        }
    }

    [Fact]
    public async Task A_case_s_check_keeps_for_the_handler_what_it_read_as_it_would_alone()
    {
        await using var provider = Provider(CurrentUserSnapshot.User(Guid.NewGuid()));
        await using var scope = provider.CreateAsyncScope();
        var request = new ExportBoard(AccessRequirement.All(AccessRequirement.SignedIn(), Met("north")));

        await scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardRequest>>().RequireAsync(request, Cancellation);

        scope.ServiceProvider.GetRequiredService<Checked<string>>().TakeFor(request).Should().Be("north");
    }

    // ------------------------------------------------------------------ fails closed

    [Fact]
    public async Task A_combination_with_a_case_nothing_decides_is_stopped_before_any_case_is_asked()
    {
        var (checks, asked) = Checks();
        var request = new ExportBoard(AccessRequirement.Any(Met("a"), new OnNothing()));

        checks.Decides(request.Requires).Should().BeFalse("a case without its check lets nobody through, whichever case would have let the caller in");
        checks.Decides(AccessRequirement.Any(Met("a"), AccessRequirement.All(new OnPlan("pro", true), AccessRequirement.SignedIn()))).Should().BeTrue();

        (await FluentActions.Awaiting(() => checks.RequireAsync(request, Cancellation).AsTask()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*declares 'CombinedRequirementTests.OnNothing', which none of the access checks registered for CombinedRequirementTests.IBoardRequest decides*");
        asked.Entries.Should().BeEmpty("nothing is asked of a combination that cannot be decided whole");
    }

    [Fact]
    public void A_combination_takes_a_case_or_more_and_none_that_opens_the_door()
    {
        FluentActions.Invoking(() => AccessRequirement.All()).Should().Throw<ArgumentException>().WithMessage("*takes at least one requirement*");
        FluentActions.Invoking(() => AccessRequirement.Any()).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => AccessRequirement.All(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AccessRequirement.Any(Met("a"), null!)).Should().Throw<ArgumentException>().WithMessage("*holds no null*");
        FluentActions.Invoking(() => AccessRequirement.Any(AccessRequirement.AllowAnonymous(), Met("a"))).Should().Throw<ArgumentException>()
            .WithMessage("*does not take AllowAnonymous()*", "anyone or a permission is anyone, and reads as if it were closed");
        FluentActions.Invoking(() => AccessRequirement.All(AccessRequirement.AllowAnonymous(), Met("a"))).Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ records, logs and the request in hand

    [Fact]
    public void Two_combinations_that_say_the_same_are_equal_and_a_log_names_their_cases()
    {
        var cases = new AccessRequirement[] { Met("a"), new OnPlan("pro", Met: true) };
        var all = AccessRequirement.All(cases);
        cases[0] = Refuses("z");

        all.Should().Be(AccessRequirement.All(Met("a"), new OnPlan("pro", Met: true)), "the cases are copied, and compared in order");
        all.GetHashCode().Should().Be(AccessRequirement.All(Met("a"), new OnPlan("pro", Met: true)).GetHashCode());
        all.Should().NotBe(AccessRequirement.All(new OnPlan("pro", Met: true), Met("a")), "the order is what is asked");
        all.Should().NotBe(AccessRequirement.Any(Met("a"), new OnPlan("pro", Met: true)), "all of them is not one of them");
        all.Cases.Should().Equal(Met("a"), new OnPlan("pro", Met: true));
        all.ToString().Should().Be("AllOf { Cases = [OnBoard { Name = a, Met = True }, OnPlan { Plan = pro, Met = True }] }");
    }

    [Fact]
    public async Task The_request_in_hand_asks_its_whole_combination_again_and_names_it_by_its_types()
    {
        var (checks, asked) = Checks(CurrentUserSnapshot.User(Guid.NewGuid()));
        var request = new ExportBoard(AccessRequirement.Any(Refuses("a"), AccessRequirement.All(AccessRequirement.SignedIn(), Met("b"))));

        await checks.RequireAsync(request, Cancellation);
        var hand = RequestInHand.Current!;
        asked.Entries.Clear();

        (await hand.StillPassesAsync(Cancellation)).Should().BeTrue();
        asked.Entries.Should().Equal(["board a", "board b"], "the same cases, asked of the same checks, now");
        hand.ToString().Should().Be(
            "CombinedRequirementTests.ExportBoard (AccessRequirement.Any(CombinedRequirementTests.OnBoard, AccessRequirement.All(AccessRequirement.SignedInUser, CombinedRequirementTests.OnBoard)))",
            "a log line names the cases by their types and nothing of their values");
    }

    [Fact]
    public async Task A_refusal_of_one_way_or_another_is_written_as_json_with_its_reasons()
    {
        var (checks, _) = Checks();
        var refusal = await RefusedAsync(checks, AccessRequirement.Any(AccessRequirement.Own(Guid.NewGuid()), Refuses("a")));

        // As an edge writes a refusal's arguments, a problem's or an error's: each reason an object, its code and its arguments.
        var json = JsonSerializer.Serialize(refusal.Arguments, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Be("""{"Reasons":[{"code":"access.not-own","arguments":{}},{"code":"boards.not-permitted","arguments":{"Board":"a"}}]}""");
        new RefusalReason("tenancy.not-permitted", new Dictionary<string, object?> { ["permission"] = "tenancy.seats.manage" })
            .Should().Be(new RefusalReason("tenancy.not-permitted", new Dictionary<string, object?> { ["Permission"] = "tenancy.seats.manage" }), "two reasons that say the same are equal, the names matched as arguments are");
        FluentActions.Invoking(() => new RefusalReason(" ")).Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<RefusalException> RefusedAsync(AccessChecks<IBoardRequest> checks, AccessRequirement requirement)
        => (await FluentActions.Awaiting(() => checks.RequireAsync(new ExportBoard(requirement), Cancellation).AsTask()).Should().ThrowAsync<RefusalException>()).Which;

    private static IReadOnlyList<RefusalReason> Reasons(RefusalException refusal)
        => refusal.Arguments[ToolkitRefusals.ReasonsArgument].Should().BeAssignableTo<IReadOnlyList<RefusalReason>>().Subject;

    /// <summary>A set of the core's check, the boards' and billing's, made by hand, as a test of a module makes one.</summary>
    private static (AccessChecks<IBoardRequest> Checks, Asked Asked) Checks(CurrentUserSnapshot? caller = null)
    {
        var asked = new Asked();
        var checks = new AccessChecks<IBoardRequest>(
        [
            new CurrentUserAccessCheck(new CurrentUser(source: new Answering(caller ?? CurrentUserSnapshot.Anonymous))),
            new BoardCheck(asked, new Checked<string>()),
            new PlanCheck(asked),
        ]);
        return (checks, asked);
    }

    private static ServiceProvider Provider(CurrentUserSnapshot caller)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new CurrentUser(source: new Answering(caller)));
        services.AddSingleton(new Asked());
        services.AddAccessCheck<IBoardRequest, BoardCheck>();
        services.AddAccessCheck<IBoardRequest, PlanCheck>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
