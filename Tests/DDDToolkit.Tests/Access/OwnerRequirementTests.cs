using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Tests.Access;

/// <summary>A board's id, for the requests about a board's owner.</summary>
[EntityId<Guid>]
public readonly partial record struct OwnedBoardId;

/// <summary>
/// A request about the owner's own aggregate: <see cref="AccessRequirement.OwnerOf{TId}"/> names the aggregate, and the
/// core's check has its owner read by the <see cref="IAggregateOwners{TId}"/> registered for the id, then compares it with
/// the caller's own of the owner's type, which the <see cref="ICallersOwn{TValue}"/> of that type says. The storage here is
/// a list of the test's own; what Entity Framework reads, as the caller, is its package's tests'.
/// </summary>
public class OwnerRequirementTests
{
    /// <summary>What every request of the test's module implements.</summary>
    public interface IBoardsRequest : IRequireAccess;

    /// <summary>A request that requires what it is given.</summary>
    public sealed record EditBoard(AccessRequirement Requires) : IBoardsRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>An application's own user id, over the id its users' tokens name.</summary>
    public readonly record struct UserId(Guid Value);

    /// <summary>The application's handle: the caller's own user id is the one over the id its token names.</summary>
    public sealed class CallersUser : ICallersOwn<UserId>
    {
        public UserId? Find(Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
    }

    /// <summary>The boards, and who owns each: what a storage reads, kept in a list, and how often it was asked.</summary>
    public sealed class Boards<TOwner>(Dictionary<OwnedBoardId, TOwner?> rows) : IAggregateOwners<OwnedBoardId>
        where TOwner : struct, IEquatable<TOwner>
    {
        public int Read { get; private set; }

        public Type OwnerType => typeof(TOwner);

        public ValueTask<AggregateOwner?> ReadAsync(OwnedBoardId aggregate, CancellationToken cancellationToken)
        {
            Read++;
            return ValueTask.FromResult(rows.TryGetValue(aggregate, out var owner) ? (owner is { } some ? new AggregateOwner(some) : AggregateOwner.Nobody) : null);
        }
    }

    /// <summary>A case of the module's own: a key, held by nobody.</summary>
    public sealed record HoldsKey(string Key) : AccessRequirement;

    /// <summary>Decides the module's key, held by nobody, and says it was asked.</summary>
    public sealed class KeyCheck(List<string> asked) : IAccessCheck
    {
        public bool Decides(AccessRequirement requirement) => requirement is HoldsKey;

        public ValueTask RequireAsync(AccessRequirement requirement, IRequireAccess request, CancellationToken cancellationToken)
        {
            asked.Add(((HoldsKey)requirement).Key);
            return ValueTask.FromException(new RefusalException("boards.not-permitted", RefusalKind.NotPermitted, "That takes the key."));
        }
    }

    /// <summary>An accessor that answers the caller a test gives it.</summary>
    private sealed class Answering(Caller caller) : ICallerAccessor
    {
        public Caller Current => caller;
    }

    private static readonly Guid Ann = Guid.Parse("3f9a1e2c-6b7d-4c8e-9a10-5d2e7f4b8c61");

    private static readonly Guid Bob = Guid.Parse("7c2d9e4f-1a3b-4d5e-8f60-2b1a9c8d7e53");

    private static readonly OwnedBoardId AnnsBoard = new(Guid.Parse("0d5c6a6e-8d5f-4b3e-9f60-1a2b3c4d5e01"));

    private static readonly OwnedBoardId BobsBoard = new(Guid.Parse("0d5c6a6e-8d5f-4b3e-9f60-1a2b3c4d5e02"));

    private static readonly OwnedBoardId NobodysBoard = new(Guid.Parse("0d5c6a6e-8d5f-4b3e-9f60-1a2b3c4d5e03"));

    private static readonly OwnedBoardId NoBoard = new(Guid.Parse("0d5c6a6e-8d5f-4b3e-9f60-1a2b3c4d5e04"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Boards<UserId> UsersBoards() => new(new()
    {
        [AnnsBoard] = new UserId(Ann),
        [BobsBoard] = new UserId(Bob),
        [NobodysBoard] = null,
    });

    [Fact]
    public async Task The_owner_of_a_board_is_let_through_and_anybody_else_is_not_own()
    {
        var boards = UsersBoards();
        using var provider = Provider(Caller.User(Ann), boards);
        using var scope = provider.CreateScope();

        Checks(scope).Decides(AccessRequirement.OwnerOf(AnnsBoard)).Should().BeTrue("a storage reads the owner, and the caller's own of its type is said");
        await Checks(scope).RequireAsync(new EditBoard(AccessRequirement.OwnerOf(AnnsBoard)), Cancellation);
        boards.Read.Should().Be(1, "one row is read, once");

        var refusal = await RefusedAsync(scope, AccessRequirement.OwnerOf(BobsBoard));
        refusal.Code.Should().Be(ToolkitRefusals.NotOwn, "Bob's board is not Ann's");
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Arguments.Should().BeEmpty("the board is the request's, and the caller sent it");
    }

    [Fact]
    public async Task A_board_whose_owner_is_null_is_nobody_s_and_the_owner_alone_lets_nobody_through()
    {
        using var provider = Provider(Caller.User(Ann), UsersBoards());
        using var scope = provider.CreateScope();

        (await RefusedAsync(scope, AccessRequirement.OwnerOf(NobodysBoard))).Code.Should().Be(ToolkitRefusals.NotOwn, "a row whose owner is null is nobody's, and nobody is Ann");
    }

    [Fact]
    public async Task A_board_there_is_no_row_of_for_the_caller_is_not_found()
    {
        using var provider = Provider(Caller.User(Ann), UsersBoards());
        using var scope = provider.CreateScope();

        var refusal = await RefusedAsync(scope, AccessRequirement.OwnerOf(NoBoard));
        refusal.Code.Should().Be(ToolkitRefusals.NotFound, "a row the storage does not answer, whether none was made or the caller may not see it, is not found");
        refusal.Kind.Should().Be(RefusalKind.NotFound);
        refusal.Message.Should().Be("That was not found, or it is not one you can see.");
    }

    [Theory]
    [InlineData("a caller who did not sign in")]
    [InlineData("the application itself")]
    [InlineData("the application in a scope")]
    public async Task A_caller_with_no_own_of_the_owner_s_type_is_not_own_and_nothing_is_read(string kind)
    {
        var caller = kind switch
        {
            "a caller who did not sign in" => Caller.Anonymous,
            "the application itself" => Caller.System,
            _ => Caller.SystemIn("boards"),
        };
        var boards = UsersBoards();
        using var provider = Provider(caller, boards);
        using var scope = provider.CreateScope();

        (await RefusedAsync(scope, AccessRequirement.OwnerOf(NoBoard))).Code.Should().Be(ToolkitRefusals.NotOwn, "{0} owns nothing, whatever the row says", kind);
        boards.Read.Should().Be(0, "a database that checks every row may refuse such a caller the table outright, so nothing is sent for it");
    }

    [Fact]
    public async Task The_owner_or_the_key_asks_the_key_where_the_board_is_somebody_else_s_nobody_s_or_not_found()
    {
        var asked = new List<string>();
        using var provider = Provider(Caller.User(Ann), UsersBoards(), services => services.AddSingleton(asked).AddAccessCheck<IBoardsRequest, KeyCheck>());
        using var scope = provider.CreateScope();
        AccessRequirement OwnerOrTheKey(OwnedBoardId board) => AccessRequirement.Any(AccessRequirement.OwnerOf(board), new HoldsKey("boards.manage"));

        await Checks(scope).RequireAsync(new EditBoard(OwnerOrTheKey(AnnsBoard)), Cancellation);
        asked.Should().BeEmpty("her own board is hers, and the key is not asked");

        foreach (var (board, reason) in new[] { (BobsBoard, ToolkitRefusals.NotOwn), (NobodysBoard, ToolkitRefusals.NotOwn), (NoBoard, ToolkitRefusals.NotFound) })
        {
            var refusal = await RefusedAsync(scope, OwnerOrTheKey(board));
            refusal.Code.Should().Be(ToolkitRefusals.Refused);
            ((IReadOnlyList<RefusalReason>)refusal.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(each => each.Code)
                .Should().Equal([reason, "boards.not-permitted"], "the owner's way through refused, and the key was asked after it");
        }

        asked.Should().Equal("boards.manage", "boards.manage", "boards.manage");
    }

    [Fact]
    public async Task An_owner_that_is_a_guid_is_the_caller_s_verified_identity_with_nothing_registered()
    {
        using var provider = Provider(Caller.User(Ann), new Boards<Guid>(new() { [AnnsBoard] = Ann, [BobsBoard] = Bob }));
        using var scope = provider.CreateScope();

        await Checks(scope).RequireAsync(new EditBoard(AccessRequirement.OwnerOf(AnnsBoard)), Cancellation);
        (await RefusedAsync(scope, AccessRequirement.OwnerOf(BobsBoard))).Code.Should().Be(ToolkitRefusals.NotOwn);
    }

    [Fact]
    public async Task Without_a_reader_of_the_owner_nothing_decides_the_case_and_the_registration_is_named()
    {
        using var provider = Provider(Caller.User(Ann), boards: null);
        using var scope = provider.CreateScope();

        Checks(scope).Decides(AccessRequirement.OwnerOf(AnnsBoard)).Should().BeFalse("a requirement nothing checks lets nobody through, the owner included");
        (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new EditBoard(AccessRequirement.OwnerOf(AnnsBoard)), Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*declares 'AccessRequirement.OwnedAggregate<OwnedBoardId>'*services.AddDDDToolkitEntityFramework()*IAggregateOwners<TId>*");

        // Nor in a set made by hand without the scope's services, where nothing could be registered.
        var byHand = new AccessChecks<IBoardsRequest>([new CallerAccessCheck(new Answering(Caller.User(Ann)))]);
        byHand.Decides(AccessRequirement.OwnerOf(AnnsBoard)).Should().BeFalse();
    }

    [Fact]
    public async Task An_owner_of_a_type_nothing_says_the_caller_s_own_of_stops_the_request_and_names_the_registration()
    {
        using var provider = Provider(Caller.User(Ann), UsersBoards(), withHandle: false);
        using var scope = provider.CreateScope();

        (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new EditBoard(AccessRequirement.OwnerOf(AnnsBoard)), Cancellation).AsTask())
                .Should().ThrowAsync<InvalidOperationException>("no answer of the owner lets anybody through where nothing says whose it is"))
            .WithMessage("*OwnedBoardId is a OwnerRequirementTests.UserId*services.AddCallersOwn<OwnerRequirementTests.UserId, TCallersOwn>()*");
    }

    [Fact]
    public void Two_requirements_about_the_same_aggregate_are_equal()
    {
        AccessRequirement.OwnerOf(AnnsBoard).Should().Be(AccessRequirement.OwnerOf(AnnsBoard));
        AccessRequirement.OwnerOf(AnnsBoard).Should().NotBe(AccessRequirement.OwnerOf(BobsBoard));
        AccessRequirement.OwnerOf(AnnsBoard).Aggregate.Should().Be(AnnsBoard);
        AccessRequirement.OwnerOf(AnnsBoard).Should().NotBe(AccessRequirement.Own(AnnsBoard), "the owner of the board is not the board itself");
        new AggregateOwner(new UserId(Ann)).Should().Be(new AggregateOwner(new UserId(Ann)));
        AggregateOwner.Nobody.Owner.Should().BeNull();
    }

    private static async Task<RefusalException> RefusedAsync(IServiceScope scope, AccessRequirement requirement)
        => (await FluentActions.Awaiting(() => Checks(scope).RequireAsync(new EditBoard(requirement), Cancellation).AsTask()).Should().ThrowAsync<RefusalException>()).Which;

    private static AccessChecks<IBoardsRequest> Checks(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AccessChecks<IBoardsRequest>>();

    private static ServiceProvider Provider(Caller caller, IAggregateOwners<OwnedBoardId>? boards, Action<IServiceCollection>? register = null, bool withHandle = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICallerAccessor>(new Answering(caller));
        if (boards is not null)
        {
            services.AddSingleton(boards);
        }

        if (withHandle)
        {
            services.AddCallersOwn<UserId, CallersUser>();
        }

        register?.Invoke(services);
        services.AddAccessChecks<IBoardsRequest>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
