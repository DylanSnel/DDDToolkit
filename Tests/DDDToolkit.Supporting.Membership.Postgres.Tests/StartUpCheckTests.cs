using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// The start-up check: a host whose database does not have the functions of every resource, as the rules that
/// resource is registered with say them, does not start, and is told which function of which resource is wrong
/// and what to do. The same for the lock the rules name a key for, for the trigger that keeps the owner's role
/// of kept roles in use, and for a table of kept roles that callers may write as they like. What the rules leave
/// open is said, and refused by nobody.
/// </summary>
public sealed class StartUpCheckTests(FilingPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Task<IReadOnlyList<string>> CheckAsync(PostgresFiling filing) => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(filing.Services.Provider, Cancellation);

    [Fact]
    public async Task A_database_with_the_functions_as_the_rules_say_them_passes()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // The documents and the folders name both keys of the lock: there is nothing to remark on either.
        (await CheckAsync(filing)).Should().BeEmpty();

        // And for a host whose contexts run as the caller, whoever is calling when it starts: the check asks as the application itself.
        using var asCaller = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, asCaller: true, seed: false);
        using (Callers.Begin(Caller.Anonymous))
        {
            await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(asCaller.Services.Provider, Cancellation);
        }
    }

    [Fact]
    public async Task The_check_asks_as_the_application_itself_where_every_flow_of_work_has_to_say_who_it_runs_as()
    {
        // A host that requires every flow of work to say its caller, whose contexts run as that caller. At
        // start-up nobody is calling.
        using var filing = await PostgresFiling.CreateAsync(
            postgres, FilingPostgres.Template.Secured, asCaller: true, seed: false, configure: services => services.RequireExplicitCallers());

        // With nobody calling, a context of this host cannot ask the database anything.
        await FluentActions.Awaiting(async () =>
        {
            await using var scope = filing.Services.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FilingContext>().Documents.CountAsync(Cancellation);
        }).Should().ThrowAsync<NoCallerException>();

        // The check does all the same: it begins as the application itself around what it asks.
        Callers.Ambient.Should().BeNull();
        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_database_without_the_functions_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);

        var refused = await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>();

        refused.Which.Message.Should()
            .StartWith("The functions that answer the membership of Document in the database are not as its rules, 'documents', say them: ")
            .And.Contain("filing.documents_as_member (missing), filing.documents_as_member_with (missing), filing.documents_i_see (missing), filing.documents_where_i_hold (missing)")
            .And.Contain("Mark the rules the resource is registered with [MembershipRules<DocumentShare>]")
            .And.Contain("where the project that runs the export sees them")
            .And.Contain("apply the access files");
    }

    [Fact]
    public async Task A_database_with_the_functions_of_one_resource_is_refused_for_the_other()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);

        // The host listed the documents' contribution and forgot the folders'.
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new DocumentMembershipFunctions()], rules: []));

        var refused = await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>();

        refused.Which.Message.Should()
            .StartWith("The functions that answer the membership of Folder in the database are not as its rules, 'folders', say them: ")
            .And.Contain("filing.folder_ids_held (missing), filing.folder_ids_seen (missing), filing.folder_ids_staffed (missing), filing.folder_ids_staffed_with (missing)")
            .And.Contain("[MembershipRules<FolderMember>]")
            .And.NotContain("documents_");

        // With both, it starts.
        await filing.ExecuteAsync(FilingPostgres.AccessScript(rules: []));
        await CheckAsync(filing);
    }

    [Theory]
    [InlineData("a role gives another key")]
    [InlineData("another key is what being a member gives")]
    [InlineData("the resource has another key")]
    [InlineData("another role may ask")]
    [InlineData("the members are known by another claim")]
    [InlineData("another key changes the members")]
    [InlineData("no key changes the owner")]
    [InlineData("another scope is the host's own work")]
    public async Task Functions_written_from_other_rules_than_the_resource_is_registered_with_are_refused(string difference)
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);

        // The export ran with rules that differ from the registered ones in one thing the functions are written from.
        var registered = FolderMembership.Rules;
        IReadOnlyList<DeclaredRole> roles =
        [
            new("keeper", [FolderKeys.Read, FolderKeys.File, FolderKeys.Staff, FolderKeys.Shred]),
            new("clerk", difference == "a role gives another key" ? [FolderKeys.Read, FolderKeys.File, FolderKeys.Staff] : [FolderKeys.Read, FolderKeys.File, FolderKeys.Shred]),
            new("visitor", [FolderKeys.Read]),
        ];
        var exported = new MembershipRules(
            registered.Name,
            keys: difference == "the resource has another key" ? [.. registered.Keys, "folders.audit"] : registered.Keys,
            roles,
            members: difference == "the members are known by another claim" ? MemberSource.Claim("app_metadata.badge") : registered.Members,
            seeKey: difference == "another key is what being a member gives" ? FolderKeys.Read : null,
            ownerRole: registered.OwnerRole,
            memberKeys: registered.MemberKeys,
            codes: registered.Codes,
            functions: registered.Functions,
            grantTo: difference == "another role may ask" ? [RowAccessRoles.User] : registered.GrantTo,
            changeMembersKey: difference == "another key changes the members" ? FolderKeys.File : registered.ChangeMembersKey,
            changeOwnerKey: difference == "no key changes the owner" ? null : registered.ChangeOwnerKey,
            systemScopes: difference == "another scope is the host's own work" ? ["archive"] : registered.SystemScopes);
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new DocumentMembershipFunctions(), new MembershipRowAccessContribution<FolderMember>(exported)], rules: []));

        var refused = await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>();

        refused.Which.Message.Should()
            .StartWith("The functions that answer the membership of Folder in the database are not as its rules, 'folders', say them: ")
            .And.Contain("filing.folder_ids_held (it was written from other rules), filing.folder_ids_seen (it was written from other rules), "
                + "filing.folder_ids_staffed (it was written from other rules), filing.folder_ids_staffed_with (it was written from other rules)");
    }

    [Fact]
    public async Task Rules_that_say_the_same_in_another_instance_are_the_same_rules()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);

        // Declared again, entry for entry: what the functions are written from is what the rules say, not which object says it.
        var registered = FolderMembership.Rules;
        var again = new MembershipRules(
            registered.Name,
            [.. registered.Keys],
            [.. registered.Roles],
            registered.Members,
            registered.SeeKey,
            registered.OwnerRole,
            registered.MemberKeys,
            MembershipCodes.Under("elsewhere"),
            registered.Functions,
            registered.GrantTo,
            changeMembersKey: registered.ChangeMembersKey,
            changeOwnerKey: registered.ChangeOwnerKey,
            systemScopes: [.. registered.SystemScopes]);
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new DocumentMembershipFunctions(), new MembershipRowAccessContribution<FolderMember>(again)], rules: []));

        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_function_that_was_changed_by_hand_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // No longer pinned to an empty search path: the schemas of a caller's session would decide what it reads, as its owner.
        await filing.ExecuteAsync("ALTER FUNCTION filing.documents_i_see() RESET search_path");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_i_see (it has no empty search path). ");

        // Running as its caller, who reads the member rows under the policies that ask it.
        await filing.ExecuteAsync("ALTER FUNCTION filing.documents_i_see() SET search_path = ''; ALTER FUNCTION filing.documents_where_i_hold(text) SECURITY INVOKER");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_where_i_hold (it does not run as its owner). ");

        // Written again by hand, with a body of somebody's own.
        await filing.ExecuteAsync(
            """
            ALTER FUNCTION filing.documents_where_i_hold(text) SECURITY DEFINER;
            CREATE OR REPLACE FUNCTION filing.documents_as_member() RETURNS SETOF uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path = ''
                AS $$ SELECT d."Id" FROM filing."Documents" d $$;
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_as_member (it was written from other rules). ");

        // A function of the name that takes something else is not the function.
        await filing.ExecuteAsync(FilingPostgres.AccessScript(rules: FilingPostgres.Rules));
        await CheckAsync(filing);
        await filing.ExecuteAsync(
            """
            DROP POLICY IF EXISTS "Users read the documents they see (select) for authenticated" ON filing."Documents";
            DROP FUNCTION filing.documents_i_see();
            CREATE FUNCTION filing.documents_i_see(uuid) RETURNS SETOF uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$ SELECT $1 $$;
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_i_see (missing). ");
    }

    [Fact]
    public async Task A_function_that_answers_one_value_where_a_set_is_asked_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);
        await filing.ExecuteAsync(FilingPostgres.AccessScript(rules: []));
        await CheckAsync(filing);

        // Of the right name, running as its owner with an empty search path, and answering one id: a policy
        // that asks which documents a caller sees would be answered one of them, or none.
        await filing.ExecuteAsync(
            """
            DROP FUNCTION filing.documents_i_see();
            CREATE FUNCTION filing.documents_i_see() RETURNS uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$ SELECT NULL::uuid $$;
            REVOKE ALL ON FUNCTION filing.documents_i_see() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION filing.documents_i_see() TO authenticated;
            """);

        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_i_see (it answers no set). ");
    }

    [Fact]
    public async Task What_the_check_reads_of_a_functions_body_is_the_line_that_names_its_rules()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // The limit of the check, said here so nobody reads more into it: a body rewritten by hand that keeps
        // its first line is not found. The check holds a database to the rules and the version of the package
        // its files were written from, not against whoever may rewrite its functions.
        var first = (await filing.ListAsync<string>("SELECT pg_catalog.split_part(p.prosrc, E'\\n', 2) FROM pg_catalog.pg_proc p WHERE p.proname = 'documents_as_member'")).Single();
        first.Should().StartWith("-- Membership of documents, in form ");
        await filing.ExecuteAsync(
            $"""
            CREATE OR REPLACE FUNCTION filing.documents_as_member() RETURNS SETOF uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $body$
            {first}
            SELECT d."Id" FROM filing."Documents" d
            $body$;
            """);

        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_function_that_others_may_execute_than_the_roles_its_rules_name_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // Given to every role there is, by hand: a role the rules never named could ask a function that runs as its owner.
        await filing.ExecuteAsync("GRANT EXECUTE ON FUNCTION filing.documents_i_see() TO PUBLIC");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("membership of Document").And.Contain("say them: filing.documents_i_see (every role may execute it). ");

        // Given to one more role than the rules name: a caller that did not sign in could ask what the rules keep to those who did.
        await filing.ExecuteAsync(
            """
            REVOKE EXECUTE ON FUNCTION filing.documents_i_see() FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION filing.documents_i_see() TO anon;
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("membership of Document").And.Contain("say them: filing.documents_i_see (anon may execute it, which the rules do not name). ");

        // Taken back from a role the folder's rules name: every policy that asks the function as that role would fail.
        await filing.ExecuteAsync(
            $"""
            REVOKE EXECUTE ON FUNCTION filing.documents_i_see() FROM anon;
            REVOKE EXECUTE ON FUNCTION filing.folder_ids_held(text) FROM {FilingPostgres.ArchivistRole};
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("membership of Folder").And.Contain($"say them: filing.folder_ids_held ({FilingPostgres.ArchivistRole} may not execute it). ");

        // From the signed-in user's role on a document's, and from both roles a folder's rules name.
        await filing.ExecuteAsync(
            """
            REVOKE EXECUTE ON FUNCTION filing.folder_ids_held(text) FROM authenticated;
            REVOKE EXECUTE ON FUNCTION filing.documents_as_member_with(text) FROM authenticated;
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("say them: filing.documents_as_member_with (authenticated may not execute it). ");
        await filing.ExecuteAsync("GRANT EXECUTE ON FUNCTION filing.documents_as_member_with(text) TO authenticated");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain($"say them: filing.folder_ids_held (authenticated, {FilingPostgres.ArchivistRole} may not execute it). ");

        // The access file gives the grants back, as it gives everything else.
        await filing.ExecuteAsync(FilingPostgres.AccessScript());
        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_function_one_more_role_may_execute_than_the_rules_name_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);

        // The folder's functions are for signed-in users and the archivists. Given by hand to the role scoped
        // system work runs as, and to callers that did not sign in: both are named, and neither is PUBLIC.
        await filing.ExecuteAsync("GRANT EXECUTE ON FUNCTION filing.folder_ids_held(text) TO ddd_system_in, anon");

        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .StartWith("The functions that answer the membership of Folder in the database are not as its rules, 'folders', say them: ")
            .And.Contain("filing.folder_ids_held (anon, ddd_system_in may execute it, which the rules do not name). ");

        // What the document's rules do not name, the folder's do: the archivists ask a folder's functions, and no document's.
        await filing.ExecuteAsync(
            $"""
            REVOKE EXECUTE ON FUNCTION filing.folder_ids_held(text) FROM ddd_system_in, anon;
            GRANT EXECUTE ON FUNCTION filing.documents_where_i_hold(text) TO {FilingPostgres.ArchivistRole};
            """);
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain($"say them: filing.documents_where_i_hold ({FilingPostgres.ArchivistRole} may execute it, which the rules do not name). ");

        // The access file takes every such grant back.
        await filing.ExecuteAsync(FilingPostgres.AccessScript());
        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_database_whose_lock_was_taken_off_by_hand_is_refused()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false);
        await CheckAsync(filing);

        // One policy of the lock dropped: signed-in users are held for adding and changing a share, and no longer for removing one.
        await filing.ExecuteAsync("DROP POLICY \"Members change with the key (delete) for authenticated\" ON filing.\"DocumentShares\"");
        var refused = await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>();
        refused.Which.Message.Should()
            .StartWith("The rules 'documents' of Document name the key a caller needs to change its members or its owner, and the database does not hold callers to it: ")
            .And.Contain("filing.\"DocumentShares\" (authenticated is not held to the key that changes the members). ")
            .And.Contain("Apply the access files the export writes");

        // Row security turned off on the roles' table: its policies are there, and hold nobody.
        await filing.ExecuteAsync(FilingPostgres.AccessScript());
        await CheckAsync(filing);
        await filing.ExecuteAsync($"ALTER TABLE filing.\"{FilingContext.FolderStaffRolesTable}\" DISABLE ROW LEVEL SECURITY");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The rules 'folders' of Folder name the key").And.Contain(
                $"filing.\"{FilingContext.FolderStaffRolesTable}\" (authenticated, {FilingPostgres.ArchivistRole} is not held to the key that changes the members). ");

        // The trigger on the owner column disabled, and then dropped.
        await filing.ExecuteAsync(FilingPostgres.AccessScript());
        await CheckAsync(filing);
        await filing.ExecuteAsync("ALTER TABLE filing.\"Documents\" DISABLE TRIGGER documents_owner_stays");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("hold callers to it: filing.\"Documents\" (its owner column is not held to the key that changes the owner). ");
        await filing.ExecuteAsync("ALTER TABLE filing.\"Documents\" ENABLE TRIGGER documents_owner_stays; DROP TRIGGER folders_owner_stays ON filing.\"Folders\"");
        (await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("hold callers to it: filing.\"Folders\" (its owner column is not held to the key that changes the owner). ");

        await filing.ExecuteAsync(FilingPostgres.AccessScript());
        await CheckAsync(filing);
    }

    [Fact]
    public async Task A_database_that_lets_a_caller_give_a_role_it_does_not_see_or_archive_the_owners_role_is_refused()
    {
        using var garden = await PostgresGarden.CreateAsync(postgres, seed: false);
        Task<IReadOnlyList<string>> CheckGardenAsync() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation);
        await CheckGardenAsync();

        // The plots keep their roles and name the key that changes the gardeners: the policy that holds a role
        // given to one the caller sees, dropped for adding a row, is a lock that is not in place.
        await garden.ExecuteAsync("DROP POLICY \"Members hold roles the caller sees (insert) for authenticated\" ON gardens.\"PlotGardenerRoles\"");
        (await FluentActions.Awaiting(CheckGardenAsync).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .StartWith("The rules 'plots' of Plot name the key a caller needs to change its members or its owner, and the database does not hold callers to it: ")
            .And.Contain("gardens.\"PlotGardenerRoles\" (authenticated is not held to the roles it sees). ");

        // The same, dropped for changing a row: a hold moved onto a role the caller does not see.
        await garden.ExecuteAsync(PostgresGarden.AccessScript());
        await CheckGardenAsync();
        await garden.ExecuteAsync("DROP POLICY \"Members hold roles the caller sees (update) for authenticated\" ON gardens.\"PlotGardenerRoles\"");
        (await FluentActions.Awaiting(CheckGardenAsync).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .StartWith("The rules 'plots' of Plot name the key a caller needs to change its members or its owner, and the database does not hold callers to it: ")
            .And.Contain("gardens.\"PlotGardenerRoles\" (authenticated is not held to the roles it sees). ");

        // The trigger that keeps the owner's role in use, disabled: whoever may change a role could archive it.
        await garden.ExecuteAsync(PostgresGarden.AccessScript());
        await CheckGardenAsync();
        await garden.ExecuteAsync("ALTER TABLE gardens.\"PlotRoles\" DISABLE TRIGGER plots_owner_role_stays");
        (await FluentActions.Awaiting(CheckGardenAsync).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be(
                "The roles of Plot are kept in gardens.\"PlotRoles\", and the database lets the owner's role, the one made from 'owner', be archived: the trigger that keeps it in use, "
                + "plots_owner_role_stays, is missing, disabled, or written from other rules. Nobody could be made an owner after. Apply the access files the export writes with the "
                + "rules the resource is registered with: the trigger comes with the functions.");

        // Dropped, for the sheds; and its function rewritten by hand, so it names no rules.
        await garden.ExecuteAsync("ALTER TABLE gardens.\"PlotRoles\" ENABLE TRIGGER plots_owner_role_stays; DROP TRIGGER sheds_owner_role_stays ON gardens.\"ShedRoles\"");
        (await FluentActions.Awaiting(CheckGardenAsync).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The roles of Shed are kept in gardens.\"ShedRoles\", and the database lets the owner's role, the one made from 'keeper', be archived");
        await garden.ExecuteAsync(PostgresGarden.AccessScript());
        await garden.ExecuteAsync("CREATE OR REPLACE FUNCTION gardens.plots_owner_role_stays() RETURNS trigger LANGUAGE plpgsql SET search_path = '' AS $body$ BEGIN RETURN NEW; END $body$");
        (await FluentActions.Awaiting(CheckGardenAsync).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The roles of Plot are kept in gardens.\"PlotRoles\"");

        // The access file puts all of it back.
        await garden.ExecuteAsync(PostgresGarden.AccessScript());
        await CheckGardenAsync();
    }

    [Fact]
    public async Task Rules_that_name_no_key_for_the_lock_are_said_loudly_and_refused_by_nobody()
    {
        // The plots name both keys. The sheds name neither: whoever the host's rules let change a shed writes
        // its hands and its owner. That is the host's to choose, so the check starts, and says it.
        var logged = new Logged();
        using var garden = await PostgresGarden.CreateAsync(postgres, seed: false, configure: services => services.AddLogging(logging => logging.AddProvider(logged)));

        var remarks = await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation);

        remarks.Should().ContainSingle().Which.Should()
            .StartWith("The rules 'sheds' of Shed name no key that changes its members and none that changes its owner (changeMembersKey, changeOwnerKey), so the database leaves its member rows and its owner column to whoever the application's row access rules let change a Shed")
            .And.Contain("without a command of the application's having asked anything")
            .And.Contain("Name the keys the application's commands require for it in the rules");
        logged.Lines.Should().Equal([(LogLevel.Warning, "DDDToolkit.Supporting.Membership.Postgres", remarks[0])], "what is remarked on is logged as a warning, where the host has a logger");

        // Rules that name one of the two are told about the other.
        static MembershipRules Sheds(string? membersKey, string? ownerKey)
            => new("sheds", keys: [.. ShedMembership.Rules.Keys], roles: [.. ShedMembership.Rules.Roles], ownerRole: ShedMembership.Rules.OwnerRole,
                memberKeys: ShedMembership.Rules.MemberKeys, codes: ShedMembership.Codes, rolesKept: true, changeMembersKey: membersKey, changeOwnerKey: ownerKey);

        async Task<IReadOnlyList<string>> RemarksForAsync(MembershipRules sheds)
        {
            using var host = await PostgresGarden.CreateAsync(postgres, secured: false, seed: false, sheds: sheds);
            await host.ExecuteAsync(PostgresGarden.AccessScript([new PlotMembershipFunctions(), new MembershipRowAccessContribution<ShedHand>(sheds), new GardenOwnFunctions()]));
            return await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(host.Services.Provider, Cancellation);
        }

        (await RemarksForAsync(Sheds(ShedKeys.Stock, null))).Should().ContainSingle().Which.Should()
            .StartWith("The rules 'sheds' of Shed name no key that changes its owner (changeOwnerKey), so the database leaves its owner column, and with it every key an owner holds, to whoever");
        (await RemarksForAsync(Sheds(null, ShedKeys.Sell))).Should().ContainSingle().Which.Should()
            .StartWith("The rules 'sheds' of Shed name no key that changes its members (changeMembersKey), so the database leaves its member rows to whoever");
        (await RemarksForAsync(Sheds(ShedKeys.Stock, ShedKeys.Sell))).Should().BeEmpty();
    }

    [Fact]
    public async Task A_role_table_callers_may_write_unguarded_is_refused()
    {
        // The host's access file without its rules for the role classes: the tables of the roles have no row
        // security, and every caller's role may write them.
        using var garden = await PostgresGarden.CreateAsync(postgres, secured: false, seed: false);
        await garden.ExecuteAsync(PostgresGarden.AccessScript(rules: [PostgresGarden.Rules[0]]));

        var refused = await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>();
        refused.Which.Message.Should()
            .StartWith("The roles of Plot are kept in gardens.PlotRoles, which has no row security, and authenticated may write it. ")
            .And.Contain("A role's row says what everybody who holds the role may do")
            .And.Contain("Give PlotRole row access rules of its own, [RowAccess<PlotRole>]");

        // The privileges taken from the roles the rules name is the other way: nobody of them writes the table, guarded or not.
        await garden.ExecuteAsync("REVOKE INSERT, UPDATE, DELETE ON gardens.\"PlotRoles\", gardens.\"ShedRoles\" FROM authenticated");
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation);

        // A privilege on one column writes a role's keys as well.
        await garden.ExecuteAsync("GRANT UPDATE (\"Keys\") ON gardens.\"ShedRoles\" TO authenticated");
        (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().StartWith("The roles of Shed are kept in gardens.ShedRoles, which has no row security, and authenticated may write it. ");

        // With the host's rules for both role classes, the tables check their rows, and it starts.
        await garden.ExecuteAsync("GRANT INSERT, UPDATE, DELETE ON gardens.\"PlotRoles\", gardens.\"ShedRoles\" TO authenticated");
        await garden.ExecuteAsync(PostgresGarden.AccessScript());
        await MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(garden.Services.Provider, Cancellation);
    }

    [Fact]
    public async Task Rules_that_name_a_role_the_host_runs_no_caller_as_are_said()
    {
        // The folder's rules let a token role ask its functions, and this host mapped that token role to no database role.
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Secured, seed: false, configure: services => services.AddPostgresRowLevelSecurity());

        var refused = await FluentActions.Awaiting(() => CheckAsync(filing)).Should().ThrowAsync<InvalidOperationException>();

        refused.Which.Message.Should()
            .StartWith("The rules 'folders' of Folder let the role '@token:archivist' ask its functions, and the host runs no caller as it: ")
            .And.Contain("PostgresRowLevelSecurityOptions.TokenRoles");
    }

    [Fact]
    public async Task What_is_not_registered_or_not_mapped_is_said()
    {
        // Nothing registered: there is nothing to hold the database to, and the host is told so rather than passed.
        await using (var empty = new ServiceCollection().BuildServiceProvider())
        {
            (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(empty, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().StartWith("No resource with members is registered");
        }

        // Registered, and its context is not one the services make.
        var services = new ServiceCollection();
        FilingHost.Add(services);
        await using (var withoutContext = services.BuildServiceProvider())
        {
            (await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(withoutContext, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().StartWith("FilingContext, which keeps the members of Document, is not a context the services make");
        }

        await FluentActions.Awaiting(() => MembershipPostgresChecks.EnsureFunctionsAreInPlaceAsync(null!, Cancellation)).Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>A logger provider that keeps what is logged: the level, the category and the text.</summary>
    private sealed class Logged : ILoggerProvider
    {
        public List<(LogLevel Level, string Category, string Text)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Category(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Category(Logged owner, string name) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Only what the package's own check says: Entity Framework logs under categories of its own.
                if (name.StartsWith("DDDToolkit.Supporting.Membership", StringComparison.Ordinal))
                {
                    lock (owner.Lines)
                    {
                        owner.Lines.Add((logLevel, name, formatter(state, exception)));
                    }
                }
            }
        }
    }
}
