namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The access questions on Postgres are the statements they are on SQLite: one for what is held on a resource,
/// one for the keys held on many, and a reach inside whatever statement the module runs. Counted with the
/// context running as the caller, under the policies, as a host on row level security runs it.
/// </summary>
public sealed class MemberStatementsOnPostgresTests(FilingPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_is_held_on_one_resource_and_the_keys_held_on_many_are_one_statement_each()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var commands = filing.Services.Commands;

        var held = await filing.Services.AsAsync(TestCallers.User(data.Ben), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().RequireAsync(data.Minutes, DocumentKeys.Edit, Cancellation));
        commands.Count.Should().Be(1);
        held.Via.Should().Be(MemberVia.Members);
        commands.Commands[0].Should().Contain("EXISTS").And.Contain("filing.\"DocumentShares\"").And.Contain("filing.\"DocumentShareRoles\"");

        commands.Reset();
        var keys = await filing.Services.AsAsync(TestCallers.User(data.Ada), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>()
            .KeysOnAsync([data.Minutes, data.Budget, data.Outline], FilingScenario.DocumentKeysAsked, Cancellation));
        commands.Count.Should().Be(1);
        keys[data.Minutes].Should().BeEquivalentTo([DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share]);
        keys[data.Budget].Should().BeEquivalentTo([DocumentKeys.View]);
        keys.Should().NotContainKey(data.Outline);

        // A folder's, known by the claim: the same shape over the host's own table names.
        commands.Reset();
        var staff = await filing.Services.AsAsync(TestCallers.Staff(data.Clerk), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>()
            .KeysOnAsync([data.Cabinet, data.Annex], FilingScenario.FolderKeysAsked, Cancellation));
        commands.Count.Should().Be(1);
        commands.Commands[0].Should().Contain($"filing.\"{FilingContext.FolderStaffTable}\"").And.Contain($"\"{FilingContext.StaffColumn}\"").And.NotContain("Document");
        staff[data.Cabinet].Should().BeEquivalentTo([FolderKeys.Read, FolderKeys.File]);
        staff[data.Annex].Should().BeEquivalentTo(
            [FolderKeys.Read, FolderKeys.File, FolderKeys.Staff, FolderKeys.Shred, FolderKeys.HandOver],
            "the clerk keeps the annex, and a keeper holds every key of a folder");
    }

    [Fact]
    public async Task Questions_asked_side_by_side_each_run_as_their_caller_on_a_context_of_their_own()
    {
        // A host whose contexts come from a pool with a factory, and run as the caller under the policies.
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true, ownContexts: true);
        var data = filing.Scenario;
        var commands = filing.Services.Commands;

        // Three questions of one request, asked at once as Ben: three statements, none on the request's own
        // context, each answered as Ben, under the policies.
        using (Callers.Begin(TestCallers.User(data.Ben)))
        {
            await using var scope = filing.Services.Provider.CreateAsyncScope();
            var requests = scope.ServiceProvider.GetRequiredService<FilingContext>();
            var access = scope.ServiceProvider.GetRequiredService<IMemberQuestions<DocumentId>>();
            commands.Reset();

            var asked = await Task.WhenAll(
                access.HoldAsync(data.Minutes, DocumentKeys.Edit, Cancellation),
                access.HoldAsync(data.Budget, DocumentKeys.Share, Cancellation),
                access.HoldAsync(data.Outline, DocumentKeys.View, Cancellation));

            asked[0]!.Via.Should().Be(MemberVia.Members, "Ben contributes to the minutes");
            asked[1]!.Via.Should().Be(MemberVia.Members, "and owns the budget");
            asked[2].Should().BeNull("the outline is Cy's alone: the policies hide it from Ben, and so does the question");
            commands.Sent.Should().HaveCount(3);
            commands.Sent.Should().OnlyContain(sent => sent.Context != null && !ReferenceEquals(sent.Context, requests), "each reading takes a context of its own");
            commands.Sent.Select(sent => sent.Context).Distinct().Should().HaveCount(3, "and no two readings that run side by side share one");
        }

        // The same questions as Hal, who is on no document: a context taken from the pool again does not keep the caller before.
        using (Callers.Begin(TestCallers.User(data.Hal)))
        {
            await using var scope = filing.Services.Provider.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<IMemberQuestions<DocumentId>>();

            var asked = await Task.WhenAll(
                access.HoldAsync(data.Minutes, DocumentKeys.Edit, Cancellation),
                access.HoldAsync(data.Budget, DocumentKeys.Share, Cancellation),
                access.HoldAsync(data.Outline, DocumentKeys.View, Cancellation));

            asked.Should().OnlyContain(hold => hold == null);
        }

        // And as the owner of the outline, the third is answered: it was the caller that hid it, not the question.
        (await filing.Services.AsAsync(TestCallers.User(data.Cy), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(data.Outline, DocumentKeys.View, Cancellation)))!
            .Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task Until_when_a_key_is_held_is_read_in_the_statement_that_reads_the_hold()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var commands = filing.Services.Commands;
        var precision = TimeSpan.FromMilliseconds(1);

        async Task<MemberHold<DocumentId>?> AskAsync(UserId user, string key)
        {
            commands.Reset();
            var held = await filing.Services.AsAsync(TestCallers.User(user), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(data.Minutes, key, Cancellation));
            commands.Count.Should().Be(1, "how " + key + " is held, until when, and the document's version are one statement");
            return held;
        }

        // Dee contributes for two hours, and Hal's membership ends in three days with a role that would outlast it.
        var started = data.Now.AddMinutes(-5);
        await filing.Services.ChangeAsync(data.Minutes, document =>
        {
            document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(started, data.Now.AddHours(2)), started, by: data.Ada);
            document.ShareWith(data.Hal, DocumentMembership.Contributor, MemberPeriod.Between(started, data.Now.AddDays(3)), started, by: data.Ada);
            document.GiveRole(data.Hal, DocumentMembership.Owner, MemberPeriod.Between(started, data.Now.AddDays(10)), started, by: data.Ada);
        });

        // By owning the document: no end. By a role with no end, in a membership with none: no end.
        var ada = await AskAsync(data.Ada, DocumentKeys.Share);
        (ada!.Via, ada.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
        (await AskAsync(data.Ben, DocumentKeys.Edit))!.Until.Should().BeNull();

        // By being a member: the membership's end. By a role: the role's end, or its membership's if that is sooner.
        (await AskAsync(data.Cy, DocumentKeys.View))!.Until.Should().BeCloseTo(data.Now.AddDays(7), precision);
        (await AskAsync(data.Dee, DocumentKeys.Edit))!.Until.Should().BeCloseTo(data.Now.AddHours(2), precision);
        (await AskAsync(data.Hal, DocumentKeys.Edit))!.Until.Should().BeCloseTo(data.Now.AddDays(3), precision, "the role that ends last gives the key for ten days, and counts only inside a membership of three");
        (await AskAsync(data.Hal, DocumentKeys.Share))!.Until.Should().BeCloseTo(data.Now.AddDays(3), precision);

        // Not held: nothing to end.
        var cy = await AskAsync(data.Cy, DocumentKeys.Edit);
        (cy!.Via, cy.Until).Should().Be(((MemberVia?)null, (DateTimeOffset?)null));

        // A folder's, known by the claim, with a keeper's deputy for three days.
        await filing.Services.ChangeAsync(data.Cabinet, folder => folder.GiveRole(data.Visitor, FolderMembership.Keeper, MemberPeriod.Between(started, data.Now.AddDays(3)), started, by: data.Keeper));
        var deputy = await filing.Services.AsAsync(TestCallers.Staff(data.Visitor), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Cabinet, FolderKeys.Staff, Cancellation));
        deputy!.Until.Should().BeCloseTo(data.Now.AddDays(3), precision);
        var keeper = await filing.Services.AsAsync(TestCallers.Staff(data.Keeper), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Cabinet, FolderKeys.HandOver, Cancellation));
        (keeper!.Via, keeper.Until).Should().Be((MemberVia.Members, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task A_reach_and_a_resources_own_columns_are_one_statement_of_the_modules_own()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var commands = filing.Services.Commands;

        var titles = await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var seen = provider.GetRequiredService<IMemberQuestions<DocumentId>>().Reach(DocumentKeys.View);
            return await provider.GetRequiredService<FilingContext>().Documents.Within(seen).OrderBy(document => document.Title).Select(document => document.Title).ToListAsync(Cancellation);
        });
        titles.Should().Equal("Budget", "Minutes");
        commands.Count.Should().Be(1);

        commands.Reset();
        var answer = await filing.Services.AsAsync(TestCallers.User(data.Cy), async provider =>
        {
            var access = provider.GetRequiredService<IMemberQuestions<DocumentId>>();
            var act = access.Reach(DocumentKeys.Edit);
            var found = await provider.GetRequiredService<FilingContext>().Documents
                .Where(document => document.Id == data.Minutes)
                .Within(access.KeyReach([]).See)
                .Reached(act)
                .Select(reached => new { reached.Resource.Title, reached.Resource.Archived, reached.AsMember, reached.FromAbove })
                .SingleAsync(Cancellation);

            return (found.Title, found.Archived, Via: act.Via(found.AsMember, found.FromAbove));
        });
        answer.Should().Be(("Minutes", false, (MemberVia?)null), "Cy sees the minutes and may not change them");
        commands.Count.Should().Be(1);
    }
}
