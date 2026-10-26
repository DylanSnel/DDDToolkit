using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The fact sits in the domain and the policy at the door: an aggregate root marks the property that names its owner
/// <c>[Owner]</c>, and a request that marks the aggregate's id <c>[Own]</c> with <c>[OwnOnly]</c>, or <c>[OrOwn]</c> beside
/// a key, is about that owner's own. The generator sees both, the domain through the project it references, and writes
/// <c>AccessRequirement.OwnerOf(request.Board)</c> where the id itself has no own. Each test compiles a module and asks
/// the request it made what it requires; what the core's check then reads is its own tests', and Entity Framework's.
/// </summary>
public class OwnerRequirementGenerationTests
{
    private const string Hint = "BillingAccessBehavior.";

    /// <summary>The ids of the module's aggregates that mark an owner, and of one that marks none.</summary>
    internal const string OwnedIds =
        """

        [EntityId<Guid>] public readonly partial record struct BoardId;
        [EntityId<Guid>] public readonly partial record struct CardId;
        [EntityId<Guid>] public readonly partial record struct NoteId;
        [EntityId<Guid>] public readonly partial record struct LedgerId;
        """;

    /// <summary>
    /// The aggregates: a board its user owns, a card a seat placed or nobody did, a note whose author is the caller's
    /// verified identity, and a ledger that names no owner.
    /// </summary>
    internal const string Owned =
        """

        [AggregateRoot<BoardId>]
        public sealed partial class Board
        {
            public Board(BoardId id, UserId owner) : base(id) => Owner = owner;

            [Owner] public UserId Owner { get; private set; }
        }

        [AggregateRoot<CardId>]
        public sealed partial class Card
        {
            public Card(CardId id, SeatId? placedBy) : base(id) => PlacedBy = placedBy;

            [Owner] public SeatId? PlacedBy { get; private set; }
        }

        [AggregateRoot<NoteId>]
        public sealed partial class Note
        {
            public Note(NoteId id, Guid author) : base(id) => Author = author;

            [Owner] public Guid Author { get; private set; }
        }

        [AggregateRoot<LedgerId>]
        public sealed partial class Ledger
        {
            public Ledger(LedgerId id) : base(id)
            {
            }
        }
        """;

    /// <summary>The application's handle of its own user id, beside the requests, where the generator sees it.</summary>
    internal const string CallersUser =
        """
        public sealed class CallersUser : ICallersOwn<UserId>
        {
            public UserId? Find(DDDToolkit.Abstractions.Access.Caller caller) => caller.UserId is { } id ? new UserId(id) : null;
        }

        """;

    /// <summary>
    /// A project of the module's requests above its contracts and its domain, each a project of its own as an application
    /// has them: the aggregates and their owners are seen through metadata.
    /// </summary>
    internal static GeneratorTestHost OverTheDomain(string requests, string domain = Owned)
        => GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithReferencedAssembly(RequirementAttributeGenerationTests.Ids + OwnedIds, "Shop.Contracts")
            .WithReferencedProject("Shop.Domain", project => project.WithSource(RequirementAttributeGenerationTests.Domain + domain, "Domain.cs"), GeneratorTestHost.MemberListGenerators())
            .WithSource(RequirementAttributeGenerationTests.Usings + requests, "Requests.cs")
            .WithDependencyInjection();

    /// <summary>
    /// The same module in one project, its ids, its aggregates and its requests together, so a test can run what it
    /// compiled: an aggregate the project declares is read as one of a project it references.
    /// </summary>
    internal static GeneratorTestHost Module(string requests)
        => GeneratorTestHost.Create(RequirementAttributeGenerationTests.Billing, "Billing.cs")
            .WithTenancy()
            .WithMembershipAlone()
            .WithSource(RequirementAttributeGenerationTests.Ids + OwnedIds, "Ids.cs")
            .WithSource(RequirementAttributeGenerationTests.Usings + Owned, "Domain.cs")
            .WithSource(RequirementAttributeGenerationTests.Usings + requests, "Requests.cs")
            .WithDependencyInjection();

    private static object IdOf(EmittedAssembly emitted, string name, Guid value) => emitted.New("Shop.Billing." + name, value);

    private static AccessRequirement Asked(EmittedAssembly emitted, string typeName, params object?[] arguments)
        => ((IRequireAccess)emitted.New("Shop.Billing." + typeName, arguments)).RequiredAccess;

    [Fact]
    public void The_owner_alone_of_a_board_is_owner_of_the_board_its_request_names()
    {
        var result = Module(
            CallersUser
            + """
              [OwnOnly]
              public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.ArchiveBoard request => global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Board),",
            "the board's id has no own of its own, and the board marks its owner: the request is about that owner's own");

        var emitted = result.Emit();
        var board = Guid.NewGuid();
        var asked = Asked(emitted, "ArchiveBoard", IdOf(emitted, "BoardId", board));
        asked.GetType().Name.Should().Be("OwnedAggregate`1");
        asked.ToString().Should().Contain(board.ToString());
    }

    [Fact]
    public void The_owner_or_the_key_is_any_of_the_two_the_owner_first()
    {
        var result = Module(
            CallersUser
            + """
              [RequiresKey("boards.manage")]
              public sealed record RenameBoard([OrOwn] BoardId Board, string Name) : IBillingRequest;
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.RenameBoard request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Board), "
            + "global::DDDToolkit.Supporting.Tenancy.Access.TenancyAccess.ForTheWholeTenant(\"boards.manage\")),",
            "the owner reads one column of one row; the key only where the board is not the caller's");

        var emitted = result.Emit();
        var any = Asked(emitted, "RenameBoard", IdOf(emitted, "BoardId", Guid.NewGuid()), "Q3").Should().BeOfType<AccessRequirement.AnyOf>().Subject;
        any.Cases[0].GetType().Name.Should().Be("OwnedAggregate`1");
        any.Cases[1].Should().Be(TenancyAccess.ForTheWholeTenant("boards.manage"));
    }

    [Fact]
    public void An_owner_of_a_type_a_package_or_the_core_says_the_own_of_needs_no_handle_and_may_be_nullable()
    {
        // Tenancy says the caller's own of the seat id, the core of a Guid; a card nobody placed is nobody's, at the door.
        var result = OverTheDomain(
            """
            [OwnOnly] public sealed record MoveCard([Own] CardId Card) : IBillingRequest;
            [RequiresKey("notes.edit")] public sealed record EditNote([OrOwn] NoteId Note) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.MoveCard request => global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Card),");
        result.ShouldContain(Hint, "global::Shop.Billing.EditNote request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Note), ");
    }

    [Fact]
    public void The_own_of_the_id_itself_wins_over_the_owner_of_its_aggregate()
    {
        // An ICallersOwn of the id's own reads nothing, and says the caller's own of the id first: the line compares the
        // id, documented as the choice, and the owner the board marks is not read.
        var result = OverTheDomain(
            CallersUser
            + """
              public sealed class CallersBoard : ICallersOwn<BoardId>
              {
                  public BoardId? Find(DDDToolkit.Abstractions.Access.Caller caller) => null;
              }

              [OwnOnly]
              public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ArchiveBoard request => global::DDDToolkit.Access.AccessRequirement.Own(request.Board),");
    }

    [Fact]
    public void A_seat_tenancy_says_the_own_of_stays_the_caller_s_own_seat_whatever_it_marks()
    {
        // The seat is declared with the template Tenancy names its seat id by: the id is the caller's own seat, and an
        // [Owner] the seat may mark does not take its place.
        var result = OverTheDomain(
            "[OwnOnly] public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;",
            Owned.Replace("[AggregateRoot<LedgerId>]", "public sealed partial class Seat { [Owner] public Guid Holder { get; private set; } }\n\n[AggregateRoot<LedgerId>]", StringComparison.Ordinal)).RunCore();

        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.LeaveTenant request => global::DDDToolkit.Access.AccessRequirement.Own(request.Seat),");
    }

    [Fact]
    public void Beside_a_resource_the_owner_s_way_through_still_sees_the_resource()
    {
        // Take your own card off a document you see, or manage the document's members: it falls out of the caller's own
        // on a resource, the owner in the place of the value.
        var result = OverTheDomain(
            """
            [RequiresKey("documents.manage")]
            public sealed record UnpinCard([OnResource] DocumentId Document, [OrOwn] CardId Card) : IBillingRequest;
            """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(
            Hint,
            "global::Shop.Billing.UnpinCard request => global::DDDToolkit.Access.AccessRequirement.Any(global::DDDToolkit.Access.AccessRequirement.All(global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Card), "
            + "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.Sees(\"documents.manage\", request.Document)), "
            + "global::DDDToolkit.Supporting.Membership.Access.MemberAccess.On(\"documents.manage\", request.Document)),");
    }

    [Fact]
    public void What_the_owner_s_own_requires_already_is_counted_for_the_owner_alone()
    {
        // Only a seat in the tenant has a seat of its own there, so the card's owner is in the tenant, as the seat's own is.
        var result = OverTheDomain("[InTenant, OwnOnly] public sealed record MoveCard([Own] CardId Card) : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics("DDD00084");
        result.ShouldHaveDiagnostic("DDD00084", at: "InTenant").GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("carries [InTenant] beside [OwnOnly] with [Own] 'Card', which requires it already");
    }

    [Fact]
    public void The_project_says_the_aggregates_whose_owner_it_asks_about_for_the_start_up_check()
    {
        // The lines of [Own] and [OrOwn] on an aggregate's id, and a call the project writes itself, once each; a seat
        // is the caller's own seat, no owner of anything, and a project that asks about none says nothing.
        var result = Module(
            CallersUser
            + """
              [OwnOnly]
              public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;

              [RequiresKey("boards.manage")]
              public sealed record RenameBoard([OrOwn] BoardId Board) : IBillingRequest;

              public sealed record EditNote(NoteId Note) : IBillingRequest
              {
                  AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.Any(AccessRequirement.OwnerOf(Note), AccessRequirement.SignedIn());
              }

              [OwnOnly]
              public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;
              """).RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        var said = result.Source("DDDToolkit.AsksOwnerOf.");
        said.Should().Contain("[assembly: global::DDDToolkit.Access.AsksOwnerOfAttribute(typeof(global::Shop.Billing.BoardId))]")
            .And.Contain("[assembly: global::DDDToolkit.Access.AsksOwnerOfAttribute(typeof(global::Shop.Billing.NoteId))]")
            .And.NotContain("SeatId");

        result.Emit().Assembly.GetCustomAttributes(typeof(AsksOwnerOfAttribute), inherit: false).Cast<AsksOwnerOfAttribute>()
            .Select(static asked => asked.AggregateId.Name).Should().Equal(["BoardId", "NoteId"], "once each, in the order of their names");

        Module("[OwnOnly] public sealed record LeaveTenant([Own] SeatId Seat) : IBillingRequest;").RunCore()
            .AllSources.Should().NotContain("AsksOwnerOf", "a project that asks about no owner says nothing");
    }

    [Fact]
    public void An_aggregate_of_a_project_the_requests_reference_is_read_as_one_of_their_own()
    {
        var result = OverTheDomain(CallersUser + "[OwnOnly] public sealed record ArchiveBoard([Own] BoardId Board) : IBillingRequest;").RunCore();

        result.ShouldCompile();
        result.ShouldHaveExactlyDiagnostics();
        result.ShouldContain(Hint, "global::Shop.Billing.ArchiveBoard request => global::DDDToolkit.Access.AccessRequirement.OwnerOf(request.Board),", "the domain is seen through metadata, its attributes and all");
    }
}
