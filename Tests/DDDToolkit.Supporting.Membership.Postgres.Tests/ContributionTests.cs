using DDDToolkit.Abstractions.Access;
using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Membership.TestHost.Access;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// What the contribution of one resource writes: four functions named after the resource, from its rules and the
/// names its model gives its tables, granted to the roles its row access rules are for; and the lock, for rules with
/// permissions that lock the members and the owner. No policy that allows anything, no table kept to itself, and
/// nothing for a context that is another resource's.
/// </summary>
public sealed class ContributionTests(FilingPostgres postgres)
{
    /// <summary>
    /// What the functions are written with: the roles as the hosts map them, the folders' token role among them, and the
    /// hosts' row access rules, whose roles the functions are granted to.
    /// </summary>
    private static readonly RowAccessExport Export = new()
    {
        Roles = FilingPostgres.RoleNames,
        Rules = [.. FilingPostgres.Rules, .. PostgresDepot.Rules, .. PostgresGarden.Rules],
    };

    private static DatabaseFunction Depot(string name) => new(DepotFunctions.Owner, name);

    /// <summary><see cref="Export"/> with other roles, or other rules.</summary>
    private static RowAccessExport ExportWith(RowAccessRoleNames? roles = null, IReadOnlyList<RowAccessRule>? rules = null)
        => new() { Roles = roles ?? Export.Roles, Rules = rules ?? Export.Rules };

    [Fact]
    public void The_contribution_writes_five_functions_named_after_the_resource_and_allows_nothing()
    {
        using var context = FilingPostgres.Model();

        var written = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!;

        written.Policies.Should().OnlyContain(policy => policy.Restrictive, "what a caller may read and change of a folder is the application's own rules to say: the package only narrows");
        written.ExclusiveTables.Should().BeNull("the member tables follow the rules of the resource that owns them");
        written.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("folders_as_member", "", "SETOF bigint"),
            ("folders_as_member_with", "text", "SETOF bigint"),
            ("folders_i_see", "", "SETOF bigint"),
            ("folders_where_i_hold", "text", "SETOF bigint"),
            ("folders_fellow_members", "", "SETOF text"));
        written.Functions.Should().OnlyContain(function => function.SecurityDefiner && !function.Inlinable && function.Volatility == "STABLE");

        // A document's are its own: other names, another id.
        var documents = new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!;
        documents.Functions.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("documents_as_member", "", "SETOF uuid"),
            ("documents_as_member_with", "text", "SETOF uuid"),
            ("documents_i_see", "", "SETOF uuid"),
            ("documents_where_i_hold", "text", "SETOF uuid"),
            ("documents_fellow_members", "", "SETOF uuid"));
    }

    [Fact]
    public void The_functions_are_granted_to_the_roles_the_resources_row_access_rules_are_for()
    {
        using var context = FilingPostgres.Model();

        // The folders' rules are for signed-in users, and one for the archivists' token role; and the folders' module
        // lets its own scoped work through, so the role that work runs as asks them as well.
        new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Functions
            .Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User, Archivists.Role, RowAccessRoles.SystemIn }));

        // A token role named in a row access rule alone is granted them by that rule; without the rule, it is not.
        var withoutArchivists = ExportWith(rules: [.. Export.Rules.Where(rule => !rule.Roles.Contains(Archivists.Role))]);
        new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, withoutArchivists)!.Functions
            .Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User, RowAccessRoles.SystemIn }));

        // The documents' rules are for signed-in users, and turn scoped work off: the user's role alone.
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Functions
            .Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User }));

        // A rule for callers who did not sign in grants them nothing: an anonymous caller is nobody's member.
        var anonymous = ExportWith(rules: [.. Export.Rules, RowAccessRule.For<Document>("Anybody reads the title", RowOperations.Read, "true", RowAccessRoles.Anonymous)]);
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, anonymous)!.Functions
            .Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User }));

        // And a resource no rule is for, whose module lets its scoped work through: that role alone.
        var papers = MembershipRules.Declare(typeof(Document), "papers", [new("papers.read")], Roles.Fixed([new("reader", ["papers.read"])]), systemScopes: ["filing"]);
        new MembershipRowAccessContribution<DocumentShare>(papers).Contribute(context, ExportWith(rules: []))!.Functions
            .Should().OnlyContain(function => function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.SystemIn }));
    }

    [Fact]
    public void Rules_with_a_permission_that_locks_the_members_lock_both_member_tables_for_the_roles_the_functions_are_granted_to()
    {
        using var context = FilingPostgres.Model();
        var staff = context.Model.FindEntityType(typeof(FolderMember))!;
        var held = MembershipModel.For(context.Model, typeof(FolderMember))!.Roles;

        var written = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Policies;

        // Adding, changing and removing a row, on the staff's table and on the table of the roles they hold, for each
        // role that may ask the folders' functions, scoped system work among them, which passes where the rules let its
        // scope through: never reading, which stays what the folder's own rules say.
        written.Select(policy => (policy.Table, policy.Command, policy.Role)).Should().BeEquivalentTo(
            from table in new[] { staff, held }
            from role in new[] { RowAccessRoles.User, Archivists.Role, RowAccessRoles.SystemIn }
            from command in new[] { "INSERT", "UPDATE", "DELETE" }
            select (table, command, role));
        written.Should().OnlyContain(policy => policy.Restrictive && policy.Name == "Members change with the permission");

        // A row is written only on a folder the caller holds the permission on, as the folder's own function answers it: the
        // permission that locks the staff, or the one that locks the keeper, since naming a keeper writes the new
        // keeper's rows.
        const string Holds =
            "(\"FolderId\" IN (SELECT held.id FROM {fn:folders/folders_where_i_hold}('folders.staff') AS held(id))"
            + " OR \"FolderId\" IN (SELECT held.id FROM {fn:folders/folders_where_i_hold}('folders.hand-over') AS held(id)))";
        written.Where(policy => policy.Command == "INSERT").Should().OnlyContain(policy => policy.Using == null && policy.WithCheck == Holds);
        written.Where(policy => policy.Command == "UPDATE").Should().OnlyContain(policy => policy.Using == Holds && policy.WithCheck == Holds);
        written.Where(policy => policy.Command == "DELETE").Should().OnlyContain(policy => policy.Using == Holds && policy.WithCheck == null);

        // A document's, for signed-in users alone, asked once where one permission locks both; and nothing for rules
        // with no such permission.
        var documents = new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Policies;
        documents.Should().HaveCount(6).And.OnlyContain(policy => policy.Role == RowAccessRoles.User && policy.Restrictive);
        documents.Should().Contain(policy => policy.Table.ClrType == typeof(DocumentShare) && policy.Command == "INSERT"
            && policy.WithCheck == "\"DocumentId\" IN (SELECT held.id FROM {fn:documents/documents_where_i_hold}('documents.share') AS held(id))");

        using var gardens = PostgresGarden.Model();
        new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules).Contribute(gardens, Export)!.Policies.Should().BeEmpty("no permission of a shed locks its hands");
    }

    [Fact]
    public void Rules_with_a_permission_that_locks_the_owner_pin_the_owner_column_for_the_roles_the_functions_are_granted_to()
    {
        using var context = FilingPostgres.Model();

        var written = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Statements;

        // A trigger, since only a trigger sees the row a statement found and the row it leaves: it fires where the
        // keeper would change, and refuses as a policy refuses unless the caller held the permission before.
        written.Should().HaveCount(4);
        written[0].Should()
            .StartWith("CREATE OR REPLACE FUNCTION \"filing\".folders_owner_stays() RETURNS trigger\n    LANGUAGE plpgsql SET search_path = '' AS $body$\nBEGIN\n    -- Membership of folders (Folder), in form ")
            .And.Contain($"IF CURRENT_USER IN ('authenticated', '{FilingPostgres.ArchivistRole}', 'ddd_system_in')")
            .And.Contain("AND NOT EXISTS (SELECT 1 FROM {fn:folders/folders_where_i_hold}('folders.hand-over') AS held(id) WHERE held.id = OLD.\"Id\") THEN")
            .And.Contain("RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'folders_owner_stays', HINT = 'ddd:access.refused', MESSAGE = ")
            .And.NotContain("SECURITY DEFINER", "it asks as the caller, who may ask the folder's functions")
            .And.NotContain("\r");
        written[1].Should().Be("REVOKE ALL ON FUNCTION \"filing\".folders_owner_stays() FROM PUBLIC");
        written[2].Should().Be("DROP TRIGGER IF EXISTS folders_owner_stays ON \"filing\".\"Folders\"");
        written[3].Should().Be(
            "CREATE TRIGGER folders_owner_stays BEFORE UPDATE OF \"Keeper\" ON \"filing\".\"Folders\"\n"
            + "    FOR EACH ROW WHEN (OLD.\"Keeper\" IS DISTINCT FROM NEW.\"Keeper\")\n"
            + "    EXECUTE FUNCTION \"filing\".folders_owner_stays()");

        // Rules with no such permission take a lock away that earlier rules may have left behind, and write none; where
        // a customer makes the roles, they take away the trigger an earlier form wrote to keep an owner's role in use.
        using var gardens = PostgresGarden.Model();
        new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules).Contribute(gardens, Export)!.Statements.Should().Equal(
            "DROP TRIGGER IF EXISTS sheds_owner_stays ON \"gardens\".\"Sheds\"",
            "DROP FUNCTION IF EXISTS \"gardens\".sheds_owner_stays()",
            "DROP TRIGGER IF EXISTS sheds_owner_role_stays ON \"gardens\".\"ShedRoles\"",
            "DROP FUNCTION IF EXISTS \"gardens\".sheds_owner_role_stays()");

        // A role the host maps to no database role is nobody a lock can be written for: said, naming the resource.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, new RowAccessExport { Rules = Export.Rules }))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("A row access rule of Folder is for the role '@token:archivist', which may ask its functions, and they cannot be granted to it: '@token:archivist' is the token role 'archivist', which is mapped to no database role*");

        // And a name Postgres would cut is refused where the lock is written: it is named after the resource.
        var longName = MembershipRules.Declare(typeof(Document), new string('d', 60), [new("d.read", NamesOwner: true)], Roles.Fixed([new("reader", ["d.read"])]));
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(longName).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*longer than the 63 characters Postgres keeps of a name*");

        // The function the trigger runs is written between two quotes of its own. A permission that has that quote in it
        // would end the body early, and what came after it would be read as the statement's own: refused where the
        // lock is written, as the export refuses a function's body with its quote in it.
        var quoted = MembershipRules.Declare(
            typeof(Document), "documents", [new("d.read"), new("d.give $body$; DROP TABLE x; --", NamesOwner: true)], Roles.Fixed([new("reader", ["d.read"])]));
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(quoted).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The lock on the owner column of Document cannot be written from the rules 'documents'*has '$body$' in it, which would end the function's body early.");
    }

    [Fact]
    public void Where_a_customer_makes_the_roles_the_lock_holds_a_role_given_to_one_the_caller_sees()
    {
        using var gardens = PostgresGarden.Model();
        var held = MembershipModel.For(gardens.Model, typeof(PlotGardener))!.Roles.GetTableName();
        var plots = new MembershipRowAccessContribution<PlotGardener>(PlotMembership.Rules).Contribute(gardens, Export)!;

        // A plot's permission that fences locks the gardeners. On the table of the roles a gardener holds a second
        // restrictive policy, for adding and changing a row, for each role the lock holds: the role is one the caller
        // sees in the role table, asked as the caller. Removing a row asks nothing of its role.
        const string Seen = "EXISTS (SELECT 1 FROM \"gardens\".\"PlotRoles\" k WHERE k.\"Id\" = \"gardens\".\"PlotGardenerRoles\".\"RoleId\")";
        plots.Policies.Where(policy => policy.Name == "Members hold roles the caller sees")
            .Select(policy => (policy.Table.GetTableName(), policy.Command, policy.Role, policy.Using, policy.WithCheck, policy.Restrictive))
            .Should().BeEquivalentTo(new (string?, string, string, string?, string?, bool)[]
            {
                (held, "INSERT", RowAccessRoles.User, null, Seen, true),
                (held, "UPDATE", RowAccessRoles.User, "true", Seen, true),
                (held, "INSERT", RowAccessRoles.SystemIn, null, Seen, true),
                (held, "UPDATE", RowAccessRoles.SystemIn, "true", Seen, true),
            });

        // It is part of the lock: the sheds lock nothing, so they get neither. And the documents' roles are in code,
        // which are names and no rows.
        new MembershipRowAccessContribution<ShedHand>(ShedMembership.Rules).Contribute(gardens, Export)!.Policies.Should().BeEmpty();
        using var filing = FilingPostgres.Model();
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(filing, Export)!.Policies.Should().NotContain(policy => policy.Name == "Members hold roles the caller sees");

        // No trigger keeps any role in use: the owner holds none, and a role is archived like any other.
        plots.Statements.Should().NotContain(statement => statement.StartsWith("CREATE", StringComparison.Ordinal) && statement.Contains("owner_role_stays", StringComparison.Ordinal));
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(filing, Export)!.Statements.Should().NotContain(statement => statement.Contains("owner_role_stays"));
    }

    [Fact]
    public void The_applications_own_work_in_a_scope_the_rules_let_through_is_answered_every_resource()
    {
        using var context = FilingPostgres.Model();

        // A folder's rules let the module's own scope through: seen and held on answer every folder for the role scoped
        // work runs as, when its claims say that scope. As member and as member with do not: such work is nobody's member.
        var folders = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Functions;
        const string OwnWork = "UNION\nSELECT r.\"Id\" FROM \"filing\".\"Folders\" r WHERE {caller:role} = 'ddd_system_in' AND {caller:claim:scope} IN ('filing')";
        folders[2].Body.Should().EndWith(OwnWork);
        folders[3].Body.Should().EndWith(OwnWork + " AND $1 IS NOT NULL");
        folders[0].Body.Should().NotContain("scope");
        folders[1].Body.Should().NotContain("scope");

        // The role is the one the host runs scoped work as, and several scopes are one list.
        var renamed = ExportWith(roles: FilingPostgres.RoleNames with { SystemIn = "filing_worker" });
        var several = MembershipRules.Declare(typeof(Document), "documents", [new(DocumentPermissions.View)], Roles.Fixed([new("reader", [DocumentPermissions.View])]), systemScopes: ["filing", "night-shift"]);
        new MembershipRowAccessContribution<DocumentShare>(several).Contribute(context, renamed)!.Functions[2].Body
            .Should().EndWith("WHERE {caller:role} = 'filing_worker' AND {caller:claim:scope} IN ('filing', 'night-shift')");

        // A document's rules say no system work in a scope, []: nothing of the kind is written, and scoped work is answered nothing.
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Functions.Should().OnlyContain(function => !function.Body.Contains("scope"));
    }

    [Fact]
    public void A_function_that_takes_a_permission_asks_nothing_without_one()
    {
        using var depot = PostgresDepot.Model();

        var crates = new MembershipRowAccessContribution<CratePorter>(CrateMembership.Rules).Contribute(depot, Export)!.Functions;

        // A condition without a column gates the whole part it is in: with no permission, the member rows are not read and the
        // depot's functions are not asked.
        crates[1].Body.Should().Contain("\nWHERE $1 IS NOT NULL AND m.\"MemberId\" = ");
        crates[3].Body.Should().Contain("r WHERE $1 IS NOT NULL AND r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}($1) AS reached(place))");

        // Asked with a permission the rules name, the depot's function is asked with that permission: there is nothing to gate.
        crates[2].Body.Should().Contain("r WHERE r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}('crates.see') AS reached(place))");
    }

    [Fact]
    public void The_functions_are_owned_by_the_resources_name_which_is_what_a_rule_asks_them_by()
    {
        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Owner.Should().Be("documents");
        new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Owner.Should().Be("folders");
        new MembershipRowAccessContribution<DocumentShare>(Rules<Document>("sales.orders")).Owner.Should().Be("sales-orders", "an owner is letters, digits and dashes");

        // The application's rules ask by the resource's id, or through questions that name that owner and those names.
        var script = FilingPostgres.AccessScript();
        script.Should().Contain("USING (\"Id\" = ANY (ARRAY(SELECT \"filing\".documents_i_see())))")
            .And.Contain("USING (\"Id\" = ANY (ARRAY(SELECT \"filing\".folders_i_see())))");
        (DocumentMembership.Rules.Functions.Seen, DocumentMembership.Rules.Functions.HeldOn).Should().Be(("documents_i_see", "documents_where_i_hold"));
        (FolderMembership.Rules.Functions.Seen, FolderMembership.Rules.Functions.HeldOn).Should().Be(("folders_i_see", "folders_where_i_hold"));
    }

    [Fact]
    public void The_three_functions_that_answer_access_say_which_set_they_answer_for_the_resources_id()
    {
        using var context = FilingPostgres.Model();

        var documents = new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Functions;
        var folders = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Functions;

        documents.Select(function => (function.Name, function.Answers)).Should().Equal(
            ("documents_as_member", null),
            ("documents_as_member_with", null),
            ("documents_i_see", new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.Seen)),
            ("documents_where_i_hold", new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.HeldOn)),
            ("documents_fellow_members", new ResourceAccessAnswer(typeof(DocumentId), ResourceAccessSet.FellowMembers)));
        folders.Select(function => (function.Name, function.Answers)).Should().Equal(
            ("folders_as_member", null),
            ("folders_as_member_with", null),
            ("folders_i_see", new ResourceAccessAnswer(typeof(FolderId), ResourceAccessSet.Seen)),
            ("folders_where_i_hold", new ResourceAccessAnswer(typeof(FolderId), ResourceAccessSet.HeldOn)),
            ("folders_fellow_members", new ResourceAccessAnswer(typeof(FolderId), ResourceAccessSet.FellowMembers)));
    }

    [Fact]
    public void A_rule_that_asks_by_the_resources_id_is_written_as_one_that_names_the_function()
    {
        var seen = ResourceAccessAnswer.NameOf(typeof(DocumentId), ResourceAccessSet.Seen);
        DDDToolkit.Supporting.Membership.TestHost.Access.UsersReadTheDocumentsTheySee.RowAccessSql.Should().Be($"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{seen}}}())))");
        DDDToolkit.Supporting.Membership.TestHost.Access.UsersChangeTheDocumentsTheyWorkOn.RowAccessSql.Should().Contain("{fn:@DDDToolkit.Supporting.Membership.TestHost.DocumentId/held_on}('documents.edit')");

        // The same rules, naming the functions as the documents' name has them: the file is the same, byte for byte.
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
        var refused = () => FilingPostgres.AccessScript(contributions: [new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules)], rules: [read]);

        refused.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Users read the documents they see' asks the resources the caller sees, by the id DDDToolkit.Supporting.Membership.TestHost.DocumentId, "
            + "and no row access contribution answers it for the contexts this is written with. Hand the script the contribution that keeps that resource's access in RowAccessExport.Contributions, the Membership package's for a resource with members*");
    }

    [Fact]
    public void Who_the_caller_is_as_a_member_is_what_the_rules_say_or_what_follows_from_the_member_id()
    {
        using var context = FilingPostgres.Model();

        var documents = new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Functions;
        var folders = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(context, Export)!.Functions;

        // A document's members are known by an id over a Guid, and no contribution keeps such an id: the caller's own.
        // The folder's rules name a claim: places the script fills in.
        documents[0].Body.Should().Contain("m.\"MemberId\" = {caller:uid}").And.NotContain("claim");
        folders[0].Body.Should().Contain("m.\"StaffCode\" = {caller:claim:app_metadata.staff}").And.NotContain("{caller:uid}");

        // An owner holds the permissions marked Owner, and none but those, by matching the owner's column, never a role.
        documents[3].Body.Should().Contain("$1 IN ('documents.view', 'documents.edit', 'documents.share') AND r.\"OwnerId\" = {caller:uid}");
        folders[3].Body.Should().Contain(
            "$1 IN ('folders.read', 'folders.file', 'folders.staff', 'folders.shred', 'folders.hand-over') AND r.\"Keeper\" = {caller:claim:app_metadata.staff}");

        // Being a member gives a document's permission that every member holds; no permission of a folder is held so.
        documents[3].Body.Should().Contain("WHERE $1 = 'documents.view'");
        folders[3].Body.Should().NotContain("folders_as_member}()");

        // Which role gives which permission: only the permissions a role can give. Shredding is no role's to give.
        folders[1].Body.Should().Contain("('keeper', 'folders.staff')").And.Contain("('clerk', 'folders.file')").And.NotContain(FolderPermissions.Shred);
    }

    [Fact]
    public void A_functions_body_is_the_same_text_whichever_machine_writes_it()
    {
        using var context = FilingPostgres.Model();

        foreach (var contribution in FilingPostgres.Contributions)
        {
            var functions = contribution.Contribute(context, Export)!.Functions;

            // Line feeds alone, whatever line endings the package's own source was checked out with: an access file
            // written on one machine is the file written on another.
            functions.Should().OnlyContain(function => !function.Body.Contains('\r'));

            // And every body says, in its first lines, which rules it was written from, for which resource, and who may
            // execute it: what the start-up check and the export read.
            functions.Select(function => function.Body.Split('\n')[0]).Distinct().Should().ContainSingle()
                .Which.Should().MatchRegex("^-- Membership of (documents \\(Document\\)|folders \\(Folder\\)), in form [0-9]+, written from the rules [0-9a-f]{64}$");
            functions.Select(function => function.Body.Split('\n')[1]).Distinct().Should().ContainSingle()
                .Which.Should().StartWith("-- Executed by: ");
        }
    }

    [Fact]
    public void A_context_that_does_not_map_the_resource_is_none_of_its_business()
    {
        using var other = new OtherContext(new DbContextOptionsBuilder<OtherContext>().UseNpgsql("Host=model-only").Options);

        new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(other, Export).Should().BeNull();
        new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(other, Export).Should().BeNull();
    }

    [Fact]
    public void A_resource_known_by_more_than_its_id_is_answered_by_its_id_and_its_rows_joined_on_its_whole_permission()
    {
        using var context = new TwoPartKeyContext(new DbContextOptionsBuilder<TwoPartKeyContext>().UseNpgsql("Host=model-only").Options);

        var functions = new MembershipRowAccessContribution<DocumentShare>(DocumentMembership.Rules).Contribute(context, Export)!.Functions;

        // The functions answer the resource's id, as the questions that ask them do, and a role's row is its member's
        // by every column of the permission the two share.
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

        // What the rules have the host answer, the database is asked through a function the rules name: rules that name
        // none are for a host whose database answers nothing, and no functions can be written from them.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PalletPorter>(Rules<Pallet>("yards", MemberSource.Resolved())).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' have the application resolve who the caller is as a member of Pallet, and name no function that answers it in the database.*DatabaseFunction*");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Kept("yards", roles: null, rolesFunction: false)).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' say the roles are kept elsewhere, and name no function that answers which of them give a permission in the database.*DatabaseFunction*");

        // Reached from above, and nothing answers where the caller holds a permission at a bay: the depot's places are its own,
        // and the rules name no function of the depot's.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Kept("yards", above: true)).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' let Crate be reached from above, where it hangs at a BayId, and nothing answers in the database where the caller holds a permission at one*");

        // Reached from above, and the resource marks no place it hangs at: a pallet hangs nowhere.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<PalletPorter>(
                MembershipRules.Declare(typeof(Pallet), "yards", [new("yards.read", EveryMember: true)], Roles.Fixed([new("reader", ["yards.read"])]), members: MemberSource.Resolved(Depot("caller_porter")), reachFromAbove: Depot("bays_where_i_hold"))).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The rules 'yards' let Pallet be reached from above, and it marks no place it hangs at.*[At]*");

        // Roles in code are held by name, and a crate's members hold roles of the depot's.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<CratePorter>(Rules<Crate>("yards", MemberSource.Resolved(Depot("caller_porter")))).Contribute(depot, Export))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("The members of Crate hold roles known by DepotRoleId, and the rules 'yards'*");

        // A claim is a text, and a document's members are stored as uuid; the caller's user id is a uuid, and a folder's staff are text.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<DocumentShare>(Rules<Document>("papers", MemberSource.Claim("app_metadata.badge"))).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*from the claim 'app_metadata.badge', a text, and the members of Document are stored as uuid*");
        FluentActions.Invoking(() => new MembershipRowAccessContribution<FolderMember>(Rules<Folder>("shelves", MemberSource.CallerId)).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("*from MemberSource.CallerId, a uuid, and the members of Folder are stored as text*");

        // Said by nothing, who a member is follows from what it is known by: a text is nobody's to answer.
        FluentActions.Invoking(() => new MembershipRowAccessContribution<FolderMember>(Rules<Folder>("shelves")).Contribute(context, Export))
            .Should().Throw<InvalidOperationException>().WithMessage("Who the caller is as a member of Folder follows from what a member is known by, StaffCode, and nothing answers it in the database*");
    }

    [Fact]
    public async Task A_permission_or_a_role_with_a_quote_or_a_brace_in_it_stays_what_it_is()
    {
        using var filing = await PostgresFiling.CreateAsync(postgres, FilingPostgres.Template.Plain, seed: false);
        var odd = MembershipRules.Declare(
            typeof(Document),
            "documents",
            [new("documents.it's {here}"), new("documents.view"), new("documents.{seen}'s", EveryMember: true), new("documents.owner's {own}", InRoles: false)],
            Roles.Fixed([new("o'brien {night}", ["documents.it's {here}", "documents.view"]), new("owner", ["documents.view"])]),
            systemScopes: []);
        await filing.ExecuteAsync(FilingPostgres.AccessScript([new MembershipRowAccessContribution<DocumentShare>(odd)], rules: [FilingPostgres.Rules[0]]));

        // A document shared in that role, saved by the application's own work.
        var owner = UserId.CreateSequential();
        var reader = UserId.CreateSequential();
        var id = DocumentId.CreateSequential();
        await filing.Services.AsAsync(CurrentUserSnapshot.System, async provider =>
        {
            var document = new Document(id, "Night shift", owner, DateTimeOffset.UtcNow.AddDays(-2));
            document.ShareWith(reader, new NamedRole("o'brien {night}"), MemberPeriod.Open(DateTimeOffset.UtcNow.AddDays(-1)), DateTimeOffset.UtcNow, by: owner);
            provider.GetRequiredService<FilingContext>().Documents.Add(document);
            await provider.GetRequiredService<FilingContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await using var session = await filing.SessionAsync(TestCallers.User(reader));
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it's {here}")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.{seen}'s")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it''s {here}")).Should().BeEmpty();
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_as_member_with($1) AS id", "documents.view")).Should().Equal(id.Value);
        (await session.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner's {own}")).Should().BeEmpty("no role gives it, and the reader does not own the document");

        // And the permissions of the resource, which its owner holds by owning it: one no role gives, and one a role
        // called owner does not list, since a role of that name is a role like any other.
        await using var asOwner = await filing.SessionAsync(TestCallers.User(owner));
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner's {own}")).Should().Equal(id.Value);
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.it's {here}")).Should().Equal(id.Value);
        (await asOwner.ListAsync<Guid>("SELECT id FROM filing.documents_where_i_hold($1) AS id", "documents.owner''s {own}")).Should().BeEmpty();
    }

    [Fact]
    public void Each_of_the_three_things_rules_say_is_written_by_itself()
    {
        using var depot = PostgresDepot.Model();

        // A crate's rules as they are: the depot answers who the caller is, which roles give a permission, and where a permission is held.
        var crates = new MembershipRowAccessContribution<CratePorter>(CrateMembership.Rules).Contribute(depot, Export)!.Functions;
        crates.Select(function => (function.Name, function.Parameters, function.Returns)).Should().Equal(
            ("crates_as_member", "", "SETOF uuid"),
            ("crates_as_member_with", "text", "SETOF uuid"),
            ("crates_i_see", "", "SETOF uuid"),
            ("crates_where_i_hold", "text", "SETOF uuid"),
            ("crates_fellow_members", "", "SETOF uuid"));
        crates.Should().OnlyContain(function => function.SecurityDefiner && function.GrantTo!.SequenceEqual(new[] { RowAccessRoles.User, RowAccessRoles.SystemIn }));

        // Asked by logical name, a text the script fills in: nothing here refers to what defines them.
        crates[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:depot/caller_porter}()) AND ");
        crates[1].Body.Should().Contain("h.\"RoleId\" IN (SELECT giving.role FROM {fn:depot/roles_with_permission}($1) AS giving(role))");
        crates[2].Body.Should().Contain("UNION\nSELECT r.\"Id\" FROM \"depot\".\"Crates\" r WHERE r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}('crates.see') AS reached(place))");
        crates[3].Body.Should().Contain("r.\"BayId\" IN (SELECT reached.place FROM {fn:depot/bays_where_i_hold}($1) AS reached(place))");

        // Members that are users, by their own id, with the depot's roles and no reach from above: only the roles are asked.
        var users = new MembershipRowAccessContribution<CratePorter>(Kept("crates", MemberSource.CallerId, reached: false)).Contribute(depot, Export)!.Functions;
        users[0].Body.Should().Contain("m.\"MemberId\" = {caller:uid} AND ").And.NotContain("{fn:");
        users[1].Body.Should().Contain("{fn:depot/roles_with_permission}($1)");
        users[2].Body.Should().NotContain("reached.place", "a crate that nothing above reaches is seen by its members and its owner alone")
            .And.Contain("UNION\nSELECT r.\"Id\" FROM \"depot\".\"Crates\" r WHERE r.\"OwnerId\" = {caller:uid}", "its owner sees it by the owner column, whether or not its member row is there");
        users[3].Body.Should().NotContain("reached.place");

        // What a role can give stands in front of what the host answers, as the rules say it.
        string Cut(OtherPermissions? otherPermissions, IReadOnlyList<ResourcePermission>? permissions = null)
            => new MembershipRowAccessContribution<CratePorter>(Kept("crates", MemberSource.CallerId, reached: false, otherPermissions: otherPermissions, noOtherPermissions: otherPermissions is null, permissions: permissions)).Contribute(depot, Export)!.Functions[1].Body;
        Cut(null).Should().Contain("\n  AND $1 IN ('crates.see', 'crates.pack', 'crates.weigh', 'crates.scrap')\n  AND EXISTS (", "the permissions a role can give, and nothing else");
        Cut(OtherPermissions.AllBut("depot.manage", "crates.it's")).Should().Contain("\n  AND $1 NOT IN ('crates.move', 'depot.manage', 'crates.it''s')\n  AND EXISTS (");
        Cut(OtherPermissions.All).Should().Contain("\n  AND $1 NOT IN ('crates.move')\n  AND EXISTS (");
        Cut(OtherPermissions.All, [new("crates.see", EveryMember: true)]).Should().NotContain("$1 NOT IN").And.Contain("{fn:depot/roles_with_permission}($1)");
        Cut(null, [new("crates.see", EveryMember: true, InRoles: false)]).Should().Contain("\n  AND false\n", "a role gives a member nothing where the rules let it give nothing");

        // A pallet: the depot answers who the caller is, and the roles are in code.
        var pallets = new MembershipRowAccessContribution<PalletPorter>(PalletMembership.Rules).Contribute(depot, Export)!.Functions;
        pallets[0].Body.Should().Contain("m.\"MemberId\" = (SELECT {fn:depot/caller_porter}()) AND ");
        pallets[1].Body.Should().Contain("(VALUES ('loader', 'pallets.see'), ('loader', 'pallets.load'), ('checker', 'pallets.see')) AS gives(role, permission)").And.NotContain("roles_with_permission");
        pallets[3].Body.Should().Contain("$1 IN ('pallets.see', 'pallets.load', 'pallets.strap') AND r.\"OwnerId\" = (SELECT {fn:depot/caller_porter}())");

        // Roles in code, and reach from above, for members that are users: a document that is reached by whoever holds
        // the permission over its owner, where the document marks it hangs, [At].
        using var managed = new ManagedContext(new DbContextOptionsBuilder<ManagedContext>().UseNpgsql("Host=model-only").Options);
        var above = MembershipRules.Declare(
            typeof(Document),
            "documents",
            [new(DocumentPermissions.View, EveryMember: true), new(DocumentPermissions.Edit)],
            Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])]),
            reachFromAbove: new DatabaseFunction("staff", "reports_where_i_hold"));
        var documents = new MembershipRowAccessContribution<DocumentShare>(above).Contribute(managed, Export)!.Functions;
        documents[1].Body.Should().Contain("(VALUES ('contributor', 'documents.view'), ('contributor', 'documents.edit')) AS gives(role, permission)");
        documents[2].Body.Should().EndWith("UNION\nSELECT r.\"Id\" FROM \"public\".\"Document\" r WHERE r.\"OwnerId\" IN (SELECT reached.place FROM {fn:staff/reports_where_i_hold}('documents.view') AS reached(place))");
        documents[3].Body.Should().EndWith("UNION\nSELECT r.\"Id\" FROM \"public\".\"Document\" r WHERE $1 IS NOT NULL AND r.\"OwnerId\" IN (SELECT reached.place FROM {fn:staff/reports_where_i_hold}($1) AS reached(place))");

        // A permission marked FromAbove = false is held from above by nobody.
        var notFromAbove = MembershipRules.Declare(
            typeof(Document),
            "documents",
            [new(DocumentPermissions.View, EveryMember: true), new(DocumentPermissions.Edit, FromAbove: false)],
            Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])]),
            reachFromAbove: new DatabaseFunction("staff", "reports_where_i_hold"));
        new MembershipRowAccessContribution<DocumentShare>(notFromAbove).Contribute(managed, Export)!.Functions[3].Body
            .Should().EndWith("WHERE $1 IS NOT NULL AND $1 NOT IN ('documents.edit') AND r.\"OwnerId\" IN (SELECT reached.place FROM {fn:staff/reports_where_i_hold}($1) AS reached(place))");
    }

    [Fact]
    public void The_fingerprint_covers_what_the_functions_are_written_from()
    {
        using var filing = FilingPostgres.Model();
        using var depot = PostgresDepot.Model();
        static string MarkerOf(IRowAccessContribution contribution, DbContext context, RowAccessExport? export = null)
            => contribution.Contribute(context, export ?? Export)!.Functions[0].Body.Split('\n')[0];

        // What the host answers is part of it: another function of its own, or another list of what a role gives, is
        // another fingerprint.
        var crates = MarkerOf(new MembershipRowAccessContribution<CratePorter>(CrateMembership.Rules), depot);
        crates.Should().MatchRegex("^-- Membership of crates \\(Crate\\), in form [0-9]+, written from the rules [0-9a-f]{64}$");
        MembershipRules[] others =
        [
            Kept("crates", MemberSource.Resolved(Depot("acting_porter"))),
            Kept("crates", roles: Depot("roles_given")),
            Kept("crates", above: Depot("bays_held")),
            Kept("crates", reached: false),
            Kept("crates", otherPermissions: OtherPermissions.All),
            Kept("crates", otherPermissions: OtherPermissions.AllBut(DepotPermissions.ManageDepot, DepotPermissions.PrintLabels)),
        ];
        var markers = others.Select(rules => MarkerOf(new MembershipRowAccessContribution<CratePorter>(rules), depot)).Append(crates).ToList();
        markers.Should().OnlyHaveUniqueItems();
        MarkerOf(new MembershipRowAccessContribution<CratePorter>(Kept("crates")), depot).Should().Be(crates, "rules that say the same are written the same");

        // The permissions the lock is written from, and the scopes whose work is the application's own, are part of it
        // as well: a database locked by another permission, or answering another scope, is not the one the rules say.
        static MembershipRules Documents(bool membersLocked = false, bool ownerLocked = false, string[]? scopes = null)
            => MembershipRules.Declare(
                typeof(Document),
                "documents",
                [new(DocumentPermissions.View), new(DocumentPermissions.Edit), new(DocumentPermissions.Share, ManagesMembers: membersLocked, NamesOwner: ownerLocked)],
                Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit])]),
                systemScopes: scopes ?? []);
        string[] documents =
        [
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents()), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(membersLocked: true)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(ownerLocked: true)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(membersLocked: true, ownerLocked: true)), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing"])), filing),
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing", "archive"])), filing),

            // The scopes are compared with the role scoped work runs as, so that role's name is part of it where a scope is named.
            MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents(scopes: ["filing"])), filing, ExportWith(roles: FilingPostgres.RoleNames with { SystemIn = "filing_worker" })),
        ];
        documents.Should().OnlyHaveUniqueItems();
        MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents()), filing, ExportWith(roles: FilingPostgres.RoleNames with { SystemIn = "filing_worker" }))
            .Should().Be(documents[0], "rules that let no scope through are written without that role");

        // Who may execute the functions is no part of it: a database whose grants changed holds the same functions.
        MarkerOf(new MembershipRowAccessContribution<DocumentShare>(Documents()), filing, ExportWith(rules: [])).Should().Be(documents[0]);

        // The trigger of the lock says the same first line as the functions: one thing to hold a database to.
        var folders = new MembershipRowAccessContribution<FolderMember>(FolderMembership.Rules).Contribute(filing, Export)!;
        folders.Statements[0].Should().Contain("\n    " + folders.Functions[0].Body.Split('\n')[0] + "\n");
    }

    /// <summary>
    /// The form the SQL is written in, as its first line says it, and a hash of everything that is written for four
    /// sets of rules said in the test itself, one for each way rules can be said, with the lines that name the rules
    /// taken out. Kept together: the form is what tells the start-up check that a database holds what an earlier
    /// version of the package wrote, so it goes up whenever what is written changes.
    /// </summary>
    private static readonly (string SqlForm, string Written) KeptTogether = ("8", "eb4be28c4845154d1372eedcbd10e10922ea401fbe9ef9ac800aa9b757a772d7");

    [Fact]
    public void What_is_written_for_the_same_rules_changes_only_together_with_the_form_it_is_written_in()
    {
        using var filing = FilingPostgres.Model();
        using var depot = PostgresDepot.Model();
        using var gardens = PostgresGarden.Model();

        // Rules said here, and not the hosts' own, so that only a change of what the package writes moves the hash.
        (IRowAccessContribution Contribution, DbContext Context)[] hosts =
        [
            // Users by their id, roles in code, a permission every member holds, one permission for both locks.
            (new MembershipRowAccessContribution<DocumentShare>(MembershipRules.Declare(
                typeof(Document),
                "documents",
                [new("documents.view", EveryMember: true), new("documents.edit"), new("documents.share", ManagesMembers: true, NamesOwner: true)],
                Roles.Fixed([new("contributor", ["documents.view", "documents.edit"]), new("onlooker", ["documents.view"])]),
                systemScopes: [])), filing),

            // Members by a claim, a permission no role gives, a permission for each lock, and a scope of the application's own.
            (new MembershipRowAccessContribution<FolderMember>(MembershipRules.Declare(
                typeof(Folder),
                "folders",
                [new("folders.read"), new("folders.file"), new("folders.staff", ManagesMembers: true), new("folders.shred", InRoles: false), new("folders.hand-over", InRoles: false, NamesOwner: true)],
                Roles.Fixed([new("keeper", ["folders.read", "folders.file", "folders.staff"]), new("visitor", ["folders.read"])]),
                members: MemberSource.Claim("app_metadata.staff"),
                systemScopes: ["filing"])), filing),

            // Members the host resolves, roles kept elsewhere, reached from above, no lock.
            (new MembershipRowAccessContribution<CratePorter>(Kept("crates")), depot),

            // Roles a customer makes, a permission that manages the members alone, which then holds a role given to one the caller sees.
            (new MembershipRowAccessContribution<PlotGardener>(MembershipRules.Declare(
                typeof(Plot),
                "plots",
                [new("plots.see", EveryMember: true), new("plots.plant"), new("plots.fence", ManagesMembers: true)],
                Roles.Custom(starters: [new("tender", ["plots.see", "plots.plant"])]),
                systemScopes: [])), gardens),
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

                // Without the lines that name the rules and the form: everything else is what a database holds.
                written.Append(function.Name).Append('(').Append(function.Parameters).Append(") ").Append(function.Returns).Append(' ').Append(function.SecurityDefiner).Append('\n')
                    .Append(string.Join('\n', lines.Where(line => !line.StartsWith("-- Membership of ", StringComparison.Ordinal)))).Append('\n');
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

    /// <summary>Rules of <typeparamref name="TResource"/> under another name: one permission a role in code gives.</summary>
    private static MembershipRules Rules<TResource>(string name, MemberSource? members = null)
        => MembershipRules.Declare(typeof(TResource), name, [new(name + ".read")], Roles.Fixed([new("reader", [name + ".read"])]), members: members);

    /// <summary>
    /// Rules for a crate: the crate's permissions, the depot's roles, the depot's porters and its bays, as the crates'
    /// own rules say them, each of which a test may say otherwise.
    /// </summary>
    private static MembershipRules Kept(
        string name,
        MemberSource? members = null,
        DatabaseFunction? roles = null,
        bool rolesFunction = true,
        ReachFromAbove? above = null,
        bool reached = true,
        OtherPermissions? otherPermissions = null,
        bool noOtherPermissions = false,
        IReadOnlyList<ResourcePermission>? permissions = null)
        => MembershipRules.Declare(
            typeof(Crate),
            name,
            permissions ?? CrateMembership.Rules.Permissions,
            Roles.External(rolesFunction ? roles ?? Depot("roles_with_permission") : null, noOtherPermissions ? null : otherPermissions ?? OtherPermissions.AllBut(DepotPermissions.ManageDepot)),
            members: members ?? MemberSource.Resolved(Depot("caller_porter")),
            reachFromAbove: reached ? above ?? Depot("bays_where_i_hold") : null,
            systemScopes: CrateMembership.Rules.SystemScopes,
            codes: CrateMembership.Codes);

    /// <summary>A context that keeps the documents where their owner is: who holds a permission over a document's owner reaches the document.</summary>
    private sealed class ManagedContext(DbContextOptions<ManagedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Document>().HasMembers(row => row.Shares);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context of another module, which maps none of these resources.</summary>
    private sealed class OtherContext(DbContextOptions<OtherContext> options) : DbContext(options);

    /// <summary>A context that keeps the documents under a permission of two parts, said before their members are mapped.</summary>
    private sealed class TwoPartKeyContext(DbContextOptions<TwoPartKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Document>(document =>
            {
                document.HasKey(nameof(Document.Title), nameof(Document.Id));
                document.HasMembers(row => row.Shares);
            });

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
