namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// A hold says until when the caller holds the key on the resource: the first moment it no longer does, as its
/// membership and its roles stand now, and nothing when that is never. The package refuses nobody by it; it is
/// what a host's own rule about giving reads. Read from the database in the one statement that reads the hold.
/// </summary>
public sealed class MemberHoldUntilTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_owner_and_the_applications_own_work_hold_with_no_end()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // The owner, for every key of the resource, whatever its roles are and whenever they end.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Ada, DocumentMembership.Contributor, MemberPeriod.Between(data.Now, data.Now.AddHours(1)), data.Now, by: null));
        foreach (var key in DocumentMembership.Rules.Keys)
        {
            var held = await filing.DocumentsAsync(TestCallers.User(data.Ada), access => access.HoldAsync(data.Minutes, key, Cancellation));
            (held!.Via, held.Until).Should().Be((MemberVia.Members, null), "Ada owns the minutes, and holds " + key + " by owning them");
        }

        // A keeper, for the keys no role gives as for the others.
        foreach (var key in FolderMembership.Rules.Keys)
        {
            (await filing.FoldersAsync(TestCallers.Staff(data.Keeper), access => access.HoldAsync(data.Cabinet, key, Cancellation)))!.Until.Should().BeNull();
        }

        // The application's own work.
        var system = await filing.DocumentsAsync(Caller.System, access => access.HoldAsync(data.Minutes, DocumentKeys.Share, Cancellation));
        (system!.Via, system.Until).Should().Be((MemberVia.System, null));
    }

    [Fact]
    public async Task A_key_that_being_a_member_gives_is_held_until_the_membership_ends()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Cy's membership ends next week; Dee's has no end.
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.HoldAsync(data.Minutes, DocumentKeys.View, Cancellation)))!.Until.Should().Be(data.Now.AddDays(7));
        (await filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentKeys.View, Cancellation)))!.Until.Should().BeNull();

        // What the questions that refuse hand a handler says the same.
        (await filing.DocumentsAsync(TestCallers.User(data.Cy), access => access.RequireAsync(data.Minutes, DocumentKeys.View, Cancellation))).Until.Should().Be(data.Now.AddDays(7));
    }

    [Fact]
    public async Task A_key_a_role_gives_is_held_until_the_role_ends_or_its_membership_if_that_ends_sooner()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;

        Task<MemberHold<DocumentId>?> AskAsync(UserId user, string key)
            => filing.DocumentsAsync(TestCallers.User(user), access => access.HoldAsync(data.Minutes, key, Cancellation));

        // A role with no end in a membership with no end: no end.
        (await AskAsync(data.Ben, DocumentKeys.Edit))!.Until.Should().BeNull();
        (await AskAsync(data.Ian, DocumentKeys.Share))!.Until.Should().BeNull("the owner's role, held by somebody who is not the owner, is a role like any other");

        // A role that ends while the membership goes on: the role's end.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddHours(2)), now, by: data.Ada));
        (await AskAsync(data.Dee, DocumentKeys.Edit))!.Until.Should().Be(now.AddHours(2));
        (await AskAsync(data.Dee, DocumentKeys.View))!.Until.Should().BeNull("seeing is what being a member gives, and her membership has no end");

        // A role with no end in a membership that ends: the membership's end, since a role counts only inside it.
        await filing.Services.ChangeAsync(data.Minutes, document =>
        {
            document.ShareWith(data.Hal, MemberPeriod.Between(now, now.AddDays(3)), now, by: data.Ada);
            document.GiveRole(data.Hal, DocumentMembership.Contributor, MemberPeriod.Open(now), now, by: data.Ada);
        });
        (await AskAsync(data.Hal, DocumentKeys.Edit))!.Until.Should().Be(now.AddDays(3));

        // And a role that would outlast its membership ends with it.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Hal, DocumentMembership.Owner, MemberPeriod.Between(now, now.AddDays(10)), now, by: data.Ada));
        (await AskAsync(data.Hal, DocumentKeys.Share))!.Until.Should().Be(now.AddDays(3));

        // A key that is not held has no end to name.
        var cy = await AskAsync(data.Cy, DocumentKeys.Edit);
        (cy!.Via, cy.Until).Should().Be(((MemberVia?)null, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task Of_several_roles_that_give_the_key_now_the_last_to_end_counts_and_one_that_starts_later_does_not_yet()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;

        // Dee contributes for two hours, and holds the owner's role, which gives the same key, from the next hour until the ninth.
        await filing.Services.ChangeAsync(data.Minutes, document =>
        {
            document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddHours(2)), now, by: data.Ada);
            document.GiveRole(data.Dee, DocumentMembership.Owner, MemberPeriod.Between(now.AddHours(1), now.AddHours(9)), now, by: data.Ada);
        });

        Task<MemberHold<DocumentId>?> AskAsync()
            => filing.DocumentsAsync(TestCallers.User(data.Dee), access => access.HoldAsync(data.Minutes, DocumentKeys.Edit, Cancellation));

        (await AskAsync())!.Until.Should().Be(now.AddHours(2), "the second role has not started, so it gives nothing now and lengthens nothing");

        filing.Clock.Advance(TimeSpan.FromHours(1));
        (await AskAsync())!.Until.Should().Be(now.AddHours(9), "both give the key now, and the last to end is when she stops holding it");

        filing.Clock.Advance(TimeSpan.FromHours(1));
        (await AskAsync())!.Until.Should().Be(now.AddHours(9), "the first role is over, at the very moment it ends");

        // One of them with no end: no end, whatever the other says.
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Open(filing.Clock.Now), filing.Clock.Now, by: data.Ada));
        (await AskAsync())!.Until.Should().BeNull();

        // At the moment the hold ends it is no longer one.
        await filing.Services.ChangeAsync(data.Minutes, document => document.TakeRole(data.Dee, DocumentMembership.Contributor, ownerRole: null));
        filing.Clock.Advance(TimeSpan.FromHours(7));
        var over = await AskAsync();
        (over!.Via, over.Until).Should().Be(((MemberVia?)null, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task What_the_database_answers_agrees_with_the_periods_read_over_the_aggregates()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var now = data.Now;

        // Every shape there is, on the minutes: roles and memberships that end sooner, later, never, and not yet.
        await filing.Services.ChangeAsync(data.Minutes, document =>
        {
            document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddHours(2)), now, by: data.Ada);
            document.GiveRole(data.Dee, DocumentMembership.Owner, MemberPeriod.Between(now.AddHours(1), now.AddHours(9)), now, by: data.Ada);
            document.GiveRole(data.Cy, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddDays(30)), now, by: data.Ada);
            document.GiveRole(data.Ben, DocumentMembership.Owner, MemberPeriod.Between(now, now.AddDays(5)), now, by: data.Ada);
            document.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Between(now, now.AddDays(3)), now, by: data.Ada);
            document.GiveRole(data.Hal, DocumentMembership.Contributor, MemberPeriod.Between(now, now.AddDays(1)), now, by: data.Ada);
        });

        foreach (var later in new[] { TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(5) })
        {
            filing.Clock.Advance(later);
            var minutes = await filing.Services.ReadAsync(data.Minutes);

            foreach (var user in data.Users)
            {
                foreach (var key in FilingScenario.DocumentKeysAsked)
                {
                    var held = await filing.DocumentsAsync(TestCallers.User(user), access => access.HoldAsync(data.Minutes, key, Cancellation));
                    var expected = Read(minutes, user, key, filing.Clock.Now);

                    (held?.Via is not null, held?.Until).Should().Be(expected, "of " + user + " and " + key + " at " + filing.Clock.Now.ToString("O"));
                }
            }
        }
    }

    /// <summary>
    /// Whether a user holds a key on a document, and until when, worked out from the aggregate in memory the way
    /// a reader of the rules would: what every storage has to agree with.
    /// </summary>
    private static (bool Held, DateTimeOffset? Until) Read(Document document, UserId user, string key, DateTimeOffset now)
    {
        var rules = DocumentMembership.Rules;
        if (rules.OwnerHolds(key) && document.OwnerId == user)
        {
            return (true, null);
        }

        if (document.Shares.SingleOrDefault(candidate => candidate.MemberId == user && candidate.AppliesAt(now)) is not { } share)
        {
            return (false, null);
        }

        if (rules.MembershipGives(key))
        {
            return (true, share.EndsAt);
        }

        var giving = share.Roles.Where(held => held.AppliesAt(now) && rules.RolesWith(key).Contains(held.RoleId)).ToList();
        if (giving.Count == 0)
        {
            return (false, null);
        }

        DateTimeOffset? last = giving.Any(held => held.EndsAt is null) ? null : giving.Max(held => held.EndsAt);
        return (true, last is null || (share.EndsAt is { } ends && ends < last) ? share.EndsAt : last);
    }
}
