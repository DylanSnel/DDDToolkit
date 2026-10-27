using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The access questions of a resource whose members are users, answered from the database: what a caller
/// holds on a resource is what the rules say of the member rows as they are now, whoever asks and however the
/// permission is held.
/// </summary>
public sealed class MemberQuestionsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_holds_what_its_role_gives_now()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var minutes = await filing.Services.ReadAsync(data.Minutes);

        var edits = await filing.DocumentsAsync(TestCallers.User(data.Ben), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation));
        var shares = await filing.DocumentsAsync(TestCallers.User(data.Ben), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation));

        edits.Should().Be(new MemberHold<DocumentId>(data.Minutes, MemberVia.Members, minutes.Version, Until: null), "a contributor changes a document, and what was read is the document at its version");
        shares.Should().Be(new MemberHold<DocumentId>(data.Minutes, null, minutes.Version, Until: null), "a contributor sees the document without the permission that shares it");
    }

    [Fact]
    public async Task A_role_counts_only_now_and_only_inside_a_membership_that_counts_now()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Eve's membership ended: to her the document is not there, whatever role she held in it.
        (await filing.DocumentsAsync(TestCallers.User(data.Eve), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation))).Should().BeNull();

        // Gil's membership has not started.
        (await filing.DocumentsAsync(TestCallers.User(data.Gil), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation))).Should().BeNull();

        // Fay is a member now; the role that changes ended yesterday and the one that looks on starts tomorrow.
        var fay = await filing.DocumentsAsync(TestCallers.User(data.Fay), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation));
        fay.Should().NotBeNull();
        fay!.Via.Should().BeNull("a role that ended gives nothing");

        // Cy's membership and role both end next week: until then both count, and after it neither.
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        filing.Clock.Advance(TimeSpan.FromDays(8));
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull("the moment asked about is the clock's");

        // And by then Gil has started, and Fay looks on.
        (await filing.DocumentsAsync(TestCallers.User(data.Gil), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task A_period_counts_from_its_first_moment_and_no_longer_at_its_end()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var instant = TimeSpan.FromTicks(1);

        Task<MemberHold<DocumentId>?> AskAsync(UserId user, string permission)
            => filing.DocumentsAsync(TestCallers.User(user), access => access.HoldAsync(data.Minutes, permission, Cancellation));

        // Gil's membership, and his role in it, start tomorrow, to the instant: not a moment before, and from that moment on.
        filing.Clock.Advance(TimeSpan.FromDays(1) - instant);
        (await AskAsync(data.Gil, DocumentPermissions.Edit)).Should().BeNull("the membership has not started");
        (await AskAsync(data.Fay, DocumentPermissions.View))!.Via.Should().Be(MemberVia.Members, "Fay is a member all along");
        var faysRoles = await filing.DocumentsAsync(TestCallers.User(data.Fay), access => access.PermissionsOnAsync([data.Minutes], [DocumentPermissions.View, DocumentPermissions.Edit], Cancellation));
        faysRoles[data.Minutes].Should().BeEquivalentTo([DocumentPermissions.View], "her role that looks on has not started, and seeing is what being a member gives");

        filing.Clock.Advance(instant);
        (await AskAsync(data.Gil, DocumentPermissions.Edit))!.Via.Should().Be(MemberVia.Members, "a period counts at the moment it starts");

        // Cy's membership, and his role in it, end a week after the scenario's moment: they count up to that
        // moment, and at it they are over.
        filing.Clock.Advance(TimeSpan.FromDays(6) - instant);
        (await AskAsync(data.Cy, DocumentPermissions.View))!.Via.Should().Be(MemberVia.Members);

        filing.Clock.Advance(instant);
        filing.Clock.Now.Should().Be(data.Now.AddDays(7));
        (await AskAsync(data.Cy, DocumentPermissions.View)).Should().BeNull("a period no longer counts at the moment it ends");

        // The same two moments, read over the aggregates: the database compares what the member list compares.
        data.Documents[0].Shares.Single(share => share.MemberId == data.Cy).AppliesAt(filing.Clock.Now).Should().BeFalse();
        data.Documents[0].Shares.Single(share => share.MemberId == data.Gil).AppliesAt(data.Now.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public async Task A_role_ends_at_its_own_moment_while_its_membership_goes_on()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var instant = TimeSpan.FromTicks(1);
        var until = data.Now.AddHours(2);

        // Dee is a member with no end; she contributes for two hours.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(data.Now, until), data.Now, by: data.Ada));

        Task<MemberHold<DocumentId>?> AskAsync()
            => filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation));

        (await AskAsync())!.Via.Should().Be(MemberVia.Members, "a role counts at the moment it starts");

        filing.Clock.Advance(TimeSpan.FromHours(2) - instant);
        (await AskAsync())!.Via.Should().Be(MemberVia.Members);

        filing.Clock.Advance(instant);
        var after = await AskAsync();
        after.Should().NotBeNull("she is a member still");
        after!.Via.Should().BeNull("the role no longer counts at the moment it ends");
    }

    [Fact]
    public async Task Being_a_member_gives_the_permission_that_sees_and_no_other()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Dee is a member with no role.
        (await filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation)))!.Via.Should().BeNull();

        // A folder's rules name no permission for seeing: a member of staff with no role sees the folder, and holds nothing on it.
        foreach (var permission in FilingScenario.FolderPermissionsAsked)
        {
            var held = await filing.FoldersAsync(TestCallers.Staff(data.Temp), access => access.HoldAsync(data.Cabinet, permission, Cancellation));
            held.Should().NotBeNull();
            held!.Via.Should().BeNull("being on a folder gives " + permission + " to nobody");
        }
    }

    [Fact]
    public async Task The_owner_holds_every_permission_of_the_resource_by_owning_it_whatever_its_role_gives()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // The keeper's role is one the host declared, and it lists neither handing the folder on nor, as far as a
        // member's role goes, shredding. The keeper holds both, and every other permission of a folder, by keeping it.
        foreach (var permission in FolderMembership.Rules.Permissions.Select(permission => permission.Name))
        {
            (await filing.FoldersAsync(TestCallers.Staff(data.Keeper), access => access.HoldAsync(data.Cabinet, permission, Cancellation)))!.Via.Should().Be(MemberVia.Members, "the keeper holds " + permission);
        }

        // The clerk is on the cabinet and keeps the annex: what the keeper alone holds is the clerk's there, and not here.
        foreach (var permission in new[] { FolderPermissions.HandOver, FolderPermissions.Shred, FolderPermissions.Staff })
        {
            (await filing.FoldersAsync(TestCallers.Staff(data.Clerk), access => access.HoldAsync(data.Cabinet, permission, Cancellation)))!.Via.Should().BeNull();
            (await filing.FoldersAsync(TestCallers.Staff(data.Clerk), access => access.HoldAsync(data.Annex, permission, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        }

        // Somebody else given the keeper's role holds what that role gives a member, and not what owning gives.
        await filing.Services.ChangeAsync(data.Cabinet, folder => folder.GiveRole(data.Visitor, FolderMembership.Keeper, MemberPeriod.Open(data.Now), data.Now, by: data.Keeper));
        (await filing.FoldersAsync(TestCallers.Staff(data.Visitor), access => access.HoldAsync(data.Cabinet, FolderPermissions.Staff, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.FoldersAsync(TestCallers.Staff(data.Visitor), access => access.HoldAsync(data.Cabinet, FolderPermissions.HandOver, Cancellation)))!.Via.Should().BeNull();
        (await filing.FoldersAsync(TestCallers.Staff(data.Visitor), access => access.HoldAsync(data.Cabinet, FolderPermissions.Shred, Cancellation)))!.Via.Should().BeNull();

        // Ada owns the minutes and not the budget, where she looks on.
        (await filing.DocumentsAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.DocumentsAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Budget, DocumentPermissions.Share, Cancellation)))!.Via.Should().BeNull();

        // Ian holds the manager's role on the minutes, with every permission: what a role lists it gives, to anyone.
        (await filing.DocumentsAsync(TestCallers.User(data.Ian), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task The_owner_sees_its_resource_from_the_moment_the_resource_names_it_before_its_own_membership_begins()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var notes = DocumentId.CreateSequential();

        // Opened with the owner's membership from tomorrow: its member row does not count yet, the owner column does.
        await filing.Services.AsAsync(CurrentUserSnapshot.System, async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            context.Documents.Add(new Document(notes, "Notes", data.Hal, data.Now.AddDays(1)));
            await context.SaveChangesAsync(Cancellation);
        });

        var hal = TestCallers.User(data.Hal);
        var hold = await filing.DocumentsAsync(hal, access => access.HoldAsync(notes, DocumentPermissions.View, Cancellation));
        hold.Should().NotBeNull("the owner holds every permission of what it owns, and so sees it");
        (hold!.Via, hold.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await filing.DocumentsAsync(hal, access => access.PermissionsOnAsync([notes], FilingScenario.DocumentPermissionsAsked, Cancellation)))[notes]
            .Should().BeEquivalentTo(DocumentMembership.Rules.Permissions.Select(permission => permission.Name));

        var seen = await filing.Services.AsAsync(hal, async provider =>
        {
            var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().ReachSeen();
            return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).Select(document => document.Id).ToListAsync(Cancellation);
        });
        seen.Should().Equal(notes);

        // A member whose membership has not begun, and who owns nothing, still sees nothing: owning is what counts here.
        (await filing.DocumentsAsync(TestCallers.User(data.Gil), access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task A_permission_that_is_none_of_the_resources_is_held_by_nobody()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Not by the owner, who holds the permissions of the resource and no other, and not through any role.
        (await filing.DocumentsAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Minutes, "documents.unheard-of", Cancellation)))!.Via.Should().BeNull();
        (await filing.FoldersAsync(TestCallers.Staff(data.Keeper), access => access.HoldAsync(data.Cabinet, "folders.unheard-of", Cancellation)))!.Via.Should().BeNull();

        // The keeper's role and the clerk's list shredding, and the folder's rules keep it from every member's role:
        // the clerk does not hold it, and files all the same.
        (await filing.FoldersAsync(TestCallers.Staff(data.Clerk), access => access.HoldAsync(data.Cabinet, FolderPermissions.Shred, Cancellation)))!.Via.Should().BeNull();
        (await filing.FoldersAsync(TestCallers.Staff(data.Clerk), access => access.HoldAsync(data.Cabinet, FolderPermissions.File, Cancellation)))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task Every_question_agrees_with_the_rules_read_over_the_aggregates()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        foreach (var user in data.Users)
        {
            foreach (var permission in FilingScenario.DocumentPermissionsAsked)
            {
                var reached = await filing.Services.AsAsync(TestCallers.User(user), async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(permission);
                    return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).Select(document => document.Id).ToListAsync(Cancellation);
                });

                reached.Order().Should().Equal(data.DocumentsHeldBy(user, permission), "of what " + user + " holds " + permission + " on");
            }
        }

        foreach (var staff in data.Staff)
        {
            foreach (var permission in FilingScenario.FolderPermissionsAsked)
            {
                var reached = await filing.Services.AsAsync(TestCallers.Staff(staff), async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Folder>>().Reach(permission);
                    return await provider.GetRequiredService<FilingContext>().Folders.Within(reach).Select(folder => folder.Id).ToListAsync(Cancellation);
                });

                reached.Order().Should().Equal(data.FoldersHeldBy(staff, permission), "of what " + staff + " holds " + permission + " on");
            }
        }
    }

    [Fact]
    public async Task A_caller_who_is_no_member_sees_nothing()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var hal = TestCallers.User(data.Hal);

        (await filing.DocumentsAsync(hal, access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull();
        (await filing.DocumentsAsync(hal, access => access.PermissionsOnAsync([data.Minutes, data.Budget, data.Outline], FilingScenario.DocumentPermissionsAsked, Cancellation))).Should().BeEmpty();

        var seen = await filing.Services.AsAsync(hal, async provider =>
        {
            var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().ReachSeen();
            return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).CountAsync(Cancellation);
        });
        seen.Should().Be(0);

        // Refused exactly as for a document that does not exist: nobody learns that the minutes are there. Not
        // by the code, not by the text, not by anything the refusal carries, and not by what was asked of the
        // database on the way: one statement for either.
        filing.Commands.Reset();
        var unseen = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.DocumentsAsync(hal, access => access.RequireAsync(data.Minutes, DocumentPermissions.View, Cancellation)));
        var forTheUnseen = filing.Commands.Count;
        filing.Commands.Reset();
        var missing = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.DocumentsAsync(hal, access => access.RequireAsync(DocumentId.CreateSequential(), DocumentPermissions.View, Cancellation)));
        var forTheMissing = filing.Commands.Count;

        (unseen.Code, unseen.Message, unseen.Kind).Should().Be((missing.Code, missing.Message, missing.Kind));
        unseen.Arguments.Should().BeEmpty("a refusal that named the document, or the permission, would say it is there");
        missing.Arguments.Should().BeEmpty();
        (forTheUnseen, forTheMissing).Should().Be((1, 1), "a second look at a document that is there would tell it from one that is not");
    }

    [Fact]
    public async Task Somebody_who_did_not_sign_in_reaches_nothing_and_nothing_is_read_for_them()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // An anonymous caller, a signed-in one whose token names no user, and staff whose token carries no staff code.
        CurrentUserSnapshot[] nobodies = [CurrentUserSnapshot.Anonymous, CurrentUserSnapshot.User(id: null)];
        foreach (var nobody in nobodies)
        {
            (await filing.DocumentsAsync(nobody, access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull();
            (await filing.DocumentsAsync(nobody, access => access.PermissionsOnAsync([data.Minutes], [DocumentPermissions.View], Cancellation))).Should().BeEmpty();
            (await filing.DocumentsAsync(nobody, access => Task.FromResult(access.Reach(DocumentPermissions.View).Everything))).Should().BeFalse();
        }

        (await filing.FoldersAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Cabinet, FolderPermissions.Read, Cancellation))).Should().BeNull("a folder's members are known by the staff claim, and Ada's token has none");
        filing.Commands.Count.Should().Be(0, "a caller that is nobody's member reaches nothing, so there is nothing to ask the database");

        // Asked about a list, nobody is refused: the statement the reach goes into answers nothing.
        var listed = await filing.Services.AsAsync(CurrentUserSnapshot.Anonymous, async provider =>
        {
            var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(DocumentPermissions.View);
            return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).ToListAsync(Cancellation);
        });
        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task The_application_itself_holds_every_permission_on_every_resource()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var system = CurrentUserSnapshot.System;

        foreach (var permission in FilingScenario.DocumentPermissionsAsked)
        {
            (await filing.DocumentsAsync(system, access => access.HoldAsync(data.Outline, permission, Cancellation)))!.Via.Should().Be(MemberVia.System);
        }

        (await filing.DocumentsAsync(system, access => access.HoldAsync(DocumentId.CreateSequential(), DocumentPermissions.View, Cancellation))).Should().BeNull("what is not there is not there for the system either");

        var all = await filing.Services.AsAsync(system, async provider =>
        {
            var reach = provider.GetRequiredService<MembershipAccessManager<Folder>>().Reach(FolderPermissions.Shred);
            reach.Everything.Should().BeTrue();
            return await provider.GetRequiredService<FilingContext>().Folders.Within(reach).Select(folder => folder.Id).ToListAsync(Cancellation);
        });
        all.Should().BeEquivalentTo([data.Cabinet, data.Annex]);

        var permissions = await filing.DocumentsAsync(system, access => access.PermissionsOnAsync([data.Minutes, DocumentId.CreateSequential()], [DocumentPermissions.Edit, "documents.unheard-of"], Cancellation));
        permissions.Should().ContainSingle().Which.Should().Match<KeyValuePair<DocumentId, IReadOnlySet<string>>>(pair =>
            pair.Key == data.Minutes && pair.Value.SetEquals(new[] { DocumentPermissions.Edit, "documents.unheard-of" }));
    }

    [Fact]
    public async Task Work_in_a_scope_holds_nothing_on_a_resource_unless_its_registration_says_so()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var elsewhere = CurrentUserSnapshot.SystemIn("other");
        var own = CurrentUserSnapshot.SystemIn(FolderMembership.Scope);

        // A document's rules turn system work in a scope off, systemScopes: []: work in a scope, the host's own
        // module's too, is nobody's member there and nothing above the rules. It sees no document, holds no permission, and nothing is read for it.
        foreach (var scoped in new[] { elsewhere, own })
        {
            (await filing.DocumentsAsync(scoped, access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull();
            (await filing.DocumentsAsync(scoped, access => access.PermissionsOnAsync([data.Minutes, data.Budget], FilingScenario.DocumentPermissionsAsked, Cancellation))).Should().BeEmpty();
            await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotFound, () => filing.DocumentsAsync(scoped, access => access.RequireAsync(data.Minutes, DocumentPermissions.View, Cancellation)));

            var listed = await filing.Services.AsAsync(scoped, async provider =>
            {
                var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(DocumentPermissions.View);
                reach.Everything.Should().BeFalse();
                return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).CountAsync(Cancellation);
            });
            listed.Should().Be(0);
        }

        filing.Commands.Count.Should().Be(2, "only the two lists were asked of the database, and each answered nothing");

        // A folder's rules say nothing of scopes, so the module's own is let through: its work there is the application's own, and holds
        // every permission on every folder, a permission no member's role gives among them.
        var hold = await filing.FoldersAsync(own, access => access.HoldAsync(data.Annex, FolderPermissions.Shred, Cancellation));
        (hold!.Via, hold.Until).Should().Be((MemberVia.System, null));
        (await filing.FoldersAsync(own, access => access.PermissionsOnAsync([data.Cabinet, data.Annex], [FolderPermissions.Staff, FolderPermissions.HandOver], Cancellation)))
            .Should().HaveCount(2).And.OnlyContain(pair => pair.Value.SetEquals(new[] { FolderPermissions.Staff, FolderPermissions.HandOver }));
        (await filing.FoldersAsync(own, access => access.RequireAsync(data.Cabinet, FolderPermissions.Staff, Cancellation))).Via.Should().Be(MemberVia.System);

        // Work in any other scope holds nothing on a folder either: another module's work is not above its rules.
        (await filing.FoldersAsync(elsewhere, access => access.HoldAsync(data.Annex, FolderPermissions.Read, Cancellation))).Should().BeNull();
        (await filing.FoldersAsync(elsewhere, access => Task.FromResult(access.Reach(FolderPermissions.Read).Everything))).Should().BeFalse();
        await Refused.WithCodeAsync(FolderRefusals.Membership, MembershipRefusals.NotFound, () => filing.FoldersAsync(elsewhere, access => access.RequireAsync(data.Cabinet, FolderPermissions.Read, Cancellation)));
    }

    [Fact]
    public async Task Staff_are_known_by_a_claim_of_their_token_and_users_by_their_id()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // The clerk's token carries the staff code; whose user id it carries does not matter to a folder.
        (await filing.FoldersAsync(TestCallers.Staff(data.Clerk, user: data.Hal), access => access.HoldAsync(data.Cabinet, FolderPermissions.File, Cancellation)))!.Via.Should().Be(MemberVia.Members);

        // And the staff code does not matter to a document: Ben is a member of the minutes by his user id.
        (await filing.DocumentsAsync(TestCallers.Staff(data.Nobody, user: data.Ben), access => access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.DocumentsAsync(TestCallers.Staff(data.Clerk, user: data.Hal), access => access.HoldAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task What_is_refused_and_in_which_order_is_the_same_as_for_any_storage()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Seen without the permission: not permitted, naming the permission, under the document's code.
        var refused = await Refused.WithCodeAsync(DocumentRefusals.Membership, MembershipRefusals.NotPermitted, () => filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.RequireAsync(data.Minutes, DocumentPermissions.Edit, Cancellation)));
        refused.Arguments.Should().Contain("Permission", DocumentPermissions.Edit);
        refused.Kind.Should().Be(RefusalKind.NotPermitted);

        // With the permission: what was read, for the handler.
        var held = await filing.DocumentsAsync(TestCallers.User(data.Ben), access => access.RequireAsync(data.Minutes, DocumentPermissions.Edit, Cancellation));
        held.Via.Should().Be(MemberVia.Members);

        // How a permission is held is an answer for a query that shows it, and not holding it is one too.
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.ViaAsync(data.Minutes, DocumentPermissions.Edit, Cancellation))).Should().BeNull();
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.ViaAsync(data.Minutes, DocumentPermissions.View, Cancellation))).Should().Be(MemberVia.Members);
    }
}
