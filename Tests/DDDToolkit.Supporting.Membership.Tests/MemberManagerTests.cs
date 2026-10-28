using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The member manager the generator writes for a member class, <c>DocumentShareManager</c> for the host's documents: what
/// it checks, in order, before it changes the members, the hooks it calls around each change, the save of a change to
/// the caller's own place, and the order it lists members in. Over the access manager a stub storage answers for and a
/// store kept in memory, so the order of the questions is what the tests see.
/// </summary>
public sealed class MemberManagerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Author = new(new Guid("00000000-0000-0000-0000-000000000004"));

    private static readonly UserId Reader = new(new Guid("00000000-0000-0000-0000-000000000002"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Document Minutes() => new(DocumentId.CreateSequential(), "Minutes", Author, Now);

    private static DocumentShareManager Manager(
        StubAccess<Document, DocumentId> access,
        StubMemberStore<Document, DocumentId, UserId> store,
        IMemberDirectory<DocumentId, UserId>? directory = null,
        IMemberRolePolicy<DocumentId, NamedRole>? policy = null,
        ICurrentUser? user = null)
        => new(access, store, user ?? TestUser, new FixedClock(Now), directory: directory, policy: policy);

    // ---------------------------------------------------------------- what a change checks, in order

    [Fact]
    public async Task A_member_is_added_by_whoever_holds_the_permission_that_manages_the_members_and_the_resource_raises_its_events()
    {
        var document = Minutes();
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.Share, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId> { CurrentMember = Author }.With(document);

        var answered = await Manager(access, store).AddAsync(document.Id, Reader, DocumentMembership.Onlooker, until: Now.AddDays(7), Cancellation);

        answered.Should().BeSameAs(document, "the manager answers the resource it loaded, changed and saved");
        var share = document.Shares.Single(each => each.MemberId == Reader);
        (share.StartsAt, share.EndsAt, share.AddedBy).Should().Be((Now, Now.AddDays(7), Author));
        share.Roles.Single().RoleId.Should().Be(DocumentMembership.Onlooker);

        // The rules mark the share permission as the one that manages the members: that is what was asked.
        access.Asked.Should().Equal("caller", "hold " + DocumentPermissions.Share);
        store.Saves.Should().ContainSingle();

        // The events are the document's own, raised in its hooks: the member, then its role.
        Said(document.DomainEvents.Skip(1)).Should().Equal(Said([new DocumentShared(document.Id, Reader), new DocumentRoleGiven(document.Id, Reader, DocumentMembership.Onlooker)]));
    }

    [Fact]
    public async Task The_caller_is_refused_before_anything_is_admitted_loaded_or_changed()
    {
        var document = Minutes();
        var directory = new Directory(Reader);
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);

        // A document the caller sees without the permission: not-permitted, naming it.
        var sees = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.View, MemberVia.Members));
        (await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => Manager(sees, store, directory).AddAsync(document, Reader, null, null, Cancellation)))
            .Arguments.Should().Contain("Permission", DocumentPermissions.Share);

        // A document the caller does not see: not-found, as for one that does not exist.
        var blind = new StubAccess<Document, DocumentId>(DocumentMembership.Rules);
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => Manager(blind, store, directory).RemoveAsync(document.Id, Reader, Cancellation));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => Manager(blind, store, directory).NameOwnerAsync(document.Id, Reader, FormerOwner.Leaves, Cancellation));

        // A caller that is nobody is refused with its own refusal, first of all.
        var nobody = new StubAccess<Document, DocumentId>(DocumentMembership.Rules) { Nobody = new RefusalException("tenancy.no-seat", RefusalKind.NotPermitted, "No seat.") };
        (await FluentActions.Awaiting(() => Manager(nobody, store, directory).GiveRoleAsync(document.Id, Reader, DocumentMembership.Onlooker, null, Cancellation))
            .Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be("tenancy.no-seat");

        directory.Asked.Should().BeEmpty("who may be a member is asked once the caller may make the change");
        document.Shares.Should().ContainSingle("the owner, and nobody else");
        store.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task Who_may_be_a_member_and_which_role_goes_to_one_are_asked_after_the_caller_and_before_the_change()
    {
        var document = Minutes();
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.Share, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);
        var left = UserId.CreateSequential();

        // Somebody the application's directory does not know as active.
        var refusal = await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.MemberNotActive,
            () => Manager(access, store, new Directory(Reader)).AddAsync(document.Id, left, null, null, Cancellation));
        (refusal.Code, refusal.Kind).Should().Be(("documents.member-not-active", RefusalKind.Conflict));
        refusal.Arguments.Should().Contain("Member", left);

        // A role the rules do not declare, and one the application's own rule keeps off a member list.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.RoleNotForMembers, () => Manager(access, store).AddAsync(document.Id, Reader, new NamedRole("editor"), null, Cancellation));
        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => Manager(access, store, policy: new NoManagers()).AddAsync(document.Id, Reader, DocumentMembership.Manager, null, Cancellation));

        // The old owner's role is asked as any role given is.
        await Refused.WithCodeAsync(
            DocumentRefusals.Membership,
            MembershipRefusals.RoleNotForMembers,
            () => Manager(access, store).NameOwnerAsync(document.Id, Reader, FormerOwner.StaysAs(new NamedRole("editor")), Cancellation));

        // What was given once can always be taken back: taking a role asks neither.
        await Manager(access, store).AddAsync(document.Id, Reader, DocumentMembership.Manager, null, Cancellation);
        await Manager(access, store, new Directory(), new NoManagers()).TakeRoleAsync(document.Id, Reader, DocumentMembership.Manager, Cancellation);
        document.Shares.Single(each => each.MemberId == Reader).Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task The_resources_guard_runs_in_front_of_every_change_and_its_rules_after()
    {
        var document = Minutes();
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.Share, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);
        var manager = Manager(access, store);

        // The member list's own rules, under the resource's codes.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.MemberNotFound, () => manager.GiveRoleAsync(document.Id, Reader, DocumentMembership.Onlooker, null, Cancellation));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.OwnerProtected, () => manager.RemoveAsync(document.Id, Author, Cancellation));
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.InvalidPeriod, () => manager.AddAsync(document.Id, Reader, null, Now, Cancellation));

        // The document's guard, OnChangingMembers, refuses an archived document every change, before the list.
        document.MoveToArchive();
        foreach (var change in new Func<Task>[]
                 {
                     () => manager.AddAsync(document.Id, Reader, null, null, Cancellation),
                     () => manager.GiveRoleAsync(document.Id, Author, DocumentMembership.Onlooker, null, Cancellation),
                     () => manager.TakeRoleAsync(document.Id, Reader, DocumentMembership.Onlooker, Cancellation),
                     () => manager.RemoveAsync(document.Id, Author, Cancellation),
                     () => manager.NameOwnerAsync(document.Id, Author, FormerOwner.Leaves, Cancellation),
                 })
        {
            (await FluentActions.Awaiting(change).Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(DocumentRefusals.Archived);
        }

        store.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task Naming_an_owner_needs_the_permission_that_names_the_owner_and_sets_the_owner()
    {
        var keeper = new StaffCode("K-001");
        var clerk = new StaffCode("C-014");
        var folder = new Folder(new FolderId(7), keeper, Now);
        folder.Admit(clerk, FolderMembership.Clerk, MemberPeriod.Open(Now), Now, by: keeper);
        var store = new StubMemberStore<Folder, FolderId, StaffCode>().With(folder);

        // The folder's staff permission manages its members, and names no keeper.
        var staff = new StubAccess<Folder, FolderId>(FolderMembership.Rules).Sees(folder.Id, version: 3, (FolderPermissions.Staff, MemberVia.Members));
        (await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotPermitted, () => new FolderMemberManager(staff, store, TestUser, new FixedClock(Now)).NameOwnerAsync(folder.Id, clerk, FormerOwner.StaysAs(FolderMembership.Clerk), Cancellation)))
            .Arguments.Should().Contain("Permission", FolderPermissions.HandOver);
        folder.Keeper.Should().Be(keeper);

        var handOver = new StubAccess<Folder, FolderId>(FolderMembership.Rules).Sees(folder.Id, version: 3, (FolderPermissions.HandOver, MemberVia.Above));
        var named = await new FolderMemberManager(handOver, store, TestUser, new FixedClock(Now)).NameOwnerAsync(folder, clerk, FormerOwner.StaysAs(FolderMembership.Clerk), Cancellation);

        named.Keeper.Should().Be(clerk, "naming an owner sets the property marked [Owner]");
        folder.Staff.Single(each => each.MemberId == keeper).Roles.Single().RoleId.Should().Be(FolderMembership.Clerk);
        handOver.Asked.Should().Equal("caller", "hold " + FolderPermissions.HandOver);
    }

    [Fact]
    public async Task A_document_handed_over_raises_what_its_hook_says()
    {
        var document = Minutes();
        document.ShareWith(Reader, DocumentMembership.Contributor, MemberPeriod.Open(Now), Now, Author);
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.Share, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);
        var before = document.DomainEvents.Count;

        await Manager(access, store).NameOwnerAsync(document.Id, Reader, FormerOwner.StaysAs(DocumentMembership.Onlooker), Cancellation);

        document.OwnerId.Should().Be(Reader);
        Said(document.DomainEvents.Skip(before)).Should().Equal(Said(
        [
            new DocumentRoleTaken(document.Id, Reader, DocumentMembership.Contributor),
            new DocumentRoleGiven(document.Id, Author, DocumentMembership.Onlooker),
            new DocumentHandedOver(document.Id, Author, Reader),
        ]));
    }

    [Fact]
    public async Task A_resource_that_marks_no_permission_for_a_change_lets_only_system_work_make_it()
    {
        var owner = UserId.CreateSequential();
        var shed = new Shed(ShedId.CreateSequential(), "Tools", owner, Now);
        var access = new StubAccess<Shed, ShedId>(ShedMembership.Rules).Sees(shed.Id, version: 0, (ShedPermissions.Open, MemberVia.Members));
        var store = new StubMemberStore<Shed, ShedId, UserId>().With(shed);
        var manager = new ShedHandManager(access, store, TestUser, new FixedClock(Now));

        using (TestAccess.BeginAs(owner.Value))
        {
            var refused = await Refused.WithCodeAsync(ShedMembership.Codes, MembershipRefusals.NotPermitted, () => manager.AddAsync(shed.Id, Reader, null, null, Cancellation));
            refused.Message.Should().Contain("ManagesMembers").And.Contain("Only system work");
            (await Refused.WithCodeAsync(ShedMembership.Codes, MembershipRefusals.NotPermitted, () => manager.NameOwnerAsync(shed.Id, Reader, FormerOwner.Leaves, Cancellation)))
                .Message.Should().Contain("NamesOwner");
        }

        using (TestAccess.BeginSystem())
        {
            await manager.AddAsync(shed.Id, Reader, null, null, Cancellation);
        }

        shed.Hands.Select(hand => hand.MemberId).Should().Equal(owner, Reader);
        access.Asked.Should().BeEmpty("with no permission to ask about, the caller's kind alone decides");
    }

    // ---------------------------------------------------------------- leaving, and the caller's own place

    [Fact]
    public async Task A_member_leaves_without_a_permission_and_the_owner_does_not()
    {
        var document = Minutes();
        document.ShareWith(Reader, DocumentMembership.Contributor, MemberPeriod.Open(Now), Now, Author);
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.View, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);

        // The owner stays until somebody else is named owner, and somebody who is no member has no place to leave.
        store.CurrentMember = Author;
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.OwnerProtected, () => Manager(access, store).LeaveAsync(document.Id, Cancellation));
        store.CurrentMember = null;
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.MemberNotFound, () => Manager(access, store).LeaveAsync(document.Id, Cancellation));

        // A member leaves a document it sees, holding nothing that manages the members.
        store.CurrentMember = Reader;
        await Manager(access, store).LeaveAsync(document, Cancellation);
        document.Shares.Select(share => share.MemberId).Should().Equal(Author);
        Said(document.DomainEvents.TakeLast(1)).Should().Equal(Said([new DocumentUnshared(document.Id, Reader)]));

        // A document the caller does not see is not there to leave.
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => Manager(new StubAccess<Document, DocumentId>(DocumentMembership.Rules), store).LeaveAsync(document.Id, Cancellation));
    }

    [Fact]
    public async Task A_change_to_the_callers_own_place_is_saved_as_the_resources_own_work_only_for_the_request_in_hand_that_read_the_resource()
    {
        var document = Minutes();
        document.ShareWith(Reader, DocumentMembership.Manager, MemberPeriod.Open(Now), Now, Author);
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.Share, MemberVia.Members), (DocumentPermissions.View, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId> { CurrentMember = Reader }.With(document);

        await using var provider = new ServiceCollection()
            .AddSingleton<MembershipAccessManager<Document>>(access)
            .AddSingleton<ICurrentUser>(TestUser)
            .AddAccessCheck<IFilingRequest, MemberAccessCheck<Document, DocumentId>>()
            .BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var checks = scope.ServiceProvider.GetRequiredService<AccessChecks<IFilingRequest>>();

        using (TestAccess.BeginAs(Reader.Value))
        {
            // Outside the handling of a request, a change to the caller's own place saves as the caller.
            await Manager(access, store).TakeRoleAsync(document.Id, Reader, DocumentMembership.Manager, Cancellation);
            store.Saves.Single()!.Kind.Should().Be(CurrentUserKind.User);

            // The request in hand read the document: the manager takes its answer, asks nothing again, and saves the
            // caller's own place as the documents' own work. The documents' rules name no scope, so it has none.
            document.GiveRole(Reader, DocumentMembership.Manager, MemberPeriod.Open(Now), Now, Author);
            access.Asked.Clear();
            await checks.RequireAsync(new ShareDocument(document.Id, Reader), Cancellation);
            await Manager(access, store).RemoveAsync(document.Id, Reader, Cancellation);

            access.Asked.Should().Equal(["caller", "hold " + DocumentPermissions.Share], "the request's check asked once, and the manager took its answer");
            store.Saves[1]!.Kind.Should().Be(CurrentUserKind.System);
            TestUser.Snapshot.Kind.Should().Be(CurrentUserKind.User, "the work ends with the save");

            // A change to somebody else's place saves as the caller, request in hand or not.
            var author = Manager(access, store);
            document.ShareWith(UserId.CreateSequential(), MemberPeriod.Open(Now), Now, Author);
            var other = document.Shares.Last().MemberId;
            await checks.RequireAsync(new ShareDocument(document.Id, other), Cancellation);
            await author.RemoveAsync(document.Id, other, Cancellation);
            store.Saves[2]!.Kind.Should().Be(CurrentUserKind.User);

            // Nor where the unit of work holds anything else: that is not what the check let through.
            document.ShareWith(Reader, MemberPeriod.Open(Now), Now, Author);
            store.Besides.Add("Folder");
            await checks.RequireAsync(new ShareDocument(document.Id, Reader), Cancellation);
            await Manager(access, store).LeaveAsync(document.Id, Cancellation);
            store.Saves[3]!.Kind.Should().Be(CurrentUserKind.User);
        }
    }

    // ---------------------------------------------------------------- listing

    [Fact]
    public async Task The_members_are_listed_owner_first_then_by_when_they_start_then_by_member_and_each_ones_roles_by_start_then_by_role()
    {
        UserId first = new(new Guid("00000000-0000-0000-0000-000000000001"));
        UserId third = new(new Guid("00000000-0000-0000-0000-000000000003"));

        // The owner is the last to have joined and has the highest id: it comes first all the same.
        var document = new Document(DocumentId.CreateSequential(), "Minutes", Author, Now.AddDays(5));
        document.ShareWith(third, MemberPeriod.Open(Now.AddDays(1)), Now, by: Author);
        document.ShareWith(Reader, MemberPeriod.Open(Now.AddDays(-5)), Now, by: Author);
        document.ShareWith(first, MemberPeriod.Open(Now.AddDays(1)), Now, by: Author);
        document.GiveRole(Reader, DocumentMembership.Onlooker, MemberPeriod.Open(Now), Now, by: Author);
        document.GiveRole(Reader, DocumentMembership.Manager, MemberPeriod.Open(Now), Now, by: Author);
        document.GiveRole(Reader, DocumentMembership.Contributor, MemberPeriod.Between(Now.AddDays(-3), Now.AddDays(3)), Now, by: Author);

        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(document.Id, version: 0, (DocumentPermissions.View, MemberVia.Members));
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(document);

        var members = await Manager(access, store).ListAsync(document, Cancellation);

        members.Select(member => member.MemberId).Should().Equal(Author, Reader, first, third);
        members.Should().AllBeOfType<DocumentShare>("the members are answered as the application's own class, whole");

        // The contributor's role started first; the manager's and the onlooker's started together, and are told apart by role.
        members[1].Roles.Select(held => held.RoleId.Value).Should().Equal("contributor", "manager", "onlooker");

        // What counts at a moment the member says itself.
        members[1].AppliesAt(Now).Should().BeTrue();
        members[1].HoldsAt(DocumentMembership.Contributor, Now.AddDays(5)).Should().BeFalse();
        members[3].AppliesAt(Now).Should().BeFalse("a membership that has not started counts for nothing yet");
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("da-DK")]
    [InlineData("nl-NL")]
    public async Task Members_and_roles_known_by_a_text_are_listed_in_one_order_whatever_the_readers_language(string language)
    {
        // Everything starts at the same moment, so only the member and the role tell the rows apart. A language
        // puts "a" before "B", and Danish puts "aa" after "z"; the characters themselves say otherwise both times.
        var keeper = new StaffCode("K-001");
        var folder = new Folder(new FolderId(7), keeper, Now);
        foreach (var staff in new[] { "z-1", "aa-1", "a-2", "B-1" })
        {
            folder.Admit(new StaffCode(staff), FolderMembership.Visitor, MemberPeriod.Open(Now), Now, by: keeper);
        }

        foreach (var role in new[] { "aa", "z", "alpha", "Zulu" })
        {
            folder.GiveRole(new StaffCode("a-2"), new NamedRole(role), MemberPeriod.Open(Now), Now, by: keeper);
        }

        var access = new StubAccess<Folder, FolderId>(FolderMembership.Rules).Sees(folder.Id, version: 0, (FolderPermissions.Read, MemberVia.Members));
        var manager = new FolderMemberManager(access, new StubMemberStore<Folder, FolderId, StaffCode>().With(folder), TestUser, new FixedClock(Now));

        IReadOnlyList<FolderMember> members;
        using (CultureScope.Use(language))
        {
            members = await manager.ListAsync(folder.Id, Cancellation);
        }

        members.Select(member => member.MemberId.Value).Should().Equal(["K-001", "B-1", "a-2", "aa-1", "z-1"], "the owner first, then the members by the characters of their codes");
        members[2].Roles.Select(held => held.RoleId.Value).Should().Equal("Zulu", "aa", "alpha", "visitor", "z");
    }

    [Fact]
    public async Task The_members_of_several_resources_are_listed_for_those_the_caller_sees()
    {
        var seen = Minutes();
        var hidden = Minutes();
        seen.ShareWith(Reader, MemberPeriod.Open(Now), Now, by: Author);
        var access = new StubAccess<Document, DocumentId>(DocumentMembership.Rules).Sees(seen.Id, version: 0, (DocumentPermissions.View, MemberVia.Members));

        // The stub store answers what it holds; the store a package writes keeps to the reach the manager hands it.
        var store = new StubMemberStore<Document, DocumentId, UserId>().With(seen);
        var manager = Manager(access, store);

        var listed = await manager.ListAsync([seen.Id, hidden.Id, seen.Id], Cancellation);
        listed.Keys.Should().Equal(seen.Id);
        listed[seen.Id].Select(member => member.MemberId).Should().Equal(Author, Reader);

        (await manager.ListAsync(Array.Empty<DocumentId>(), Cancellation)).Should().BeEmpty();
        await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => manager.ListAsync(hidden.Id, Cancellation));
        await FluentActions.Awaiting(() => manager.ListAsync([.. Enumerable.Range(0, DocumentShareManager.MostResources + 1).Select(_ => DocumentId.CreateSequential())], Cancellation))
            .Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>What events say, without the id and the moment each one is stamped with.</summary>
    private static IEnumerable<object> Said(IEnumerable<DDDToolkit.Interfaces.IDomainEvent> events)
        => events.Select(raised => (object)(((DDDToolkit.BaseTypes.DomainEvent)raised) with { EventId = Guid.Empty, OccurredAt = default }));

    /// <summary>A directory that knows the users it was given as active, and says whom it was asked about.</summary>
    private sealed class Directory(params UserId[] active) : IMemberDirectory<DocumentId, UserId>
    {
        public List<UserId> Asked { get; } = [];

        public ValueTask<bool> IsActiveAsync(UserId member, CancellationToken cancellationToken)
        {
            Asked.Add(member);
            return ValueTask.FromResult(active.Contains(member));
        }
    }

    /// <summary>The application's own rule: the manager's role never goes to a member.</summary>
    private sealed class NoManagers : IMemberRolePolicy<DocumentId, NamedRole>
    {
        public ValueTask<bool> MayHoldAsync(NamedRole role, CancellationToken cancellationToken) => ValueTask.FromResult(role != DocumentMembership.Manager);
    }
}
