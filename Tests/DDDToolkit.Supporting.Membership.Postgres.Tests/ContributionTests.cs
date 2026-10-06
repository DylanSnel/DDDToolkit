using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Npgsql;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// What the contribution of one resource writes: four functions under the host's names, from the resource's
/// rules and the names its model gives its tables, with the grants the rules name; and the lock, for rules
/// that name the keys that change the members and the owner. No policy that allows anything, no table kept to
/// itself, and nothing for a context that is another resource's.
/// </summary>
public sealed class ContributionTests(FilingPostgres postgres)
{
    /// <summary>What the functions are written with: the roles as the host maps them, the folder's token role among them.</summary>
    private static readonly RowAccessExport Export = new() { Roles = FilingPostgres.RoleNames };

    [Fact]
    public void The_contribution_writes_four_functions_under_the_hosts_names_and_allows_nothing()
    {
        using var context = FilingPostgres.Model();

        var written = new FolderMembershipFunctions().Contribute(context, Export)!;

        written.Policies.Should().OnlyContain(policy => policy.Restrictive, "what a caller may read and change of a folder is the application's own rules to say: the package only narrows");
        written.ExclusiveTables.Should().BeNull("the member tables follow the rules of the resource that owns them");
        written.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("folder_ids_staffed", "", "SETOF bigint"),
            ("folder_ids_staffed_with", "text", "SETOF bigint"),
            ("folder_ids_seen", "", "SETOF bigint"),
            ("folder_ids_held", "text", "SETOF bigint"));
        written.Functions.Should().OnlyContain(function => function.SecurityDefiner && !function.Inlinable && function.Volatility == "STABLE");
        written.Functions.Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User, RowAccessRoles.Token(FilingPostgres.ArchivistTokenRole) }), "who may ask is what the rules say");

        // A document's are its own: other names, another id, and signed-in users alone.
        var documents = new DocumentMembershipFunctions().Contribute(context, Export)!;
        documents.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("documents_as_member", "", "SETOF uuid"),
            ("documents_as_member_with", "text", "SETOF uuid"),
            ("documents_i_see", "", "SETOF uuid"),
            ("documents_where_i_hold", "text", "SETOF uuid"));
        documents.Functions.Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User }));
    }

    [Fact]
    public void Rules_that_name_the_key_that_changes_the_members_lock_both_member_tables_for_the_roles_they_name()
    {
        using var context = FilingPostgres.Model();
        var staff = context.Model.FindEntityType(typeof(FolderMember))!;
        var held = MembershipModel.Of(context.Model, typeof(FolderMember))!.Roles;

        var written = new FolderMembershipFunctions().Contribute(context, Export)!.Policies;

        // Adding, changing and removing a row, on the staff's table and on the table of the roles they hold,
        // for each role the folder's rules let ask its functions: never reading, which stays what the folder's
        // own rules say.
        written.Select(policy => (policy.Table, policy.Command, policy.Role)).Should().BeEquivalentTo(
            from table in new[] { staff, held }
            from role in new[] { RowAccessRoles.User, RowAccessRoles.Token(FilingPostgres.ArchivistTokenRole) }
            from command in new[] { "INSERT", "UPDATE", "DELETE" }
            select (table, command, role));
        written.Should().OnlyContain(policy => policy.Restrictive && policy.Name == "Members change with the key");

        // A row is written only on a folder the caller holds the key on, as the folder's own function answers
        // it: the key that changes the staff, or the key that hands the folder on, since naming a keeper
        // writes the new keeper's rows.
        const string Holds =
            "(\"FolderId\" IN (SELECT held.id FROM {fn:folders/folder_ids_held}('folders.staff') AS held(id))"
            + " OR \"FolderId\" IN (SELECT held.id FROM {fn:folders/folder_ids_held}('folders.hand-over') AS held(id)))";
        written.Where(policy => policy.Command == "INSERT").Should().OnlyContain(policy => policy.Using == null && policy.WithCheck == Holds);
        written.Where(policy => policy.Command == "UPDATE").Should().OnlyContain(policy => policy.Using == Holds && policy.WithCheck == Holds);
        written.Where(policy => policy.Command == "DELETE").Should().OnlyContain(policy => policy.Using == Holds && policy.WithCheck == null);

        // A document's, for signed-in users alone, under its own names, asked once where one key does both;
        // and nothing for rules that name no such key.
        var documents = new DocumentMembershipFunctions().Contribute(context, Export)!.Policies;
        documents.Should().HaveCount(6).And.OnlyContain(policy => policy.Role == RowAccessRoles.User && policy.Restrictive);
        documents.Should().Contain(policy => policy.Table.ClrType == typeof(DocumentShare) && policy.Command == "INSERT"
            && policy.WithCheck == "\"DocumentId\" IN (SELECT held.id FROM {fn:documents/documents_where_i_hold}('documents.share') AS held(id))");

        using var gardens = PostgresGarden.Model();
        new ShedMembershipFunctions().Contribute(gardens, Export)!.Policies.Should().BeEmpty("a shed's rules name no key that changes its hands");
    }

    [Fact]
    public void Rules_that_name_the_key_that_changes_the_owner_pin_the_owner_column_for_the_roles_they_name()
    {
        using var context = FilingPostgres.Model();

        var written = new FolderMembershipFunctions().Contribute(context, Export)!.Statements;

        // A trigger, since only a trigger sees the row a statement found and the row it leaves: it fires where
        // the keeper would change, and refuses as a policy refuses unless the caller held the key before.
        written.Should().HaveCount(4);
        written[0].Should()
            .StartWith("CREATE OR REPLACE FUNCTION \"filing\".folders_owner_stays() RETURNS trigger\n    LANGUAGE plpgsql SET search_path = '' AS $body$\nBEGIN\n    -- Membership of folders, in form ")
            .And.Contain($"IF CURRENT_USER IN ('authenticated', '{FilingPostgres.ArchivistRole}')")
            .And.Contain("AND NOT EXISTS (SELECT 1 FROM {fn:folders/folder_ids_held}('folders.hand-over') AS held(id) WHERE held.id = OLD.\"Id\") THEN")
            .And.Contain("RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'folders_owner_stays', HINT = 'ddd:access.refused', MESSAGE = ")
            .And.NotContain("SECURITY DEFINER", "it asks as the caller, who may ask the folder's functions")
            .And.NotContain("\r");
        written[1].Should().Be("REVOKE ALL ON FUNCTION \"filing\".folders_owner_stays() FROM PUBLIC");
        written[2].Should().Be("DROP TRIGGER IF EXISTS folders_owner_stays ON \"filing\".\"Folders\"");
        written[3].Should().Be(
            "CREATE TRIGGER folders_owner_stays BEFORE UPDATE OF \"Keeper\" ON \"filing\".\"Folders\"\n"
            + "    FOR EACH ROW WHEN (OLD.\"Keeper\" IS DISTINCT FROM NEW.\"Keeper\")\n"
            + "    EXECUTE FUNCTION \"filing\".folders_owner_stays()");

        // Rules that name no such key take a lock away that earlier rules may have left behind, and write none.
        // What follows, for the sheds, is the trigger that keeps the owner's role in use: their roles are kept.
        using var gardens = PostgresGarden.Model();
        var sheds = new ShedMembershipFunctions().Contribute(gardens, Export)!.Statements;
        sheds.Take(2).Should().Equal(
            "DROP TRIGGER IF EXISTS sheds_owner_stays ON \"gardens\".\"Sheds\"",
            "DROP FUNCTION IF EXISTS \"gardens\".sheds_owner_stays()");
        sheds.Skip(2).Should().HaveCount(4).And.OnlyContain(
            statement => statement.Contains("sheds_owner_role_stays", StringComparison.Ordinal),
            "and the rest is the trigger that keeps the owner's role in use, with its function: no lock on the owner column is written");

        // A role the host maps to no database role is nobody a lock can be written for: said, naming the rules.
        FluentActions.Invoking(() => new FolderMembershipFunctions().Contribute(context, new RowAccessExport()))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'folders' let the role '@token:archivist' ask the resource's functions, and the lock on the owner column cannot be written for it*");

        // And a name Postgres would cut is refused where the lock is written: it is named after the rules,
        // whatever short names the rules give their functions.
        var longName = new MembershipRules(
            new string('d', 60), keys: ["d.read"], roles: [new("reader", ["d.read"])], functions: new("d_on", "d_with", "d_seen", "d_held"), changeOwnerKey: "d.read");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(longName).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*longer than the 63 characters Postgres keeps of a name*");

        // The same rules without that key ask for no lock, so their name is nobody's trouble: the functions
        // are written, and there is no lock of that name to take away.
        var longAndOpen = new MembershipRules(new string('d', 60), keys: ["d.read"], roles: [new("reader", ["d.read"])], functions: new("d_on", "d_with", "d_seen", "d_held"));
        var open = new MembershipRowAccessContribution<DocumentShare>(longAndOpen).Contribute(context, Export)!;
        open.Functions.Should().HaveCount(4);
        open.Statements.Should().BeEmpty();

        // The function the trigger runs is written between two quotes of its own. A key that has that quote in
        // it would end the body early, and what came after it would be read as the statement's own: refused
        // where the lock is written, as the export refuses a function's body with its quote in it.
        var quoted = new MembershipRules(
            "documents", keys: ["d.read", "d.give $body$; DROP TABLE x; --"], roles: [new("reader", ["d.read"])], changeOwnerKey: "d.give $body$; DROP TABLE x; --");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(quoted).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The lock on the owner column of Document cannot be written from the rules 'documents'*has '$body$' in it, which would end the function's body early.");
    }

    [Fact]
    public void Where_the_roles_are_kept_the_lock_holds_a_role_given_to_one_the_caller_sees_and_a_trigger_keeps_the_owners_role_in_use()
    {
        using var gardens = PostgresGarden.Model();
        var held = MembershipModel.Of(gardens.Model, typeof(PlotGardener))!.Roles.GetTableName();
        var plots = new PlotMembershipFunctions().Contribute(gardens, Export)!;

        // The plots' rules name the key that changes the gardeners. On the table of the roles a gardener holds a
        // second restrictive policy, for adding and changing a row, for each role the rules let ask the
        // functions: the role is one the caller sees in the role table, asked as the caller. Removing a row asks
        // nothing of its role.
        const string Seen = "EXISTS (SELECT 1 FROM \"gardens\".\"PlotRoles\" k WHERE k.\"Id\" = \"gardens\".\"PlotGardenerRoles\".\"RoleId\")";
        plots.Policies.Where(policy => policy.Name == "Members hold roles the caller sees")
            .Select(policy => (policy.Table.GetTableName(), policy.Command, policy.Role, policy.Using, policy.WithCheck, policy.Restrictive))
            .Should().BeEquivalentTo(new (string?, string, string, string?, string?, bool)[]
            {
                (held, "INSERT", RowAccessRoles.User, null, Seen, true),
                (held, "UPDATE", RowAccessRoles.User, "true", Seen, true),
            });

        // It is part of the lock: the sheds keep their roles and name no key that changes their hands, so they
        // get neither. And the documents declare their roles, which are names and no rows.
        new ShedMembershipFunctions().Contribute(gardens, Export)!.Policies.Should().BeEmpty();
        using var filing = FilingPostgres.Model();
        new DocumentMembershipFunctions().Contribute(filing, Export)!.Policies.Should().NotContain(policy => policy.Name == "Members hold roles the caller sees");

        // The owner's role: a trigger on the role table, after the lock on the owner column, written from the
        // starter role the rules name as the owner's, which refuses a row of it that would be archived.
        var trigger = plots.Statements.Skip(4).ToList();
        trigger.Should().HaveCount(4);
        trigger[0].Should()
            .StartWith("CREATE OR REPLACE FUNCTION \"gardens\".plots_owner_role_stays() RETURNS trigger\n    LANGUAGE plpgsql SET search_path = '' AS $body$\nBEGIN\n    -- Membership of plots, in form ")
            .And.Contain("\n    IF NEW.\"MadeFrom\" = 'owner' AND NEW.\"Status\" IS DISTINCT FROM 'Active' THEN\n")
            .And.Contain("RAISE EXCEPTION USING ERRCODE = 'check_violation'")
            .And.NotContain("CURRENT_USER", "whoever writes the row is held, as the role refuses in C# whoever asks")
            .And.NotContain("SECURITY DEFINER")
            .And.NotContain("\r");
        trigger[1].Should().Be("REVOKE ALL ON FUNCTION \"gardens\".plots_owner_role_stays() FROM PUBLIC");
        trigger[2].Should().Be("DROP TRIGGER IF EXISTS plots_owner_role_stays ON \"gardens\".\"PlotRoles\"");
        trigger[3].Should().Be(
            "CREATE TRIGGER plots_owner_role_stays BEFORE INSERT OR UPDATE OF \"MadeFrom\", \"Status\" ON \"gardens\".\"PlotRoles\"\n"
            + "    FOR EACH ROW EXECUTE FUNCTION \"gardens\".plots_owner_role_stays()");

        // The sheds name their keeper as the owner's role, on a role table of their own.
        new ShedMembershipFunctions().Contribute(gardens, Export)!.Statements.Should()
            .Contain(statement => statement.StartsWith("CREATE OR REPLACE FUNCTION \"gardens\".sheds_owner_role_stays()", StringComparison.Ordinal)
                && statement.Contains("IF NEW.\"MadeFrom\" = 'keeper' AND", StringComparison.Ordinal))
            .And.Contain("DROP TRIGGER IF EXISTS sheds_owner_role_stays ON \"gardens\".\"ShedRoles\"");

        // Rules that declare their roles keep no role table, and get no such trigger.
        new DocumentMembershipFunctions().Contribute(filing, Export)!.Statements.Should().NotContain(statement => statement.Contains("owner_role_stays"));

        // A name Postgres would cut is refused, as the lock's is: rules that keep their roles have this trigger whatever else they say.
        var longName = new MembershipRules(
            new string('p', 50), keys: [PlotKeys.See], roles: [new("tender", [PlotKeys.See])], memberKeys: MemberKeys.Only(PlotKeys.See), rolesKept: true,
            functions: new("p_on", "p_with", "p_seen", "p_held"));
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PlotGardener>(longName).Contribute(gardens, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules '" + new string('p', 50) + "' keep the roles of the resource, and the trigger that keeps the owner's role in use would be called*longer than the 63 characters Postgres keeps of a name*");
    }

    [Fact]
    public void The_applications_own_work_in_a_scope_the_rules_name_is_answered_every_resource()
    {
        using var context = FilingPostgres.Model();

        // A folder's rules name the module's scope: seen and held on answer every folder for the role scoped
        // work runs as, when its claims say that scope. As member and as member with do not: such work is nobody's member.
        var folders = new FolderMembershipFunctions().Contribute(context, Export)!.Functions;
        const string OwnWork = "UNION\nSELECT r.\"Id\" FROM \"filing\".\"Folders\" r WHERE {caller:role} = 'ddd_system_in' AND {caller:claim:scope} IN ('filing')";
        folders[2].Body.Should().EndWith(OwnWork);
        folders[3].Body.Should().EndWith(OwnWork + " AND $1 IS NOT NULL");
        folders[0].Body.Should().NotContain("scope");
        folders[1].Body.Should().NotContain("scope");

        // The role is the one the host runs scoped work as, and several scopes are one list.
        var renamed = new RowAccessExport { Roles = FilingPostgres.RoleNames with { SystemIn = "filing_worker" } };
        var several = new MembershipRules("documents", keys: [DocumentKeys.View], roles: [new("reader", [DocumentKeys.View])], systemScopes: ["filing", "night-shift"]);
        new MembershipRowAccessContribution<DocumentShare>(several).Contribute(context, renamed)!.Functions[2].Body
            .Should().EndWith("WHERE {caller:role} = 'filing_worker' AND {caller:claim:scope} IN ('filing', 'night-shift')");

        // A document's rules name no scope: nothing of the kind is written, and scoped work is answered nothing.
        new DocumentMembershipFunctions().Contribute(context, Export)!.Functions.Should().OnlyContain(function => !function.Body.Contains("scope"));
    }

    [Fact]
    public void A_function_that_takes_a_key_asks_nothing_without_one()
    {
        using var depot = PostgresDepot.Model();

        var crates = new CrateMembershipFunctions().Contribute(depot, Export)!.Functions;

        // A condition without a column gates the whole part it is in: with no key, the member rows are not
        // read and the depot's functions are not asked.
        crates[1].Body.Should().Contain("\nWHERE $1 IS NOT NULL AND m.\"MemberId\" = ");
        crates[3].Body.Should().Contain("r WHERE $1 IS NOT NULL AND r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}($1) AS reached(place))");

        // Asked with a key the rules name, the depot's function is asked with that key: there is nothing to gate.
        crates[2].Body.Should().Contain("r WHERE r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}('crates.see') AS reached(place))");
    }

    [Fact]
    public void The_functions_are_owned_by_the_rules_name_which_is_what_a_rule_asks_them_by()
    {
        new DocumentMembershipFunctions().Owner.Should().Be("documents");
        new FolderMembershipFunctions().Owner.Should().Be("folders");
        new MembershipRowAccessContribution<DocumentShare>(Rules("sales.orders")).Owner.Should().Be("sales-orders", "an owner is letters, digits and dashes");

        // The application's rules get the rules' own names: the documents' rules ask by the document's id, and the
        // folders' through questions that name that owner and those names.
        var script = FilingPostgres.AccessScript();
        script.Should().Contain("USING (\"Id\" = ANY (ARRAY(SELECT filing.documents_i_see())))")
            .And.Contain("USING (\"Id\" = ANY (ARRAY(SELECT filing.folder_ids_seen())))");
        (DocumentMembership.Rules.Functions.Seen, DocumentMembership.Rules.Functions.HeldOn).Should().Be(("documents_i_see", "documents_where_i_hold"));
        (FolderMembership.Rules.Functions.Seen, FolderMembership.Rules.Functions.HeldOn).Should().Be(("folder_ids_seen", "folder_ids_held"));
    }

    [Fact]
    public void The_two_functions_that_answer_access_say_which_set_they_answer_for_the_resources_id()
    {
        using var context = FilingPostgres.Model();

        var documents = new DocumentMembershipFunctions().Contribute(context, Export)!.Functions;
        var folders = new FolderMembershipFunctions().Contribute(context, Export)!.Functions;

        documents.Select(function => (function.Name, function.Answers)).Should().Equal(
            ("documents_as_member", null),
            ("documents_as_member_with", null),
            ("documents_i_see", new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.Seen)),
            ("documents_where_i_hold", new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.HeldOn)));
        folders.Select(function => (function.Name, function.Answers)).Should().Equal(
            [
                ("folder_ids_staffed", null),
                ("folder_ids_staffed_with", null),
                ("folder_ids_seen", new ResourceAccessAnswer(typeof(FolderId), ResourceAccessSet.Seen)),
                ("folder_ids_held", new ResourceAccessAnswer(typeof(FolderId), ResourceAccessSet.HeldOn)),
            ],
            "the names are the rules' own, and a rule that asks by the folder's id gets them all the same");
    }

    [Fact]
    public void A_rule_that_asks_by_the_resources_id_is_written_as_one_that_names_the_function()
    {
        var seen = ResourceAccessAnswer.NameOf(typeof(DocumentId), ResourceAccessSet.Seen);
        DDDToolkit.Supporting.Membership.TestHost.Access.UsersReadTheDocumentsTheySee.RowAccessSql.Should().Be($"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{seen}}}())))");
        DDDToolkit.Supporting.Membership.TestHost.Access.UsersChangeTheDocumentsTheyWorkOn.RowAccessSql.Should().Contain("{fn:@DDDToolkit.Supporting.Membership.TestHost.DocumentId/held_on}('documents.edit')");

        // The same rules, naming the functions as the documents' rules call them: the file is the same, byte for byte.
        RowAccessRule Named(RowAccessRule rule) => RowAccessRule.For<Document>(
            rule.Name,
            rule.Operations,
            rule.Sql.Replace(seen, "documents/documents_i_see", StringComparison.Ordinal)
                .Replace(ResourceAccessAnswer.NameOf(typeof(DocumentId), ResourceAccessSet.HeldOn), "documents/documents_where_i_hold", StringComparison.Ordinal),
            [.. rule.Roles]);

        var byName = FilingPostgres.Rules.Select(rule => rule.AggregateTypeName == typeof(Document).FullName ? Named(rule) : rule).ToList();
        byName.Should().NotBeEquivalentTo(FilingPostgres.Rules, "the documents' rules ask by the document's id");
        FilingPostgres.AccessScript(rules: byName).Should().Be(FilingPostgres.AccessScript());
    }

    [Fact]
    public void A_rule_that_asks_a_resource_whose_membership_is_not_used_is_refused_when_the_file_is_written()
    {
        // The folders' contribution alone: nothing answers for the documents, whose rule asks by the document's id.
        var read = FilingPostgres.Rules.Single(rule => rule.Name == "Users read the documents they see");
        var refused = () => FilingPostgres.AccessScript(contributions: [new FolderMembershipFunctions()], rules: [read]);

        refused.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Users read the documents they see' asks the resources the caller sees, by the id DDDToolkit.Supporting.Membership.TestHost.DocumentId, "
            + "and no row access contribution answers it for the contexts this is written with. Use the contribution that keeps that resource's access, the Membership package's for a resource with members*");
    }

    [Fact]
    public void Who_the_caller_is_as_a_member_is_what_the_rules_say()
    {
        using var context = FilingPostgres.Model();

        var documents = new DocumentMembershipFunctions().Contribute(context, Export)!.Functions;
        var folders = new FolderMembershipFunctions().Contribute(context, Export)!.Functions;

        // The caller's user id for a document, and the claim the folder's rules name for a folder: places the script fills in.
        documents[0].Body.Should().Contain("m.\"MemberId\" = {caller:uid}").And.NotContain("claim");
        folders[0].Body.Should().Contain("m.\"StaffCode\" = {caller:claim:app_metadata.staff}").And.NotContain("{caller:uid}");

        // An owner holds every key of the resource, and none but those, by matching the owner's column, never a role.
        documents[3].Body.Should().Contain("$1 IN ('documents.view', 'documents.edit', 'documents.share') AND r.\"OwnerId\" = {caller:uid}");
        folders[3].Body.Should().Contain(
            "$1 IN ('folders.read', 'folders.file', 'folders.staff', 'folders.shred', 'folders.hand-over') AND r.\"Keeper\" = {caller:claim:app_metadata.staff}");

        // Being a member gives a document's key that sees; a folder's rules name no such key.
        documents[3].Body.Should().Contain("WHERE $1 = 'documents.view'");
        folders[3].Body.Should().NotContain("folder_ids_staffed}()");

        // Which role gives which key: only the keys a member's role can give. Shredding is listed by two roles of a folder and given by none.
        folders[1].Body.Should().Contain("('keeper', 'folders.staff')").And.Contain("('clerk', 'folders.file')").And.NotContain(FolderKeys.Shred);
    }

    [Fact]
    public void A_functions_body_is_the_same_text_whichever_machine_writes_it()
    {
        using var context = FilingPostgres.Model();

        foreach (var contribution in FilingPostgres.Contributions)
        {
            var functions = contribution.Contribute(context, Export)!.Functions;

            // Line feeds alone, whatever line endings the package's own source was checked out with: an access
            // file written on one machine is the file written on another.
            functions.Should().OnlyContain(function => !function.Body.Contains('\r'));

            // And every body says, in its first line, which rules it was written from: what the start-up check reads.
            functions.Select(function => function.Body.Split('\n')[0]).Distinct().Should().ContainSingle()
                .Which.Should().MatchRegex("^-- Membership of (documents|folders), in form [0-9]+, written from the rules [0-9a-f]{64}$");
            functions.Should().OnlyContain(function => function.Body.Split('\n').Length > 1);
        }
    }

    [Fact]
    public void A_context_that_does_not_map_the_resource_is_none_of_its_business()
    {
        using var other = new OtherContext(new DbContextOptionsBuilder<OtherContext>().UseNpgsql("Host=model-only").Options);

        new DocumentMembershipFunctions().Contribute(other, Export).Should().BeNull();
        new FolderMembershipFunctions().Contribute(other, Export).Should().BeNull();
    }

    [Fact]
    public void A_resource_known_by_more_than_its_id_is_answered_by_its_id_and_its_rows_joined_on_its_whole_key()
    {
        using var context = new TwoPartKeyContext(new DbContextOptionsBuilder<TwoPartKeyContext>().UseNpgsql("Host=model-only").Options);

        var functions = new DocumentMembershipFunctions().Contribute(context, Export)!.Functions;

        // The functions answer the resource's id, as the questions that ask them do, and a role's row is its
        // member's by every column of the key the two share.
        functions.Should().OnlyContain(function => function.Returns == "SETOF uuid");
        functions[0].Body.Should().Contain("SELECT DISTINCT m.\"DocumentId\" FROM \"public\".\"DocumentShares\" m");
        functions[1].Body.Should().Contain("WHERE h.\"DocumentTitle\" = m.\"DocumentTitle\" AND h.\"DocumentId\" = m.\"DocumentId\" AND h.\"DocumentShareId\" = m.\"Id\" AND ");
        functions[3].Body.Should().Contain("SELECT r.\"Id\" FROM \"public\".\"Document\" r WHERE $1 IN ('documents.view', 'documents.edit', 'documents.share') AND r.\"OwnerId\" = {caller:uid}");
    }

    [Fact]
    public void Rules_this_contribution_cannot_answer_are_refused_when_the_functions_are_written()
    {
        using var context = FilingPostgres.Model();

        using var depot = PostgresDepot.Model();

        // What the rules have the host answer, the database is asked through a function the rules name: rules
        // that name none are for a host whose database answers nothing, and no functions can be written from them.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PalletPorter>(Rules("yards", MemberSource.Resolved())).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' have the application resolve who the caller is as a member of Pallet, and name no function that answers it in the database.*MemberSource.Resolved(\"owner/name\")*");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Kept("yards", elsewhere: new())).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' say the roles are kept elsewhere, and name no function that answers which of them give a key in the database.*rolesKeptElsewhere: new(\"owner/name\")*");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Kept("yards", above: new())).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' let Crate be reached from above, and name no function that answers where the caller holds a key in the database.*above: new(\"owner/name\")*");

        // Reached from above, and the model does not say where the resource sits: a pallet sits nowhere.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PalletPorter>(
                new MembershipRules("yards", keys: ["yards.read"], roles: [new("reader", ["yards.read"])], seeKey: "yards.read", above: new("depot/bays_where_i_hold"))).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' let Pallet be reached from above, and the model does not say where it sits.*at: resource => resource.PlaceId*");

        // Roles the rules declare are held by name, and a crate's members hold roles of the depot's.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Rules("yards")).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Crate hold roles known by DepotRoleId, and the rules 'yards' declare the roles, which a member holds by name.*");

        // A claim is a text, and a document's members are stored as uuid; the caller's user id is a uuid, and a folder's staff are text.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(Rules("papers", MemberSource.Claim("app_metadata.badge"))).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*from the claim 'app_metadata.badge', a text, and the members of Document are stored as uuid*");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<FolderMember>(Rules("shelves")).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*from MemberSource.CallerId, a uuid, and the members of Folder are stored as text*");
    }

    [Fact]
    public async Task A_key_or_a_role_with_a_quote_or_a_brace_in_it_stays_what_it_is()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);
        var odd = new MembershipRules(
            "documents",
            keys: ["documents.it's {here}", "documents.view", "documents.{seen}'s", "documents.owner's {own}"],
            roles: [new("o'brien {night}", ["documents.it's {here}", "documents.view"]), new("owner", ["documents.view"])],
            seeKey: "documents.{seen}'s");
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new MembershipRowAccessContribution<DocumentShare>(odd)], rules: []));

        // A document shared in that role, saved by the application's own work.
        var owner = UserId.CreateSequential();
        var reader = UserId.CreateSequential();
        var id = DocumentId.CreateSequential();
        await filing.Services.AsAsync(Caller.System, async provider =>
        {
            var document = new Document(id, "Night shift", owner, DocumentMembership.Owner, DateTimeOffset.UtcNow.AddDays(-2));
            document.ShareWith(reader, new NamedRole("o'brien {night}"), MemberPeriod.Open(DateTimeOffset.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow, by: owner);
            provider.GetRequiredService<FilingContext>().Documents.Add(document);
            await provider.GetRequiredService<FilingContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await using var session = await filing.SessionAsync(TestCallers.User(reader));
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it's {here}")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.{seen}'s")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it''s {here}")).Should().BeEmpty();
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_as_member_with($1) AS id", "documents.view")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner's {own}")).Should().BeEmpty("no role lists it, and the reader does not own the document");

        // And the keys of the resource, which its owner holds by owning it: one no role lists, and one the owner's role does not.
        await using var asOwner = await filing.SessionAsync(TestCallers.User(owner));
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner's {own}")).Should().Equal(id.Value);
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it's {here}")).Should().Equal(id.Value);
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner''s {own}")).Should().BeEmpty();
    }

    [Fact]
    public void A_rule_for_a_role_the_functions_are_not_granted_to_is_refused_when_the_file_is_written()
    {
        // A rule for callers who did not sign in that asks a function of signed-in users: said where the file is
        // written, naming the rule, and not where it is applied.
        var open = RowAccessRule.For<Document>("Anybody reads what they see", RowOperations.Read, DDDToolkit.Supporting.Membership.TestHost.Access.UsersReadTheDocumentsTheySee.RowAccessSql, RowAccessRoles.Anonymous);

        FluentActions.Invoking(() => FilingPostgres.AccessScript(rules: [open]))
            .Should().Throw<InvalidOperationException>().WithMessage("*Anybody reads what they see*documents_i_see*");
    }

    [Fact]
    public void Each_of_the_three_things_rules_say_is_written_by_itself()
    {
        using var depot = PostgresDepot.Model();

        // A crate's rules as they are: the depot answers who the caller is, which roles give a key, and where a key is held.
        var crates = new CrateMembershipFunctions().Contribute(depot, Export)!.Functions;
        crates.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("crates_as_member", "", "SETOF uuid"),
            ("crates_as_member_with", "text", "SETOF uuid"),
            ("crates_i_see", "", "SETOF uuid"),
            ("crates_where_i_hold", "text", "SETOF uuid"));
        crates.Should().OnlyContain(function => function.SecurityDefiner && function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User }));

        // Asked by logical name, a text the script fills in: nothing here refers to what defines them.
        crates[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:depot/caller_porter}()) AND ");
        crates[1].Body.Should().Contain("h.\"RoleId\" IN (SELECT giving.role FROM {fn:depot/roles_with_key}($1) AS giving(role))");
        crates[2].Body.Should().Contain("UNION\nSELECT r.\"Id\" FROM \"depot\".\"Crates\" r WHERE r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}('crates.see') AS reached(place))");
        crates[3].Body.Should().Contain("r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}($1) AS reached(place))");

        // Members that are users, by their own id, with the depot's roles and no reach from above: only the roles are asked.
        var users = new MembershipRowAccessContribution<CratePorter>(Kept("crates", MemberSource.CallerId, elsewhere: new("depot/roles_with_key"))).Contribute(depot, Export)!.Functions;
        users[0].Body.Should().Contain("m.\"MemberId\" = {caller:uid} AND ").And.NotContain("{fn:");
        users[1].Body.Should().Contain("{fn:depot/roles_with_key}($1)");
        users[2].Body.Should().NotContain("reached.place", "a crate that nothing above reaches is seen by its members and its owner alone")
            .And.Contain("UNION\nSELECT r.\"Id\" FROM \"depot\".\"Crates\" r WHERE r.\"OwnerId\" = {caller:uid}", "its owner sees it by the owner column, whether or not its member row is there");
        users[3].Body.Should().NotContain("reached.place");

        // What a member's role can give stands in front of what the host answers, as the rules say it.
        string Cut(MemberKeys keys)
            => new MembershipRowAccessContribution<CratePorter>(Kept("crates", MemberSource.CallerId, elsewhere: new("depot/roles_with_key"), memberKeys: keys)).Contribute(depot, Export)!.Functions[1].Body;
        Cut(MemberKeys.Only("crates.pack", "crates.it's")).Should().Contain("\n  AND $1 IN ('crates.pack', 'crates.it''s')\n  AND EXISTS (");
        Cut(MemberKeys.AllBut("crates.move")).Should().Contain("\n  AND $1 NOT IN ('crates.move')\n  AND EXISTS (");
        Cut(MemberKeys.AllBut()).Should().NotContain("$1 NOT IN").And.Contain("{fn:depot/roles_with_key}($1)");
        Cut(MemberKeys.Only()).Should().Contain("\n  AND false\n", "a role gives a member nothing where the rules list nothing");

        // A pallet: the depot answers who the caller is, and the roles are the rules' own list.
        var pallets = new PalletMembershipFunctions().Contribute(depot, Export)!.Functions;
        pallets[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:depot/caller_porter}()) AND ");
        pallets[1].Body.Should().Contain("(VALUES ('loader', 'pallets.see'), ('loader', 'pallets.load'), ('checker', 'pallets.see'), ").And.NotContain("roles_with_key");
        pallets[3].Body.Should().Contain("$1 IN ('pallets.see', 'pallets.load', 'pallets.strap') AND r.\"OwnerId\" = (SELECT {fn:depot/caller_porter}())");

        // Roles the rules declare, and reach from above, for members that are users: a document that is reached
        // by whoever holds the key over its owner. Where a resource sits is whatever its row keeps.
        using var managed = new ManagedContext(new DbContextOptionsBuilder<ManagedContext>().UseNpgsql("Host=model-only").Options);
        var above = new MembershipRules(
            "documents",
            keys: [DocumentKeys.View, DocumentKeys.Edit],
            roles: [new("contributor", [DocumentKeys.View, DocumentKeys.Edit])],
            seeKey: DocumentKeys.View,
            above: new("staff/reports_where_i_hold"));
        var documents = new MembershipRowAccessContribution<DocumentShare>(above).Contribute(managed, Export)!.Functions;
        documents[1].Body.Should().Contain("(VALUES ('contributor', 'documents.view'), ('contributor', 'documents.edit'), ");
        documents[2].Body.Should().EndWith("UNION\nSELECT r.\"Id\" FROM \"public\".\"Document\" r WHERE r.\"OwnerId\" IN (SELECT reached.place FROM {fn:staff/reports_where_i_hold}('documents.view') AS reached(place))");
        documents[3].Body.Should().EndWith("UNION\nSELECT r.\"Id\" FROM \"public\".\"Document\" r WHERE $1 IS NOT NULL AND r.\"OwnerId\" IN (SELECT reached.place FROM {fn:staff/reports_where_i_hold}($1) AS reached(place))");
    }

    [Fact]
    public void The_fingerprint_covers_what_the_functions_are_written_from()
    {
        using var filing = FilingPostgres.Model();
        using var depot = PostgresDepot.Model();
        static string MarkerOf(IRowAccessContribution contribution, DbContext context, RowAccessExport? export = null)
            => contribution.Contribute(context, export ?? Export)!.Functions[0].Body.Split('\n')[0];

        // What the host answers is part of it: another name for one of its functions, or another list of what a
        // member's role gives, is another fingerprint.
        var crates = MarkerOf(new CrateMembershipFunctions(), depot);
        crates.Should().MatchRegex("^-- Membership of crates, in form [0-9]+, written from the rules [0-9a-f]{64}$");
        MembershipRules[] others =
        [
            Kept("crates", MemberSource.Resolved("depot/acting_porter"), new("depot/roles_with_key"), new("depot/bays_where_i_hold")),
            Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_given"), new("depot/bays_where_i_hold")),
            Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key"), new("depot/bays_held")),
            Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key")),
            Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key"), new("depot/bays_where_i_hold"), MemberKeys.AllBut(CrateKeys.Move)),
            Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key"), new("depot/bays_where_i_hold"), MemberKeys.Only(CrateKeys.Move, CrateKeys.ManageDepot)),
        ];
        var markers = others.Select(rules => MarkerOf(new MembershipRowAccessContribution<CratePorter>(rules), depot)).Append(crates).ToList();
        markers.Should().OnlyHaveUniqueItems();
        MarkerOf(new MembershipRowAccessContribution<CratePorter>(
                Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key"), new("depot/bays_where_i_hold"), MemberKeys.AllBut(CrateKeys.Move, CrateKeys.ManageDepot))), depot)
            .Should().Be(crates, "rules that say the same are written the same");

        // The keys the lock is written from, and the scopes whose work is the application's own, are part of it
        // as well: a database locked by another key, or answering another scope, is not the one the rules say.
        static MembershipRules Documents(string? membersKey = null, string? ownerKey = null, string[]? scopes = null)
            => new("documents", keys: [DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share], roles: [new("contributor", [DocumentKeys.View, DocumentKeys.Edit])],
                changeMembersKey: membersKey, changeOwnerKey: ownerKey, systemScopes: scopes);
        string[] documents =
        [
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents()), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(membersKey: DocumentKeys.Share)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(membersKey: DocumentKeys.Edit)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(ownerKey: DocumentKeys.Share)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(membersKey: DocumentKeys.Share, ownerKey: DocumentKeys.Share)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing"])), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing", "archive"])), filing),

            // The scopes are compared with the role scoped work runs as, so that role's name is part of it where a scope is named.
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing"])), filing, new RowAccessExport { Roles = FilingPostgres.RoleNames with { SystemIn = "filing_worker" } }),
        ];
        documents.Should().OnlyHaveUniqueItems();
        MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents()), filing, new RowAccessExport { Roles = FilingPostgres.RoleNames with { SystemIn = "filing_worker" } })
            .Should().Be(documents[0], "rules that name no scope are written without that role");

        // The trigger of the lock says the same first line as the functions: one thing to hold a database to.
        var folders = new FolderMembershipFunctions().Contribute(filing, Export)!;
        folders.Statements[0].Should().Contain("\n    " + folders.Functions[0].Body.Split('\n')[0] + "\n");
    }

    /// <summary>
    /// The form the SQL is written in, as its first line says it, and a hash of everything that is written for
    /// four sets of rules said in the test itself, one for each way rules can be said, with that first line
    /// taken out. Kept together: the form is what tells the start-up check that a database holds what an
    /// earlier version of the package wrote, so it goes up whenever what is written changes.
    /// </summary>
    private static readonly (string SqlForm, string Written) KeptTogether = ("5", "a42a6ca93a7f0f06f71e83069597c877655dc91690bd8163d148f3da8ca917eb");

    [Fact]
    public void What_is_written_for_the_same_rules_changes_only_together_with_the_form_it_is_written_in()
    {
        using var filing = FilingPostgres.Model();
        using var depot = PostgresDepot.Model();
        using var gardens = PostgresGarden.Model();

        // Rules said here, and not the hosts' own, so that only a change of what the package writes moves the hash.
        (IRowAccessContribution Contribution, DbContext Context)[] hosts =
        [
            // Users by their id, declared roles, a key that being a member gives, one key for both locks.
            (new MembershipRowAccessContribution<DocumentShare>(new MembershipRules(
                "documents",
                keys: ["documents.view", "documents.edit", "documents.share"],
                roles: [new("contributor", ["documents.view", "documents.edit"]), new("onlooker", ["documents.view"])],
                seeKey: "documents.view",
                changeMembersKey: "documents.share",
                changeOwnerKey: "documents.share")), filing),

            // Members by a claim, a declared owner's role, a key no role gives, names of its own, two roles
            // that may ask, a key for each lock, and a scope of the application's own.
            (new MembershipRowAccessContribution<FolderMember>(new MembershipRules(
                "folders",
                keys: ["folders.read", "folders.file", "folders.staff", "folders.shred", "folders.hand-over"],
                roles: [new("keeper", ["folders.read", "folders.file", "folders.staff", "folders.shred"]), new("visitor", ["folders.read"])],
                members: MemberSource.Claim("app_metadata.staff"),
                ownerRole: "keeper",
                memberKeys: MemberKeys.AllBut("folders.shred"),
                functions: new("folder_ids_staffed", "folder_ids_staffed_with", "folder_ids_seen", "folder_ids_held"),
                grantTo: [RowAccessRoles.User, RowAccessRoles.Token(FilingPostgres.ArchivistTokenRole)],
                changeMembersKey: "folders.staff",
                changeOwnerKey: "folders.hand-over",
                systemScopes: ["filing"])), filing),

            // Members the host resolves, roles kept elsewhere, reached from above, no lock.
            (new MembershipRowAccessContribution<CratePorter>(Kept("crates", MemberSource.Resolved("depot/caller_porter"), new("depot/roles_with_key"), new("depot/bays_where_i_hold"))), depot),

            // Roles kept for the resource, a key for the members' lock alone, which then holds a role given to
            // one the caller sees; and the trigger that keeps the owner's role in use.
            (new MembershipRowAccessContribution<PlotGardener>(new MembershipRules(
                "plots",
                keys: ["plots.see", "plots.plant", "plots.fence"],
                roles: [new("tender", ["plots.see", "plots.plant"])],
                seeKey: "plots.see",
                memberKeys: MemberKeys.Only("plots.see", "plots.plant"),
                rolesKept: true,
                changeMembersKey: "plots.fence")), gardens),
        ];

        var forms = new HashSet<string>(StringComparer.Ordinal);
        var written = new StringBuilder();
        foreach (var (contribution, context) in hosts)
        {
            var result = contribution.Contribute(context, Export)!;
            foreach (var function in result.Functions)
            {
                var lines = function.Body.Split('\n');
                forms.Add(System.Text.RegularExpressions.Regex.Match(lines[0], "in form (?<form>[0-9]+),").Groups["form"].Value);

                // Without the first line, which names the rules and the form: everything else is what a database holds.
                written.Append(function.Name).Append('(').Append(function.Parameters).Append(") ").Append(function.Returns).Append(' ').Append(function.SecurityDefiner).Append('\n')
                    .Append(string.Join('\n', lines.Skip(1))).Append('\n');
            }

            foreach (var policy in result.Policies)
            {
                written.Append(policy.Table.GetTableName()).Append('|').Append(policy.Name).Append('|').Append(policy.Command).Append('|').Append(policy.Role).Append('|')
                    .Append(policy.Using).Append('|').Append(policy.WithCheck).Append('|').Append(policy.Restrictive).Append('\n');
            }

            foreach (var statement in result.Statements)
            {
                written.Append(string.Join('\n', statement.Split('\n').Where(line => !line.TrimStart().StartsWith("-- Membership of ", StringComparison.Ordinal)))).Append('\n');
            }
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(written.ToString())));
        var form = forms.Should().ContainSingle().Which;

        if (form == KeptTogether.SqlForm)
        {
            hash.Should().Be(
                KeptTogether.Written,
                "(it is " + hash + " now) what the package writes for the same rules changed, and the form it is written in did not. A database that holds the SQL from before would pass the "
                + "start-up check, since its first line would still be the one the rules give. Raise MembershipSql.SqlForm, and keep the new form and this hash here");
        }
        else
        {
            (form, hash).Should().Be(KeptTogether, "the form went up: keep it here with the hash of what is written in it");
        }
    }

    private static MembershipRules Rules(string name, MemberSource? members = null)
        => new(name, keys: [name + ".read"], roles: [new("reader", [name + ".read"])], members: members);

    /// <summary>Rules for a crate: the roles are the depot's, kept elsewhere; who a member is and what is above are the caller's to say.</summary>
    private static MembershipRules Kept(string name, MemberSource? members = null, RolesKeptElsewhere? elsewhere = null, ReachFromAbove? above = null, MemberKeys? memberKeys = null)
        => new(
            name,
            keys: [CrateKeys.Scrap],
            members: members,
            seeKey: CrateKeys.See,
            ownerRole: CrateMembership.OwnerRole,
            memberKeys: memberKeys ?? MemberKeys.AllBut(CrateKeys.Move, CrateKeys.ManageDepot),
            rolesKeptElsewhere: elsewhere ?? new("depot/roles_with_key"),
            above: above);

    /// <summary>A context that keeps the documents where their owner is: who holds a key over a document's owner reaches the document.</summary>
    private sealed class ManagedContext(DbContextOptions<ManagedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Document>().HasMembers(row => row.Shares, row => row.OwnerId, at: row => row.OwnerId);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context of another module, which maps none of these resources.</summary>
    private sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options);

    /// <summary>A context that keeps the documents under a key of two parts, said before their members are mapped.</summary>
    private sealed class TwoPartKeyContext(DbContextOptions<TwoPartKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Document>(document =>
            {
                document.HasKey(nameof(Document.Title), nameof(Document.Id));
                document.HasMembers(row => row.Shares, row => row.OwnerId);
            });

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
