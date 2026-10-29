using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.EntityFramework;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// Requirements that combine Membership's cases, sent through the door, the handler and its save under the expert hold
/// (<c>UseMemberHolds</c>): the caller's own beside a permission on a resource, <c>[OrOwn]</c> beside <c>[OnResource]</c>, whose
/// own way through still reads the resource, compares its version and keeps what the save is held to; and a permission on each
/// of two resources of one kind, each saved under its own hold.
/// </summary>
public sealed class CombinedRequirementsUnderTheHoldTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Task<SqliteFiling> FilingAsync() => SqliteFiling.SeededAsync(wiring: (options, provider) => options.UseDDDToolkit(provider).UseMemberHolds(provider));

    /// <summary>Shares a document with a user on each of two documents: the permission that shares, on both.</summary>
    private sealed record ShareOnBoth(DocumentId First, DocumentId Second, UserId With) : IFilingRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.All(
            MemberAccess.On(DocumentPermissions.Share, First),
            MemberAccess.On(DocumentPermissions.Share, Second));
    }

    [Fact]
    public async Task A_member_takes_itself_off_a_document_it_sees_without_the_permission_and_the_save_is_held_to_what_the_door_read()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;

        // Dee is a member of the minutes with no role: she sees them, and does not hold the permission that shares them.
        await filing.Services.SendAsync(TestUsers.User(data.Dee), new LeaveDocument(data.Minutes, data.Dee));

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Dee);
    }

    [Fact]
    public async Task Whoever_holds_the_permission_takes_another_member_off_and_a_member_without_it_takes_nobody_else_off()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;

        // Ada owns the minutes, so she holds every permission on them.
        await filing.Services.SendAsync(TestUsers.User(data.Ada), new LeaveDocument(data.Minutes, data.Cy));
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Cy);

        var refused = (await FluentActions.Awaiting(() => filing.Services.SendAsync(TestUsers.User(data.Dee), new LeaveDocument(data.Minutes, data.Ben)))
            .Should().ThrowAsync<RefusalException>()).Which;
        refused.Code.Should().Be(ToolkitRefusals.Refused);
        ((IReadOnlyList<RefusalReason>)refused.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(reason => reason.Code)
            .Should().Equal([ToolkitRefusals.NotOwn, DocumentRefusals.Membership[MembershipRefusals.NotPermitted]], "Ben is not hers to take off, and she does not hold the permission");
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().Contain(share => share.MemberId == data.Ben);
    }

    [Fact]
    public async Task On_the_caller_s_own_way_through_the_version_it_named_is_compared_at_the_door_as_on_the_permission_s()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;
        var read = (await filing.Services.ReadAsync(data.Minutes)).Version;

        // Somebody changes the minutes after Dee read them.
        await filing.Services.ChangeAsync(data.Minutes, minutes => minutes.Unshare(data.Eve));

        var lost = await FluentActions.Awaiting(() => filing.Services.SendAsync(TestUsers.User(data.Dee), new LeaveDocument(data.Minutes, data.Dee, ExpectedVersion: read)))
            .Should().ThrowAsync<ConcurrencyConflictException>("the door compares the version on the way the caller comes through, whichever it is");
        lost.Which.AggregateId.Should().Be(data.Minutes);
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().Contain(share => share.MemberId == data.Dee);

        await filing.Services.SendAsync(TestUsers.User(data.Dee), new LeaveDocument(data.Minutes, data.Dee, ExpectedVersion: read + 1));
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Dee);
    }

    [Fact]
    public async Task A_caller_on_its_own_way_through_a_document_it_does_not_see_is_told_it_is_not_found_and_learns_nothing_more()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;

        // Hal is a member of nothing: his own id he may name, and the minutes are not there for him either way.
        var refused = (await FluentActions.Awaiting(() => filing.Services.SendAsync(TestUsers.User(data.Hal), new LeaveDocument(data.Minutes, data.Hal)))
            .Should().ThrowAsync<RefusalException>()).Which;
        ((IReadOnlyList<RefusalReason>)refused.Arguments[ToolkitRefusals.ReasonsArgument]!).Select(reason => reason.Code)
            .Should().Equal(DocumentRefusals.Membership[MembershipRefusals.NotFound], DocumentRefusals.Membership[MembershipRefusals.NotFound]);
    }

    [Fact]
    public async Task A_permission_on_each_of_two_documents_keeps_a_hold_on_each_and_both_are_saved_under_the_hold()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;

        // Ada owns the minutes, and is given the manager's role on the budget, which Ben owns.
        await filing.Services.ChangeAsync(data.Budget, budget => budget.GiveRole(data.Ada, DocumentMembership.Manager, MemberPeriod.Open(data.Now.AddDays(-1)), data.Now, by: data.Ben));

        var command = new ShareOnBoth(data.Minutes, data.Budget, data.Hal);
        await filing.Services.AsAsync(TestUsers.User(data.Ada), async provider =>
        {
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);
            Checked<MemberHold<DocumentId>>.FindAllInHand().Select(hold => hold.Resource).Should().Equal([data.Minutes, data.Budget], "one hold for each document the door read, in the order of the cases");

            var context = provider.GetRequiredService<FilingContext>();
            foreach (var id in new[] { command.First, command.Second })
            {
                var document = await context.Documents.AsTracking().SingleAsync(each => each.Id == id, Cancellation);
                document.ShareWith(command.With, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);
            }

            await context.SaveChangesAsync(Cancellation);
        });

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().Contain(share => share.MemberId == data.Hal);
        (await filing.Services.ReadAsync(data.Budget)).Shares.Should().Contain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task A_combination_that_refused_the_caller_leaves_no_hold_a_save_could_be_held_to()
    {
        using var filing = await FilingAsync();
        var data = filing.Scenario;

        // Ada holds the permission on the minutes and only looks on at the budget: the first case passes, the second refuses.
        var command = new ShareOnBoth(data.Minutes, data.Budget, data.Hal);
        await filing.Services.AsAsync(TestUsers.User(data.Ada), async provider =>
        {
            await FluentActions.Awaiting(() => provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation).AsTask())
                .Should().ThrowAsync<RefusalException>();

            FluentActions.Invoking(() => provider.GetRequiredService<Checked<MemberHold<DocumentId>>>().TakeAllFor(command))
                .Should().Throw<InvalidOperationException>("nothing is kept for a request its checks refused, whichever of its cases passed");
        });
    }
}
