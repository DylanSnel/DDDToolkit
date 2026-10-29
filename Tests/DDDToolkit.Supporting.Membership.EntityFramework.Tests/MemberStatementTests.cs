using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// Reads in one statement, counted: what is held on one resource, the permissions held on many, and whatever a module
/// asks in a statement of its own with a reach in it. Nothing fetches a list of ids first.
/// </summary>
public sealed class MemberStatementTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_is_held_on_one_resource_is_read_in_one_statement()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // However the permission is held, and for a caller that holds nothing as well.
        (UserId User, string Key)[] asked =
        [
            (data.Ben, DocumentPermissions.Edit), (data.Dee, DocumentPermissions.View), (data.Ada, DocumentPermissions.Share),
            (data.Cy, DocumentPermissions.Share), (data.Hal, DocumentPermissions.View), (data.Ada, "documents.unheard-of"),
        ];

        foreach (var (user, permission) in asked)
        {
            filing.Commands.Reset();
            await filing.DocumentsAsync(TestUsers.User(user), access => access.HoldAsync(data.Minutes, permission, Cancellation));
            filing.Commands.Count.Should().Be(1, "seeing the document, holding " + permission + " on it, until when, and its version are one statement");
        }

        // The questions that refuse are that statement and nothing more.
        filing.Commands.Reset();
        await filing.DocumentsAsync(TestUsers.User(data.Ben), access => access.RequireAsync(data.Minutes, DocumentPermissions.Edit, Cancellation));
        await filing.DocumentsAsync(TestUsers.User(data.Ben), access => access.ViaAsync(data.Minutes, DocumentPermissions.Share, Cancellation));
        filing.Commands.Count.Should().Be(2);

        // And it reads the member rows where they are: no row of a member is fetched to be looked at here.
        filing.Commands.Commands.Should().OnlyContain(sql => sql.Contains("EXISTS", StringComparison.Ordinal) && sql.Contains("\"DocumentShares\"", StringComparison.Ordinal));
        filing.Commands.Commands.Should().NotContain(sql => sql.Contains("\"FolderStaff\"", StringComparison.Ordinal), "a question about documents reads nothing of folders");
    }

    [Fact]
    public async Task The_permissions_held_on_many_resources_are_read_in_one_statement_whatever_their_number()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        DocumentId[] about = [data.Minutes, data.Budget, data.Outline, DocumentId.CreateSequential(), data.Minutes];

        filing.Commands.Reset();
        var ada = await filing.DocumentsAsync(TestUsers.User(data.Ada), access => access.PermissionsOnAsync(about, [.. FilingScenario.DocumentPermissionsAsked, DocumentPermissions.View], Cancellation));

        filing.Commands.Count.Should().Be(1);
        ada.Keys.Should().BeEquivalentTo([data.Minutes, data.Budget], "a document she is no member of, and one that is not there, are not in the answer");
        ada[data.Minutes].Should().BeEquivalentTo([DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share], "an owner holds every permission of a document");
        ada[data.Budget].Should().BeEquivalentTo([DocumentPermissions.View]);

        // Agreed with the rules read over the aggregates, for everybody and every permission.
        foreach (var user in data.Users)
        {
            var held = await filing.DocumentsAsync(TestUsers.User(user), access => access.PermissionsOnAsync(about, FilingScenario.DocumentPermissionsAsked, Cancellation));

            foreach (var document in data.Documents)
            {
                var expected = FilingScenario.DocumentPermissionsAsked.Where(permission => data.DocumentsHeldBy(user, permission).Contains(document.Id)).ToList();
                (held.TryGetValue(document.Id, out var permissions) ? permissions : new HashSet<string>()).Should().BeEquivalentTo(expected, "of " + user + " on " + document.Title);
            }
        }

        // Fay sees the minutes and holds nothing but the permission that sees; Temp sees the cabinet and holds nothing at all.
        var fay = await filing.DocumentsAsync(TestUsers.User(data.Fay), access => access.PermissionsOnAsync([data.Minutes], [DocumentPermissions.Edit, DocumentPermissions.Share], Cancellation));
        fay.Should().BeEmpty("a resource the caller holds none of the permissions on is not in the answer");
        (await filing.FoldersAsync(TestUsers.Staff(data.Temp), access => access.PermissionsOnAsync([data.Cabinet], FilingScenario.FolderPermissionsAsked, Cancellation))).Should().BeEmpty();
        var clerk = await filing.FoldersAsync(TestUsers.Staff(data.Clerk), access => access.PermissionsOnAsync([data.Cabinet, data.Annex], FilingScenario.FolderPermissionsAsked, Cancellation));
        clerk[data.Cabinet].Should().BeEquivalentTo([FolderPermissions.Read, FolderPermissions.File]);
        clerk[data.Annex].Should().BeEquivalentTo(
            [FolderPermissions.Read, FolderPermissions.File, FolderPermissions.Staff, FolderPermissions.Shred, FolderPermissions.HandOver],
            "the clerk keeps the annex, and a keeper holds every permission of a folder, those no role gives too");
    }

    [Fact]
    public async Task Nothing_is_read_where_nothing_can_be_answered()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        var ada = TestUsers.User(data.Ada);

        (await filing.DocumentsAsync(ada, access => access.PermissionsOnAsync([], [DocumentPermissions.View], Cancellation))).Should().BeEmpty();
        (await filing.DocumentsAsync(ada, access => access.PermissionsOnAsync([data.Minutes], [], Cancellation))).Should().BeEmpty();
        (await filing.DocumentsAsync(ada, access => access.PermissionsOnAsync([data.Minutes], ["documents.unheard-of"], Cancellation))).Should().BeEmpty();
        filing.Commands.Count.Should().Be(0);

        // More resources than one question is asked about: a bug of the use case, refused before anything is read.
        var many = Enumerable.Range(0, MembershipAccessManager.MostResources + 1).Select(_ => DocumentId.CreateSequential()).ToList();
        await FluentActions.Awaiting(() => filing.DocumentsAsync(ada, access => access.PermissionsOnAsync(many, [DocumentPermissions.View], Cancellation)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*at most 200 different resources*");
        await FluentActions.Awaiting(() => filing.DocumentsAsync(ada, access => access.PermissionsOnAsync([data.Minutes], [" "], Cancellation))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => filing.DocumentsAsync(ada, access => access.HoldAsync(data.Minutes, " ", Cancellation))).Should().ThrowAsync<ArgumentException>();
        filing.Commands.Count.Should().Be(0);

        // Exactly as many as that are asked about, the same one named twice counted once.
        var most = many.Take(MembershipAccessManager.MostResources - 1).Append(data.Minutes).Append(data.Minutes).ToList();
        (await filing.DocumentsAsync(ada, access => access.PermissionsOnAsync(most, [DocumentPermissions.View], Cancellation))).Keys.Should().Equal(data.Minutes);
        filing.Commands.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_reach_goes_into_a_statement_of_the_modules_own()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        filing.Commands.Reset();
        var titles = await filing.Services.AsAsync(TestUsers.User(data.Ada), async provider =>
        {
            var seen = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(DocumentPermissions.View);

            // The module's own query, with its own filter, order and page: the reach is one more condition of it.
            return await provider.GetRequiredService<FilingContext>().Documents
                .Where(document => !document.Archived)
                .Within(seen)
                .OrderBy(document => document.Title)
                .Take(10)
                .Select(document => document.Title)
                .ToListAsync(Cancellation);
        });

        titles.Should().Equal("Budget", "Minutes");
        filing.Commands.Count.Should().Be(1, "the documents a caller may see are one statement, not a list of ids fetched first");
    }

    [Fact]
    public async Task A_resources_own_columns_are_read_next_to_how_the_permission_is_held_in_one_statement()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;
        await filing.Services.ChangeAsync(data.Budget, document => document.MoveToArchive());

        // What a module tells another module about a document: whether it is there for the caller, whether the
        // caller may change it, and what the document itself says, which only the module knows to ask.
        async Task<DocumentAnswer?> AskAsync(UserId user, DocumentId id)
            => await filing.Services.AsAsync(TestUsers.User(user), async provider =>
            {
                var access = provider.GetRequiredService<MembershipAccessManager<Document>>();
                var act = access.Reach(DocumentPermissions.Edit);
                var db = provider.GetRequiredService<FilingContext>();
                var found = await db.Documents
                    .Where(document => document.Id == id)
                    .WhereIHold(DocumentPermissions.View, db)
                    .Reached(act)
                    .Select(reached => new { reached.Resource.Archived, reached.AsMember, reached.FromAbove, reached.Until })
                    .FirstOrDefaultAsync(Cancellation);

                return found is null ? null : new DocumentAnswer(found.Archived, act.Via(found.AsMember, found.FromAbove), found.Until);
            });

        filing.Commands.Reset();
        (await AskAsync(data.Ben, data.Minutes)).Should().Be(new DocumentAnswer(false, MemberVia.Members, null));
        filing.Commands.Count.Should().Be(1);

        (await AskAsync(data.Ben, data.Budget)).Should().Be(new DocumentAnswer(true, MemberVia.Members, null), "Ben owns the budget, which is archived");
        (await AskAsync(data.Ada, data.Budget)).Should().Be(new DocumentAnswer(true, null, null), "Ada sees the budget and may not change it");
        (await AskAsync(data.Hal, data.Budget)).Should().BeNull("to Hal it is not there");
        (await AskAsync(data.Ada, data.Outline)).Should().BeNull();

        // Until when the permission is held is one more column of the same statement, for a module that goes on to it.
        var until = data.Now.AddHours(3);
        await filing.Services.ChangeAsync(data.Minutes, document => document.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Between(data.Now, until), data.Now, by: data.Ada));
        filing.Commands.Reset();
        (await AskAsync(data.Dee, data.Minutes)).Should().Be(new DocumentAnswer(false, MemberVia.Members, until));
        filing.Commands.Count.Should().Be(1);
    }

    [Fact]
    public async Task Until_when_a_permission_is_held_is_asked_only_by_a_statement_that_goes_on_to_it()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        async Task<string> SqlOfAsync(Func<IQueryable<MemberFound<Document>>, Task> read)
        {
            filing.Commands.Reset();
            await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
            {
                var access = provider.GetRequiredService<MembershipAccessManager<Document>>();
                await read(provider.GetRequiredService<FilingContext>().Documents
                    .Where(document => document.Id == data.Minutes)
                    .Within(access.ReachSeen())
                    .Reached(access.Reach(DocumentPermissions.Edit)));
            });

            return filing.Commands.Commands.Should().ContainSingle().Subject;
        }

        // How the permission is held, and nothing of when that ends: the roles' ends are not read.
        var without = await SqlOfAsync(found => found.Select(reached => new { reached.Resource.Title, reached.AsMember }).SingleAsync(Cancellation));
        without.Should().NotContain("MAX(", "a fact the statement does not go on to is no part of it");

        // With it: one more column of the same statement, read where the rows are.
        var with = await SqlOfAsync(found => found.Select(reached => new { reached.AsMember, reached.Until }).SingleAsync(Cancellation));
        with.Should().Contain("MAX(").And.Contain("\"DocumentShareRoles\"");
    }

    [Fact]
    public async Task The_permissions_held_go_into_a_statement_of_the_modules_own()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        filing.Commands.Reset();
        var rows = await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
        {
            var db = provider.GetRequiredService<FilingContext>();
            return await db.Documents
                .Where(document => !document.Archived)
                .WhereIHold([DocumentPermissions.Edit, DocumentPermissions.Share, DocumentPermissions.Edit], db)
                .Select(pair => new { pair.Resource.Id, pair.Resource.Title, pair.Permission })
                .ToListAsync(Cancellation);
        });

        filing.Commands.Count.Should().Be(1);
        rows.Select(row => (row.Id, row.Permission)).Should().BeEquivalentTo(
        [
            (data.Minutes, DocumentPermissions.Edit),
            (data.Budget, DocumentPermissions.Edit),
            (data.Budget, DocumentPermissions.Share),
        ]);
        rows.Where(row => row.Id == data.Minutes).Select(row => row.Title).Should().Equal("Minutes");

        // Read whole, each pair carries its resource as the context reads it, its members with it.
        var whole = await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
            await provider.GetRequiredService<FilingContext>().Documents.WhereIHold([DocumentPermissions.Share]).AsNoTracking().ToListAsync(Cancellation));
        whole.Should().ContainSingle().Which.Resource.Id.Should().Be(data.Budget);
        whole[0].Resource.Shares.Should().NotBeEmpty();
        whole[0].Permission.Should().Be(DocumentPermissions.Share);

        // On the set itself the query finds its context, and the application's own work holds every permission on every document.
        var all = await filing.Services.AsAsync(CurrentUserSnapshot.System, async provider =>
            await provider.GetRequiredService<FilingContext>().Documents.WhereIHold([DocumentPermissions.Share]).Select(pair => pair.Resource.Id).ToListAsync(Cancellation));
        var every = await filing.Services.AsAsync(CurrentUserSnapshot.System, async provider => await provider.GetRequiredService<FilingContext>().Documents.CountAsync(Cancellation));
        all.Should().HaveCount(every);
    }

    [Fact]
    public async Task The_resources_a_permission_is_held_on_are_asked_of_their_own_set()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        filing.Commands.Reset();
        var titles = await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
            await provider.GetRequiredService<FilingContext>().Documents.WhereIHold(DocumentPermissions.Edit).OrderBy(document => document.Title).Select(document => document.Title).ToListAsync(Cancellation));

        titles.Should().Equal("Budget", "Minutes");
        filing.Commands.Count.Should().Be(1, "one statement, not a list of ids fetched first");

        // The same as the reach the access manager makes, put into the statement by hand.
        var byHand = await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
            await provider.GetRequiredService<FilingContext>().Documents.Within(provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(DocumentPermissions.Edit))
                .OrderBy(document => document.Title).Select(document => document.Title).ToListAsync(Cancellation));
        byHand.Should().Equal(titles);

        // A permission is named, and a context the services know nothing of is told so.
        using var scope = filing.Services.Provider.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<FilingContext>().Documents;
        FluentActions.Invoking(() => documents.WhereIHold(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => documents.WhereIHold(new[] { DocumentPermissions.View, "" })).Should().Throw<ArgumentException>();
        await using var stranger = new FilingContext(new DbContextOptionsBuilder<FilingContext>().UseSqlite("Data Source=:memory:").Options);
        FluentActions.Invoking(() => stranger.Documents.WhereIHold(DocumentPermissions.View)).Should().Throw<InvalidOperationException>().WithMessage("*made without the application's services*");
    }

    [Fact]
    public async Task A_statement_takes_only_a_reach_the_resources_own_questions_made()
    {
        using var filing = await SqliteFiling.SeededAsync();
        using var scope = filing.Services.Provider.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<FilingContext>().Documents;
        var foreign = new ForeignReach<Document>(DocumentPermissions.View);

        FluentActions.Invoking(() => documents.Within(foreign)).Should().Throw<ArgumentException>().WithMessage("This reach was not made by the access manager registered for Document*");
        FluentActions.Invoking(() => documents.Reached(foreign)).Should().Throw<ArgumentException>();

        // A reach is what it was when it was made: for the caller and the moment it was asked at.
        using (TestAccess.BeginAs(TestUsers.User(filing.Scenario.Ada)))
        {
            var reach = scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Document>>().Reach(DocumentPermissions.View);
            (reach.Permission, reach.Now, reach.Everything).Should().Be((DocumentPermissions.View, filing.Clock.Now, false));

            // And it is a reach of documents: a query over another kind of resource does not take it, though both are registered.
            var folders = scope.ServiceProvider.GetRequiredService<FilingContext>().Folders;
            var see = scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Folder>>().ReachSeen();
            see.Permission.Should().BeNull("a folder's rules name no permission for seeing, and its members see it all the same");
        }
    }

    [Fact]
    public async Task The_questions_read_on_a_context_of_their_own_where_the_host_has_a_factory_for_it()
    {
        using var filing = await SqliteFiling.SeededAsync(ownContexts: true);
        var data = filing.Scenario;

        await filing.Services.AsAsync(TestUsers.User(data.Ben), async provider =>
        {
            var requests = provider.GetRequiredService<FilingContext>();
            var access = provider.GetRequiredService<MembershipAccessManager<Document>>();

            // Asked side by side, as the fields of one request are: each on a context of its own, from the pool.
            var asked = await Task.WhenAll(
                access.HoldAsync(data.Minutes, DocumentPermissions.Edit, Cancellation),
                access.HoldAsync(data.Budget, DocumentPermissions.Edit, Cancellation),
                access.HoldAsync(data.Outline, DocumentPermissions.Edit, Cancellation));
            await access.PermissionsOnAsync([data.Minutes], [DocumentPermissions.Edit], Cancellation);

            asked.Select(held => held?.Via).Should().Equal(MemberVia.Members, MemberVia.Members, null);
            filing.Commands.Sent.Should().HaveCount(4);
            filing.Commands.Sent.Select(sent => sent.Context).Should().NotContainNulls().And.NotContain(requests, "the request's own context is the unit of work of its command, and none of these ran on it");

            // A reach still goes into whatever statement the module runs, on whichever context it runs it.
            (await requests.Documents.Within(access.Reach(DocumentPermissions.Edit)).CountAsync(Cancellation)).Should().Be(2);
        });

        // Without a factory the request's context is the one asked.
        using var plain = await SqliteFiling.SeededAsync();
        await plain.Services.AsAsync(TestUsers.User(plain.Scenario.Ben), async provider =>
        {
            await provider.GetRequiredService<MembershipAccessManager<Document>>().HoldAsync(plain.Scenario.Minutes, DocumentPermissions.Edit, Cancellation);
            plain.Commands.Sent.Should().ContainSingle().Which.Context.Should().BeSameAs(provider.GetRequiredService<FilingContext>());
        });
    }

    /// <summary>What a module answers about one of its documents: its own fact, next to how the caller holds the permission and until when.</summary>
    private sealed record DocumentAnswer(bool Archived, MemberVia? Via, DateTimeOffset? Until);

    /// <summary>A reach somebody else made.</summary>
    private sealed class ForeignReach<TResource>(string permission) : MemberReach<TResource>(permission, DateTimeOffset.UnixEpoch, everything: true)
        where TResource : class;
}
