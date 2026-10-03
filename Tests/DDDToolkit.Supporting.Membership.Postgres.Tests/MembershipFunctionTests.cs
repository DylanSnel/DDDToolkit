using Npgsql;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The four set functions of each resource, asked of the database as a caller, against what the access
/// questions answer in C# for the same data, and against the rules read over the aggregates in memory: three
/// readings of one declaration, which have to agree.
/// </summary>
public sealed class MembershipFunctionTests(FilingPostgres postgres)
{
    private const string Filing = FilingContext.Schema + ".";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_functions_of_the_documents_answer_what_the_access_questions_answer()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured);
        var data = filing.Scenario;
        var functions = DocumentMembership.Rules.Functions;

        foreach (var user in data.Users)
        {
            var caller = TestCallers.User(user);
            await using var session = await filing.SessionAsync(caller);

            // The documents the caller sees, and those it is a member of: the same, for members that are users.
            var seenInCSharp = await filing.Services.AsAsync(caller, async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<DocumentId>>().KeyReach([]).See;
                return await provider.GetRequiredService<FilingContext>().Documents.Within(see).Select(document => document.Id.Value).ToListAsync(Cancellation);
            });
            var seen = data.DocumentsSeenBy(user).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>($"SELECT id FROM {Filing}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + user + " sees");
            (await session.ListAsync<Guid>($"SELECT id FROM {Filing}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);
            seenInCSharp.Should().BeEquivalentTo(seen);

            foreach (var key in FilingScenario.DocumentKeysAsked)
            {
                var heldInCSharp = await filing.Services.AsAsync(caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<DocumentId>>().Reach(key);
                    return await provider.GetRequiredService<FilingContext>().Documents.Within(reach).Select(document => document.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.DocumentsHeldBy(user, key).Select(id => id.Value).ToList();
                var because = "of what " + user + " holds " + key + " on";

                (await session.ListAsync<Guid>($"SELECT id FROM {Filing}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);

                // Through a role alone: what being a member or owning the document gives is not a role's to give.
                (await session.ListAsync<Guid>($"SELECT id FROM {Filing}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.DocumentsWithARoleFor(user, key).Select(id => id.Value), because + " through a role");
            }
        }
    }

    [Fact]
    public async Task The_functions_of_the_folders_answer_what_the_access_questions_answer()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured);
        var data = filing.Scenario;
        var functions = FolderMembership.Rules.Functions;

        foreach (var staff in data.Staff)
        {
            // Known by the claim the folder's rules name, in C# and in the database alike.
            var caller = TestCallers.Staff(staff);
            await using var session = await filing.SessionAsync(caller);

            var seen = data.FoldersSeenBy(staff).Select(id => id.Value).ToList();
            (await session.ListAsync<long>($"SELECT id FROM {Filing}{functions.Seen}() AS id")).Should().BeEquivalentTo(seen, "of what " + staff + " sees");
            (await session.ListAsync<long>($"SELECT id FROM {Filing}{functions.AsMember}() AS id")).Should().BeEquivalentTo(seen);

            foreach (var key in FilingScenario.FolderKeysAsked)
            {
                var heldInCSharp = await filing.Services.AsAsync(caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<FolderId>>().Reach(key);
                    return await provider.GetRequiredService<FilingContext>().Folders.Within(reach).Select(folder => folder.Id.Value).ToListAsync(Cancellation);
                });
                var held = data.FoldersHeldBy(staff, key).Select(id => id.Value).ToList();
                var because = "of what " + staff + " holds " + key + " on";

                (await session.ListAsync<long>($"SELECT id FROM {Filing}{functions.HeldOn}($1) AS id", key)).Should().BeEquivalentTo(held, because);
                heldInCSharp.Should().BeEquivalentTo(held, because);
                (await session.ListAsync<long>($"SELECT id FROM {Filing}{functions.AsMemberWith}($1) AS id", key))
                    .Should().BeEquivalentTo(data.FoldersWithARoleFor(staff, key).Select(id => id.Value), because + " through a role");
            }
        }
    }

    [Fact]
    public async Task A_caller_who_is_no_member_is_answered_nothing()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured);
        var data = filing.Scenario;

        // Hal is a user and a member of no document; Ada owns a document and her token carries no staff code; and
        // a token that names no user at all.
        (Caller Caller, string What)[] outsiders =
        [
            (TestCallers.User(data.Hal), "a user who is no member"),
            (TestCallers.Staff(data.Nobody, user: data.Hal), "staff on no folder"),
            (Callers.FromClaims("""{"role":"authenticated"}"""), "a token without a user"),
        ];

        foreach (var (caller, what) in outsiders)
        {
            await using var session = await filing.SessionAsync(caller);
            foreach (var functions in new[] { DocumentMembership.Rules.Functions, FolderMembership.Rules.Functions })
            {
                (await session.ListAsync<object>($"SELECT id FROM {Filing}{functions.Seen}() AS id")).Should().BeEmpty(what);
                (await session.ListAsync<object>($"SELECT id FROM {Filing}{functions.AsMember}() AS id")).Should().BeEmpty(what);
                foreach (var key in FilingScenario.DocumentKeysAsked.Concat(FilingScenario.FolderKeysAsked))
                {
                    (await session.ListAsync<object>($"SELECT id FROM {Filing}{functions.HeldOn}($1) AS id", key)).Should().BeEmpty(what);
                    (await session.ListAsync<object>($"SELECT id FROM {Filing}{functions.AsMemberWith}($1) AS id", key)).Should().BeEmpty(what);
                }
            }
        }

        // A member of one is answered nothing by the functions of the other.
        await using var ada = await filing.SessionAsync(TestCallers.User(data.Ada));
        (await ada.ListAsync<Guid>($"SELECT id FROM {Filing}{DocumentMembership.Rules.Functions.Seen}() AS id")).Should().HaveCount(2);
        (await ada.ListAsync<long>($"SELECT id FROM {Filing}{FolderMembership.Rules.Functions.Seen}() AS id")).Should().BeEmpty();

        await using var keeper = await filing.SessionAsync(TestCallers.Staff(data.Keeper));
        (await keeper.ListAsync<long>($"SELECT id FROM {Filing}{FolderMembership.Rules.Functions.Seen}() AS id")).Should().Equal(data.Cabinet.Value);
        (await keeper.ListAsync<Guid>($"SELECT id FROM {Filing}{DocumentMembership.Rules.Functions.Seen}() AS id")).Should().BeEmpty();
    }

    [Fact]
    public async Task Each_function_runs_as_its_owner_with_an_empty_search_path_and_only_for_the_roles_its_rules_name()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // name | arguments | what it answers | runs as its owner | its settings | who may execute it, of the roles there are
        var found = await filing.ListAsync<string>(
            $"""
            SELECT p.proname || ' | ' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ' | ' || pg_catalog.pg_get_function_result(p.oid)
                   || ' | ' || p.prosecdef || ' | ' || pg_catalog.array_to_string(p.proconfig, ',') || ' | '
                   || coalesce((SELECT pg_catalog.string_agg(r.rolname, ',' ORDER BY r.rolname) FROM pg_catalog.pg_roles r
                                WHERE r.rolname IN ('authenticated', 'anon', 'ddd_system_in', '{FilingPostgres.ArchivistRole}')
                                  AND pg_catalog.has_function_privilege(r.oid, p.oid, 'EXECUTE')), 'nobody')
            FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{FilingContext.Schema}'
            ORDER BY 1
            """);

        // The four of each resource, and the function its lock's trigger runs: that one asks as the caller,
        // and nobody is given it, since a trigger's function is never called by itself.
        found.Should().Equal(
            "documents_as_member |  | SETOF uuid | true | search_path=\"\" | authenticated",
            "documents_as_member_with | text | SETOF uuid | true | search_path=\"\" | authenticated",
            "documents_i_see |  | SETOF uuid | true | search_path=\"\" | authenticated",
            "documents_owner_stays |  | trigger | false | search_path=\"\" | nobody",
            "documents_where_i_hold | text | SETOF uuid | true | search_path=\"\" | authenticated",
            "folder_ids_held | text | SETOF bigint | true | search_path=\"\" | authenticated," + FilingPostgres.ArchivistRole,
            "folder_ids_seen |  | SETOF bigint | true | search_path=\"\" | authenticated," + FilingPostgres.ArchivistRole,
            "folder_ids_staffed |  | SETOF bigint | true | search_path=\"\" | authenticated," + FilingPostgres.ArchivistRole,
            "folder_ids_staffed_with | text | SETOF bigint | true | search_path=\"\" | authenticated," + FilingPostgres.ArchivistRole,
            "folders_owner_stays |  | trigger | false | search_path=\"\" | nobody");

        // Nobody else: not every role there is, and not a caller that did not sign in.
        (await filing.ListAsync<long>(
            $"""
            SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{FilingContext.Schema}' AND pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
            """)).Should().Equal(0);

        await using var anonymous = await filing.SessionAsync(Caller.Anonymous, role: "anon");
        (await FluentActions.Awaiting(() => anonymous.ListAsync<Guid>($"SELECT id FROM {Filing}documents_i_see() AS id")).Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_token_role_the_rules_name_asks_that_resources_functions_and_no_others()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured);
        var data = filing.Scenario;

        // The folder's rules let the archivists' role ask its functions, next to signed-in users; the document's name no such role.
        await using var archivist = await filing.SessionAsync(TestCallers.Staff(data.Clerk), role: FilingPostgres.ArchivistRole);

        (await archivist.ListAsync<long>($"SELECT id FROM {Filing}folder_ids_seen() AS id")).Should().BeEquivalentTo([data.Cabinet.Value, data.Annex.Value]);
        (await archivist.ListAsync<long>($"SELECT id FROM {Filing}folder_ids_held($1) AS id", FolderKeys.Staff)).Should().Equal(data.Annex.Value);

        await using var again = await filing.SessionAsync(TestCallers.Staff(data.Clerk), role: FilingPostgres.ArchivistRole);
        (await FluentActions.Awaiting(() => again.ListAsync<Guid>($"SELECT id FROM {Filing}documents_i_see() AS id")).Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_period_counts_from_its_first_moment_and_no_longer_at_its_last_in_the_database()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured);
        var data = filing.Scenario;
        var functions = DocumentMembership.Rules.Functions;
        var minutes = data.Minutes.Value;

        // The database's clock stands still in a transaction, so a row set to now() in a session is at exactly
        // the moment every question of that session is asked at. Ben contributes to the minutes; Cy looks on.
        string Membership(string column, UserId member)
            => $"UPDATE filing.\"DocumentShares\" SET \"{column}\" = pg_catalog.now() WHERE \"DocumentId\" = '{minutes}' AND \"MemberId\" = '{member.Value}'";
        string Role(string column, UserId member)
            => $"UPDATE filing.\"DocumentShareRoles\" h SET \"{column}\" = pg_catalog.now() FROM filing.\"DocumentShares\" m "
               + $"WHERE h.\"DocumentId\" = m.\"DocumentId\" AND h.\"DocumentShareId\" = m.\"Id\" AND m.\"DocumentId\" = '{minutes}' AND m.\"MemberId\" = '{member.Value}'";

        async Task<(bool Member, bool Seen, bool WithARole, bool Held)> AskAsync(UserId user, string? before)
        {
            await using var session = await filing.SessionAsync(TestCallers.User(user), before: before);
            return (
                (await session.ListAsync<Guid>($"SELECT id FROM filing.{functions.AsMember}() AS id")).Contains(minutes),
                (await session.ListAsync<Guid>($"SELECT id FROM filing.{functions.Seen}() AS id")).Contains(minutes),
                (await session.ListAsync<Guid>($"SELECT id FROM filing.{functions.AsMemberWith}($1) AS id", DocumentKeys.Edit)).Contains(minutes),
                (await session.ListAsync<Guid>($"SELECT id FROM filing.{functions.HeldOn}($1) AS id", DocumentKeys.Edit)).Contains(minutes));
        }

        // As the rows are: a member, seen, with a role that gives the key, which is held.
        (await AskAsync(data.Ben, before: null)).Should().Be((true, true, true, true));

        // A membership that starts at this very moment counts already, and one that ends at it counts no longer.
        (await AskAsync(data.Ben, Membership("StartsAt", data.Ben))).Should().Be((true, true, true, true), "a period counts from its first moment");
        (await AskAsync(data.Ben, Membership("EndsAt", data.Ben))).Should().Be((false, false, false, false), "an end is the first moment a period no longer counts");

        // The same for a role held in a membership that runs: the member stays one, and the key comes and goes with the role.
        (await AskAsync(data.Ben, Role("StartsAt", data.Ben))).Should().Be((true, true, true, true));
        (await AskAsync(data.Ben, Role("EndsAt", data.Ben))).Should().Be((true, true, false, false));

        // And each session rolled its change back: the rows are as they were.
        (await AskAsync(data.Ben, before: null)).Should().Be((true, true, true, true));
    }

    [Fact]
    public async Task Work_in_a_scope_the_rules_name_is_answered_every_resource_by_the_database_and_work_in_another_scope_none()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain);
        var data = filing.Scenario;

        // The folder's rules as they are, and the role scoped work runs as among those that may ask: the
        // functions are the same four, and the scoped system role may execute them.
        var registered = FolderMembership.Rules;
        var asked = new MembershipRules(
            registered.Name,
            keys: registered.Keys,
            roles: [.. registered.Roles],
            members: registered.Members,
            ownerRole: registered.OwnerRole,
            memberKeys: registered.MemberKeys,
            codes: registered.Codes,
            functions: registered.Functions,
            grantTo: [.. registered.GrantTo, DDDToolkit.Abstractions.Attributes.RowAccessRoles.SystemIn],
            changeMembersKey: registered.ChangeMembersKey,
            changeOwnerKey: registered.ChangeOwnerKey,
            systemScopes: registered.SystemScopes);
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new DocumentMembershipFunctions(), new MembershipRowAccessContribution<FolderMember>(asked)], rules: []));

        static string Scoped(string scope) => $$"""{"role":"ddd_system_in","scope":"{{scope}}"}""";
        long[] every = [data.Cabinet.Value, data.Annex.Value];

        // The module's own scope: every folder is seen, and every key is held on every folder. Nobody's member.
        await using (var own = await filing.SessionAsync(Callers.FromClaims(Scoped(FolderMembership.Scope)), role: "ddd_system_in"))
        {
            (await own.ListAsync<long>("SELECT id FROM filing.folder_ids_seen() AS id")).Should().BeEquivalentTo(every);
            (await own.ListAsync<long>("SELECT id FROM filing.folder_ids_held($1) AS id", FolderKeys.Shred)).Should().BeEquivalentTo(every);
            (await own.ListAsync<long>("SELECT id FROM filing.folder_ids_held($1) AS id", "folders.unheard-of")).Should().BeEquivalentTo(every, "the application's own work holds every key there is");
            (await own.ListAsync<long>("SELECT id FROM filing.folder_ids_staffed() AS id")).Should().BeEmpty();
            (await own.ListAsync<long>("SELECT id FROM filing.folder_ids_staffed_with($1) AS id", FolderKeys.Read)).Should().BeEmpty();
        }

        // Another module's scope, and no scope at all: nothing, as in C#.
        foreach (var claims in new[] { Scoped("other"), """{"role":"ddd_system_in"}""" })
        {
            await using var elsewhere = await filing.SessionAsync(Callers.FromClaims(claims), role: "ddd_system_in");
            (await elsewhere.ListAsync<long>("SELECT id FROM filing.folder_ids_seen() AS id")).Should().BeEmpty();
            (await elsewhere.ListAsync<long>("SELECT id FROM filing.folder_ids_held($1) AS id", FolderKeys.Read)).Should().BeEmpty();
        }

        // A signed-in user whose token happens to carry that scope is a user: its role is not the one scoped work runs as.
        await using var user = await filing.SessionAsync(Callers.FromClaims($$"""{"sub":"{{data.Hal.Value}}","role":"authenticated","scope":"{{FolderMembership.Scope}}"}"""));
        (await user.ListAsync<long>("SELECT id FROM filing.folder_ids_seen() AS id")).Should().BeEmpty();
        (await user.ListAsync<long>("SELECT id FROM filing.folder_ids_held($1) AS id", FolderKeys.Read)).Should().BeEmpty();

        // And what C# answers for the same callers.
        var hold = await filing.Services.AsAsync(Caller.SystemIn(FolderMembership.Scope), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Annex, FolderKeys.Shred, Cancellation));
        hold!.Via.Should().Be(MemberVia.System);
        (await filing.Services.AsAsync(Caller.SystemIn("other"), provider => provider.GetRequiredService<IMemberQuestions<FolderId>>().HoldAsync(data.Annex, FolderKeys.Read, Cancellation))).Should().BeNull();
    }

    [Fact]
    public async Task Each_resources_functions_read_that_resources_tables_alone()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        var bodies = await filing.ListAsync<string>(
            $"""
            SELECT p.proname || ': ' || p.prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = '{FilingContext.Schema}' ORDER BY 1
            """);

        // The four functions of each, and the one its lock's trigger runs.
        bodies.Where(body => body.StartsWith("documents_", StringComparison.Ordinal)).Should().HaveCount(5)
            .And.NotContain(body => body.Contains("Folder", StringComparison.Ordinal) || body.Contains("folder", StringComparison.Ordinal));
        bodies.Where(body => body.StartsWith("folder", StringComparison.Ordinal)).Should().HaveCount(5)
            .And.NotContain(body => body.Contains("Document", StringComparison.Ordinal) || body.Contains("document", StringComparison.Ordinal));

        // And each says the host's own names: the folder's tables and its own column for who a member is.
        bodies.Single(body => body.StartsWith("folder_ids_staffed:", StringComparison.Ordinal)).Should()
            .Contain($"\"{FilingContext.Schema}\".\"{FilingContext.FolderStaffTable}\"").And.Contain($"\"{FilingContext.StaffColumn}\"");
        bodies.Single(body => body.StartsWith("folder_ids_staffed_with:", StringComparison.Ordinal)).Should().Contain($"\"{FilingContext.Schema}\".\"{FilingContext.FolderStaffRolesTable}\"");
    }
}
