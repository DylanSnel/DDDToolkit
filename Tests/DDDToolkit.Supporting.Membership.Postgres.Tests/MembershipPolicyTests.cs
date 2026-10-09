using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The second lock, end to end: the application's own row access rules ask the functions the package wrote, a
/// resource's member tables follow the resource's rules through ownership, and the access questions, which run
/// as the caller under those policies, answer what they answer without them. On top of the application's
/// rules, the lock the package writes from the keys the resource's rules name: a member's row is written by
/// who holds the key that changes the members, and the owner column by who holds the key that changes the
/// owner, whatever else the application's rule lets a caller change of the resource.
/// </summary>
public sealed class MembershipPolicyTests(FilingPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_reads_its_documents_with_their_members_and_a_caller_who_is_no_member_reads_nothing()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;

        Task<List<Document>> ReadAsync(Caller caller)
            => filing.Services.AsAsync(caller, provider => provider.GetRequiredService<FilingContext>().Documents.AsNoTracking().OrderBy(document => document.Title).ToListAsync(Cancellation));

        // Ben contributes to the minutes and owns the budget: both come with everybody on them, since the member
        // tables follow the document's own rule.
        var ben = await ReadAsync(TestCallers.User(data.Ben));
        ben.Select(document => document.Title).Should().Equal("Budget", "Minutes");
        ben[1].Shares.Select(share => share.MemberId).Should().BeEquivalentTo(data.Documents[0].Shares.Select(share => share.MemberId));
        ben[1].Shares.Single(share => share.MemberId == data.Fay).Roles.Should().HaveCount(2);

        // Dee is a member with no role, and sees what she is a member of; Eve's membership ended, Gil's has not started.
        (await ReadAsync(TestCallers.User(data.Dee))).Select(document => document.Title).Should().Equal("Minutes");
        (await ReadAsync(TestCallers.User(data.Eve))).Should().BeEmpty();
        (await ReadAsync(TestCallers.User(data.Gil))).Should().BeEmpty();

        // Hal is a member of nothing, and a caller who did not sign in is nobody: no row of a document, and none of its members.
        (await ReadAsync(TestCallers.User(data.Hal))).Should().BeEmpty();
        (await ReadAsync(Caller.Anonymous)).Should().BeEmpty();

        await using var hal = await filing.SessionAsync(TestCallers.User(data.Hal));
        (await hal.ListAsync<long>(
            """SELECT (SELECT count(*) FROM filing."Documents") + (SELECT count(*) FROM filing."DocumentShares") + (SELECT count(*) FROM filing."DocumentShareRoles")"""))
            .Should().Equal(0);

        // The application's own work reads everything, as the role that owns the tables.
        (await ReadAsync(Caller.System)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Staff_read_the_folders_they_are_on_and_a_member_of_documents_reads_no_folder()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;

        Task<List<FolderId>> ReadAsync(Caller caller)
            => filing.Services.AsAsync(caller, provider => provider.GetRequiredService<FilingContext>().Folders.AsNoTracking().OrderBy(folder => folder.Id).Select(folder => folder.Id).ToListAsync(Cancellation));

        (await ReadAsync(TestCallers.Staff(data.Clerk))).Should().Equal(data.Cabinet, data.Annex);
        (await ReadAsync(TestCallers.Staff(data.Visitor))).Should().Equal(data.Cabinet);
        (await ReadAsync(TestCallers.Staff(data.Temp))).Should().Equal([data.Cabinet], "on a folder with no role is on it all the same");
        (await ReadAsync(TestCallers.Staff(data.Nobody))).Should().BeEmpty();

        // Ada owns a document; her token carries no staff code, and a folder knows its members by nothing else.
        (await ReadAsync(TestCallers.User(data.Ada))).Should().BeEmpty();

        // And the keeper of a folder, who is on no document, reads none.
        (await filing.Services.AsAsync(TestCallers.Staff(data.Keeper), provider => provider.GetRequiredService<FilingContext>().Documents.CountAsync(Cancellation))).Should().Be(0);
    }

    [Fact]
    public async Task The_access_questions_answer_under_the_policies_what_they_answer_without_them()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;

        foreach (var user in data.Users)
        {
            var caller = TestCallers.User(user);
            var seen = data.DocumentsSeenBy(user);

            foreach (var key in FilingScenario.DocumentKeysAsked)
            {
                var held = data.DocumentsHeldBy(user, key);
                foreach (var document in data.Documents)
                {
                    var hold = await filing.Services.AsAsync(caller, provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(document.Id, key, Cancellation));

                    (hold is not null).Should().Be(seen.Contains(document.Id), user + " sees " + document.Title);
                    hold?.Via.Should().Be(held.Contains(document.Id) ? MemberVia.Members : null, user + " holds " + key + " on " + document.Title);
                }
            }

            var keys = await filing.Services.AsAsync(caller, provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().KeysOnAsync([.. data.Documents.Select(document => document.Id)], FilingScenario.DocumentKeysAsked, Cancellation));
            foreach (var document in data.Documents)
            {
                (keys.TryGetValue(document.Id, out var on) ? on : new HashSet<string>())
                    .Should().BeEquivalentTo(FilingScenario.DocumentKeysAsked.Where(key => data.DocumentsHeldBy(user, key).Contains(document.Id)), user + " on " + document.Title);
            }
        }

        // A folder's questions, asked by its staff under the folder's policy.
        (await filing.Services.AsAsync(TestCallers.Staff(data.Clerk), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Annex, FolderKeys.HandOver, Cancellation)))!.Via.Should().Be(MemberVia.Members);
        (await filing.Services.AsAsync(TestCallers.Staff(data.Visitor), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Annex, FolderKeys.Read, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task A_user_opens_a_document_as_its_owner_under_a_read_rule_that_asks_what_it_sees()
    {
        // The documents' read rule is the one the docs show, Seen(), and a rule of the host's lets a user add a
        // document that names it as its owner. The owner's share and its role are written in the same save, under
        // the document's rule: the document is read through what the caller sees, before the share is there.
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, asCaller: true, seed: false);
        await filing.ExecuteAsync(FilingPostgres.AccessScript(rules:
        [
            .. FilingPostgres.Rules,
            RowAccessRule.For<Document>("Users open documents of their own", RowOperations.Create, UsersOpenDocumentsOfTheirOwn.RowAccessSql, RowAccessRoles.User),
        ]));
        var data = filing.Scenario;
        var notes = DocumentId.CreateSequential();

        // This machine's moment, not the database's: the owner sees what it owns by the owner column, so a clock a
        // little ahead of the database's, which would put the share's start after the database's now, is no matter.
        await filing.Services.AsAsync(TestCallers.User(data.Hal), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            context.Documents.Add(new Document(notes, "Notes", data.Hal, DocumentMembership.Owner, DateTimeOffset.UtcNow.AddSeconds(5)));
            await context.SaveChangesAsync(Cancellation);
        });

        (await filing.ListAsync<long>($"""SELECT count(*) FROM filing."DocumentShares" WHERE "DocumentId" = '{notes.Value}' AND "MemberId" = '{data.Hal.Value}'"""))
            .Should().Equal([1L], "the owner's share was written with the document");

        await using (var hal = await filing.SessionAsync(TestCallers.User(data.Hal)))
        {
            (await hal.ListAsync<Guid>("SELECT id FROM filing.documents_i_see() AS id")).Should().Equal([notes.Value], "the owner sees what it owns");
            (await hal.ListAsync<string>("""SELECT "Title" FROM filing."Documents" """)).Should().Equal(["Notes"]);
        }

        var hold = await filing.Services.AsAsync(TestCallers.User(data.Hal), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(notes, DocumentKeys.Share, Cancellation));
        hold!.Via.Should().Be(MemberVia.Members, "and holds every key of it");

        // Nobody else sees it, and a user does not open a document in another's name.
        await using (var cy = await filing.SessionAsync(TestCallers.User(data.Cy)))
        {
            (await cy.ListAsync<Guid>("SELECT id FROM filing.documents_i_see() AS id")).Should().BeEmpty();
        }

        var inAnothersName = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Cy), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            context.Documents.Add(new Document(DocumentId.CreateSequential(), "In his name", data.Hal, DocumentMembership.Owner, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync(Cancellation);
        })).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        inAnothersName.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>()
            .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Who_holds_a_key_that_changes_the_document_writes_its_members_under_the_documents_own_rule_and_nobody_else_does()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;

        // A few minutes back, so the database's clock, which its functions ask, need not agree with this machine's to the second.
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);

        Task ShareAsync(UserId by, UserId with)
            => filing.Services.AsAsync(TestCallers.User(by), async provider =>
            {
                var context = provider.GetRequiredService<FilingContext>();
                var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
                minutes.ShareWith(with, DocumentMembership.Onlooker, MemberPeriod.Open(now), now, by);
                await context.SaveChangesAsync(Cancellation);
            });

        async Task<long> SharesAsync()
            => (await filing.ListAsync<long>($"""SELECT count(*) FROM filing."DocumentShares" WHERE "DocumentId" = '{data.Minutes.Value}'""")).Single();

        var before = await SharesAsync();

        // Ada owns the minutes, and so holds the key that shares them: the member's row and its role's are
        // written as her, under the document's rule.
        await ShareAsync(by: data.Ada, with: data.Hal);
        (await SharesAsync()).Should().Be(before + 1);

        await using (var hal = await filing.SessionAsync(TestCallers.User(data.Hal)))
        {
            (await hal.ListAsync<Guid>("SELECT id FROM filing.documents_i_see() AS id")).Should().Equal([data.Minutes.Value], "a member from the moment the row is there");
        }

        // Cy looks on: he reads the minutes, and the database lets him write nothing of them, whatever statement reaches it.
        // It is the database that says no, to the member's row itself: its policy, not something the application checked.
        var refused = await FluentActions.Awaiting(() => ShareAsync(by: data.Cy, with: UserId.CreateSequential())).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        refused.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>()
            .Which.Should().Match<Npgsql.PostgresException>(denied => denied.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege && denied.MessageText.Contains("DocumentShares"));
        (await SharesAsync()).Should().Be(before + 1, "a caller without a key that changes the document adds no member to it");

        await using var cy = await filing.SessionAsync(TestCallers.User(data.Cy));
        (await cy.ListAsync<long>($"""WITH gone AS (DELETE FROM filing."DocumentShares" WHERE "DocumentId" = '{data.Minutes.Value}' RETURNING 1) SELECT count(*) FROM gone"""))
            .Should().Equal([0L], "and removes none");
    }

    [Fact]
    public async Task A_member_who_only_edits_writes_no_member_row_and_names_no_owner_past_the_application()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var minutes = data.Minutes.Value;
        var ben = data.Ben.Value;

        // Ben contributes to the minutes: he sees them and edits them, and the host's rule lets whoever edits a
        // document change it. He does not hold the key that shares.
        async Task<List<Guid>> HeldByBenAsync(string key)
        {
            await using var session = await filing.SessionAsync(TestCallers.User(data.Ben));
            return await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", key);
        }

        (await HeldByBenAsync(DocumentKeys.Edit)).Should().Contain(minutes);
        (await HeldByBenAsync(DocumentKeys.Share)).Should().NotContain(minutes);

        // Straight on the database, as himself, past every command of the application: his own share row is
        // one he may see, and he gives it the owner's role, which gives every key.
        const string GiveMyselfTheOwnersRole =
            """
            INSERT INTO filing."DocumentShareRoles" ("DocumentId", "DocumentShareId", "RoleId", "StartsAt", "EndsAt", "GivenBy")
            SELECT m."DocumentId", m."Id", 'owner', pg_catalog.now() - interval '1 hour', NULL, NULL
            FROM filing."DocumentShares" m WHERE m."DocumentId" = $1 AND m."MemberId" = $2
            """;
        await using (var session = await filing.SessionAsync(TestCallers.User(data.Ben)))
        {
            (await FluentActions.Awaiting(() => session.ListAsync<object>(GiveMyselfTheOwnersRole, minutes, ben)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.Should().Match<Npgsql.PostgresException>(denied => denied.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege && denied.MessageText.Contains("DocumentShareRoles"));
        }

        // A share row for somebody else, and a role he holds made to last, or taken away: none of it.
        await using (var session = await filing.SessionAsync(TestCallers.User(data.Ben)))
        {
            (await FluentActions.Awaiting(() => session.ListAsync<object>(
                    """
                    INSERT INTO filing."DocumentShares" ("DocumentId", "Id", "MemberId", "StartsAt", "EndsAt", "AddedBy")
                    VALUES ($1, pg_catalog.gen_random_uuid(), pg_catalog.gen_random_uuid(), pg_catalog.now() - interval '1 hour', NULL, NULL)
                    """,
                    minutes)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var session = await filing.SessionAsync(TestCallers.User(data.Ben)))
        {
            (await session.ListAsync<long>(
                """WITH changed AS (UPDATE filing."DocumentShareRoles" SET "RoleId" = 'owner' WHERE "DocumentId" = $1 RETURNING 1) SELECT count(*) FROM changed""", minutes))
                .Should().Equal([0L], "a row he does not hold the key for is no row he changes");
            (await session.ListAsync<long>(
                """WITH gone AS (DELETE FROM filing."DocumentShares" WHERE "DocumentId" = $1 RETURNING 1) SELECT count(*) FROM gone""", minutes))
                .Should().Equal([0L], "nor one he removes");
        }

        // And the owner column: the host's rule lets him change the document, and the lock keeps its owner what it was.
        await using (var session = await filing.SessionAsync(TestCallers.User(data.Ben)))
        {
            (await FluentActions.Awaiting(() => session.ListAsync<object>("UPDATE filing.\"Documents\" SET \"OwnerId\" = $2 WHERE \"Id\" = $1", minutes, ben)).Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.Should().Match<Npgsql.PostgresException>(denied =>
                    denied.SqlState == Npgsql.PostgresErrorCodes.InsufficientPrivilege && denied.MessageText.Contains("documents.share"));
        }

        // What he may do, he still does: the document's own columns are his to change, its owner as it was.
        await using (var session = await filing.SessionAsync(TestCallers.User(data.Ben)))
        {
            (await session.ListAsync<string>("UPDATE filing.\"Documents\" SET \"Title\" = 'Minutes, corrected', \"OwnerId\" = \"OwnerId\" WHERE \"Id\" = $1 RETURNING \"Title\"", minutes))
                .Should().Equal("Minutes, corrected");
        }

        // Nothing changed: he holds what he held, and the minutes are Ada's.
        (await HeldByBenAsync(DocumentKeys.Share)).Should().NotContain(minutes);
        var after = await filing.Services.ReadAsync(data.Minutes);
        after.OwnerId.Should().Be(data.Ada);
        after.Shares.Should().HaveCount(data.Documents[0].Shares.Count);
        after.Shares.Single(share => share.MemberId == data.Ben).Roles.Select(held => held.RoleId).Should().Equal(DocumentMembership.Contributor);

        // The same through the application, as the caller: a handler reached without its check writes nothing either.
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ben), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == data.Minutes, Cancellation);
            document.GiveRole(data.Ben, DocumentMembership.Owner, MemberPeriod.Open(from), from, by: data.Ben);
            await context.SaveChangesAsync(Cancellation);
        })).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        refused.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>().Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);

        // And a save that changes the owner column and nothing else, as a handler that went round its own command
        // would: the lock refuses it, with the toolkit's hint, so the caller is told access.refused as for a policy,
        // and the lock's name is in the failure, for the warning that names it.
        var handedOn = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ben), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == data.Minutes, Cancellation);
            context.Entry(document).Property(nameof(Document.OwnerId)).CurrentValue = data.Ben;
            await context.SaveChangesAsync(Cancellation);
        })).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        handedOn.Which.Code.Should().Be(DDDToolkit.Exceptions.ToolkitRefusals.Refused);
        var locked = handedOn.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>().Subject;
        (locked.SqlState, locked.ConstraintName, locked.Hint).Should().Be((Npgsql.PostgresErrorCodes.InsufficientPrivilege, "documents_owner_stays", "ddd:access.refused"));
        (await filing.Services.ReadAsync(data.Minutes)).OwnerId.Should().Be(data.Ada);
    }

    [Fact]
    public async Task Who_holds_the_keys_the_rules_name_changes_the_members_and_the_owner_through_the_application_as_the_caller()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Ada owns the minutes, and so holds the key that shares them. As herself, through the document's own
        // aggregate and the host's context under the policies: a share with a role, a role given, a role taken,
        // a share removed. Every row passes the lock.
        await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
            minutes.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(from), from, by: data.Ada);
            minutes.GiveRole(data.Dee, DocumentMembership.Contributor, MemberPeriod.Open(from), from, by: data.Ada);
            minutes.TakeRole(data.Ben, DocumentMembership.Contributor, DocumentMembership.Owner);
            minutes.Unshare(data.Cy);
            await context.SaveChangesAsync(Cancellation);
        });

        var shared = await filing.Services.ReadAsync(data.Minutes);
        shared.Shares.Select(share => share.MemberId).Should().Contain(data.Hal).And.NotContain(data.Cy);
        shared.Shares.Single(share => share.MemberId == data.Dee).Roles.Select(held => held.RoleId).Should().Equal(DocumentMembership.Contributor);
        shared.Shares.Single(share => share.MemberId == data.Ben).Roles.Should().BeEmpty();

        // Ian holds the owner's role on the minutes without owning them: every key, the one that shares among
        // them, through a role. He hands the minutes from Ada to Dee, as himself: the role taken from Ada, the
        // owner column changed under the trigger, and the role given to Dee are his to write, before the owner
        // changes and after.
        await filing.Services.AsAsync(TestCallers.User(data.Ian), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
            minutes.HandOver(data.Dee, DocumentMembership.Owner, from);
            await context.SaveChangesAsync(Cancellation);
        });

        var handed = await filing.Services.ReadAsync(data.Minutes);
        handed.OwnerId.Should().Be(data.Dee);
        handed.Shares.Single(share => share.MemberId == data.Dee).Roles.Select(held => held.RoleId).Should().BeEquivalentTo([DocumentMembership.Contributor, DocumentMembership.Owner]);
        handed.Shares.Single(share => share.MemberId == data.Ada).Roles.Should().BeEmpty("the owner before lost the owner's role, and stays on the document");
        handed.GetInvariantViolations().Should().BeEmpty();

        // Dee is the owner now, and holds the key by owning; Ada no longer does, and the database tells her so.
        await using (var dee = await filing.SessionAsync(TestCallers.User(data.Dee)))
        {
            (await dee.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", DocumentKeys.Share)).Should().Contain(data.Minutes.Value);
        }

        await using var ada = await filing.SessionAsync(TestCallers.User(data.Ada));
        (await ada.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", DocumentKeys.Share)).Should().NotContain(data.Minutes.Value);
        (await ada.ListAsync<long>(
            """WITH taken AS (UPDATE filing."Documents" SET "OwnerId" = $2 WHERE "Id" = $1 RETURNING 1) SELECT count(*) FROM taken""", data.Minutes.Value, data.Ada.Value))
            .Should().Equal([0L], "she is a member with no role now: the document is not hers to change at all");
    }

    [Fact]
    public async Task An_owner_that_holds_the_keys_by_owning_alone_hands_its_own_resource_on_as_the_applications_own_work()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Ben owns the budget, and holds its keys by owning it and through the owner's role. Handing it to Ada
        // is three statements: the owner's role taken from him, the owner column changed, the role given to
        // her. A policy asks what the caller holds when a statement runs, and after the second he holds
        // nothing: the third is not his to write, by the host's own rule and by the lock alike. The limit of a
        // database that checks every row, pinned here so it is known: nothing of the save stays.
        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ben), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var budget = await context.Documents.SingleAsync(document => document.Id == data.Budget, Cancellation);
            budget.HandOver(data.Ada, DocumentMembership.Owner, from);
            await context.SaveChangesAsync(Cancellation);
        })).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();
        refused.Which.GetBaseException().Should().BeOfType<Npgsql.PostgresException>().Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);

        var untouched = await filing.Services.ReadAsync(data.Budget);
        untouched.OwnerId.Should().Be(data.Ben);
        untouched.Shares.Single(share => share.MemberId == data.Ben).Roles.Select(held => held.RoleId).Should().Equal(DocumentMembership.Owner);

        // The host's command checks that he holds the key, and then saves the hand-over as its own work.
        var held = await filing.Services.AsAsync(TestCallers.User(data.Ben), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().RequireAsync(data.Budget, DocumentKeys.Share, Cancellation));
        held.Via.Should().Be(MemberVia.Members);
        await filing.Services.ChangeAsync(data.Budget, budget => budget.HandOver(data.Ada, DocumentMembership.Owner, from));

        (await filing.Services.ReadAsync(data.Budget)).OwnerId.Should().Be(data.Ada);
    }

    [Fact]
    public async Task The_application_itself_and_the_tables_owner_are_not_held_by_the_lock()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);

        // The application's own work runs as the role that owns the tables: it hands a document on and changes
        // its shares, as it seeded them. The lock is for the roles the rules name.
        await filing.Services.ChangeAsync(data.Minutes, document =>
        {
            document.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(from), from, by: null);
            document.HandOver(data.Ben, DocumentMembership.Owner, from);
        });

        var after = await filing.Services.ReadAsync(data.Minutes);
        (after.OwnerId, after.Shares.Any(share => share.MemberId == data.Hal)).Should().Be((data.Ben, true));

        // A folder has no rule that lets a user change it, and its lock is for users and archivists alike: the keeper of the
        // cabinet holds the key that hands it over, and the folder's own rules give nobody a way in. The lock adds none.
        await using var keeper = await filing.SessionAsync(TestCallers.Staff(data.Keeper));
        (await keeper.ListAsync<long>(
            """WITH handed AS (UPDATE filing."Folders" SET "Keeper" = 'C-014' WHERE "Id" = 7 RETURNING 1) SELECT count(*) FROM handed"""))
            .Should().Equal([0L], "a restrictive policy and a trigger allow nothing by themselves");
    }

    [Fact]
    public async Task With_row_security_forced_on_every_table_the_functions_and_the_lock_answer_as_they_did()
    {
        // A host that holds the tables' owner to their policies as well. The functions run as their owner, so
        // that owner has to be let past the policies of the tables they read: the role that runs the migrations
        // may bypass row level security, as Supabase's may.
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, asCaller: true, seed: false);
        await FilingPostgres.ExecuteAsync(filing.Database.SuperuserConnectionString, $"ALTER ROLE {FilingPostgres.LoginRole} BYPASSRLS", Cancellation);
        await filing.ExecuteAsync(FilingPostgres.AccessScript(force: true));
        await filing.Scenario.SaveAsync(filing.Services);
        var data = filing.Scenario;

        try
        {
            (await filing.ListAsync<bool>(
                """SELECT bool_and(c.relforcerowsecurity) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'filing' AND c.relkind = 'r'"""))
                .Should().Equal(true);
            await filing.Services.AsAsync(Caller.System, provider =>
                DDDToolkit.EntityFramework.Postgres.PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(provider.GetRequiredService<FilingContext>(), Cancellation));
            await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(filing.Services.Provider, Cancellation);

            // The functions answer what the rules say read in memory, for everybody and every key.
            foreach (var user in data.Users)
            {
                await using var session = await filing.SessionAsync(TestCallers.User(user));
                (await session.ListAsync<Guid>("SELECT id FROM filing.documents_i_see() AS id")).Should().BeEquivalentTo(data.DocumentsSeenBy(user).Select(id => id.Value), "of what " + user + " sees");
                foreach (var key in FilingScenario.DocumentKeysAsked)
                {
                    (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", key))
                        .Should().BeEquivalentTo(data.DocumentsHeldBy(user, key).Select(id => id.Value), user + " holds " + key);
                    var hold = await filing.Services.AsAsync(TestCallers.User(user), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(data.Minutes, key, Cancellation));
                    (hold?.Via is not null).Should().Be(data.DocumentsHeldBy(user, key).Contains(data.Minutes));
                }
            }

            // And the lock holds: Ben edits and writes no role for himself; Ada shares as herself.
            await using (var ben = await filing.SessionAsync(TestCallers.User(data.Ben)))
            {
                (await FluentActions.Awaiting(() => ben.ListAsync<object>("UPDATE filing.\"Documents\" SET \"OwnerId\" = $2 WHERE \"Id\" = $1", data.Minutes.Value, data.Ben.Value)).Should().ThrowAsync<Npgsql.PostgresException>())
                    .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
            }

            var from = DateTimeOffset.UtcNow.AddMinutes(-5);
            await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
            {
                var context = provider.GetRequiredService<FilingContext>();
                var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
                minutes.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(from), from, by: data.Ada);
                await context.SaveChangesAsync(Cancellation);
            });
            (await filing.Services.AsAsync(TestCallers.User(data.Hal), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().ViaAsync(data.Minutes, DocumentKeys.View, Cancellation)))
                .Should().Be(MemberVia.Members);
        }
        finally
        {
            // The role is the run's, shared by every test's database: what this test gave it, it takes back.
            await FilingPostgres.ExecuteAsync(filing.Database.SuperuserConnectionString, $"ALTER ROLE {FilingPostgres.LoginRole} NOBYPASSRLS", CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_role_that_ran_out_is_given_again_and_an_ended_membership_begun_again_in_one_save_under_the_policies()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true);
        var data = filing.Scenario;
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var before = await filing.Services.ReadAsync(data.Minutes);

        // As Ada, who owns the minutes and so may share them. Fay's role as a contributor ended yesterday: giving it
        // again replaces the one hold of that role, the row the roles' key allows once. Eve's membership ended
        // yesterday: she comes back in a new row, and the old one goes with the role held in it.
        await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
            minutes.GiveRole(data.Fay, DocumentMembership.Contributor, MemberPeriod.Open(from), from, by: data.Ada);
            minutes.ShareWith(data.Eve, MemberPeriod.Open(from), from, by: data.Ada);
            await context.SaveChangesAsync(Cancellation);
        });

        var after = await filing.Services.ReadAsync(data.Minutes);
        after.Version.Should().Be(before.Version + 1, "both changes are one save of the document");
        after.GetInvariantViolations().Should().BeEmpty();

        var fay = after.Shares.Single(share => share.MemberId == data.Fay);
        fay.Roles.Should().HaveCount(2, "the role that starts tomorrow is still hers");
        fay.Roles.Where(held => held.RoleId == DocumentMembership.Contributor).Should().ContainSingle()
            .Which.Should().Match<MemberRole<UserId, NamedRole>>(held => held.EndsAt == null && held.GivenBy == data.Ada);

        var eve = after.Shares.Where(share => share.MemberId == data.Eve).Should().ContainSingle().Subject;
        eve.Id.Should().NotBe(before.Shares.Single(share => share.MemberId == data.Eve).Id, "it is a new membership, and a new row");
        (eve.EndsAt, eve.Roles.Count).Should().Be((null, 0));

        // And the database answers from the rows as they are now: Fay changes the minutes, Eve sees them and no more.
        await using var asFay = await filing.SessionAsync(TestCallers.User(data.Fay));
        (await asFay.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", DocumentKeys.Edit)).Should().Equal(data.Minutes.Value);

        await using var asEve = await filing.SessionAsync(TestCallers.User(data.Eve));
        (await asEve.ListAsync<Guid>("SELECT id FROM filing.documents_i_see() AS id")).Should().Equal(data.Minutes.Value);
        (await asEve.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", DocumentKeys.Edit)).Should().BeEmpty();
    }

    [Fact]
    public async Task With_the_privileges_written_from_the_policies_the_functions_still_answer_and_members_still_read_and_write()
    {
        // A host that lets the export write its tables' privileges: every privilege is taken back, and a role gets
        // what its policies allow. The functions read the member rows as their owner, so they need none of it.
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, asCaller: true, seed: false);
        await filing.ExecuteAsync(FilingPostgres.AccessScript(writeGrants: true));
        await filing.Scenario.SaveAsync(filing.Services);
        var data = filing.Scenario;

        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(filing.Services.Provider, Cancellation);

        // Read under the policies, with the privileges the policies gave.
        var seen = await filing.Services.AsAsync(TestCallers.User(data.Ben), provider => provider.GetRequiredService<FilingContext>().Documents.AsNoTracking().Select(document => document.Title).ToListAsync(Cancellation));
        seen.Should().BeEquivalentTo("Minutes", "Budget");
        (await filing.Services.AsAsync(TestCallers.Staff(data.Visitor), provider => provider.GetRequiredService<FilingContext>().Folders.AsNoTracking().Select(folder => folder.Id).ToListAsync(Cancellation)))
            .Should().Equal(data.Cabinet);

        // Written by who holds the key that shares the document: its version, a member's row and its role's.
        await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            var minutes = await context.Documents.SingleAsync(document => document.Id == data.Minutes, Cancellation);
            // A few minutes back, so the database's clock, which its functions ask, need not agree with this machine's to the second.
            var from = DateTimeOffset.UtcNow.AddMinutes(-5);
            minutes.ShareWith(data.Hal, DocumentMembership.Onlooker, MemberPeriod.Open(from), from, by: data.Ada);
            await context.SaveChangesAsync(Cancellation);
        });
        (await filing.Services.AsAsync(TestCallers.User(data.Hal), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().ViaAsync(data.Minutes, DocumentKeys.View, Cancellation)))
            .Should().Be(MemberVia.Members);

        // Who a share is of is written once: the privileges leave the column out, so not even who may share the
        // document moves Ben's share, with its role, to somebody else.
        await using (var ada = await filing.SessionAsync(TestCallers.User(data.Ada)))
        {
            (await FluentActions.Awaiting(() => ada.ListAsync<long>(
                        """WITH moved AS (UPDATE filing."DocumentShares" SET "MemberId" = $1 WHERE "DocumentId" = $2 AND "MemberId" = $3 RETURNING 1) SELECT count(*) FROM moved""",
                        Guid.NewGuid(),
                        data.Minutes.Value,
                        data.Ben.Value))
                    .Should().ThrowAsync<Npgsql.PostgresException>())
                .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
        }

        // A folder has no rule that changes it, so a signed-in user holds no privilege to: refused outright, not answered empty.
        await using var clerk = await filing.SessionAsync(TestCallers.Staff(data.Clerk));
        (await FluentActions.Awaiting(() => clerk.ListAsync<long>("""WITH gone AS (DELETE FROM filing."FolderStaff" RETURNING 1) SELECT count(*) FROM gone""")).Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InsufficientPrivilege);
    }
}
