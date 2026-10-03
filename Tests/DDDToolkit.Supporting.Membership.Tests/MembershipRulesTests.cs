using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The rules of access through a resource's members are one declaration, checked when it is made: what it
/// defaults to, what it answers about a key and a role, and what it refuses to hold together. They say who
/// holds which key, and nothing about who may change the members.
/// </summary>
public sealed class MembershipRulesTests
{
    private const string View = "notes.view";

    private const string Edit = "notes.edit";

    private const string Sharing = "notes.share";

    private static readonly string[] Keys = [View, Edit, Sharing];

    private static readonly DeclaredRole Drafter = new("drafter", [View, Edit]);

    private static readonly DeclaredRole Bystander = new("bystander", [View]);

    // ---------------------------------------------------------------- defaults

    [Fact]
    public void A_name_the_keys_and_roles_are_enough_and_the_rest_has_a_default()
    {
        var rules = new MembershipRules("notes", Keys, roles: [Drafter, Bystander]);

        rules.Name.Should().Be("notes");
        rules.Keys.Should().Equal(View, Edit, Sharing);
        rules.Members.Should().BeSameAs(MemberSource.CallerId, "members are users unless the rules say otherwise");
        rules.SeeKey.Should().BeNull("being a member gives no key unless the rules name one");
        rules.OwnerRole.Should().Be(MembershipRules.DefaultOwnerRole).And.Be("owner");
        rules.Codes.All.Should().Equal(MembershipCodes.Under("notes").All, "a resource refuses under its own name");
        rules.Functions.Should().Be(new MembershipFunctions("notes_as_member", "notes_as_member_with", "notes_i_see", "notes_where_i_hold"));
        rules.GrantTo.Should().Equal(RowAccessRoles.User);
        rules.MemberKeys.Excepts.Should().BeFalse();
        rules.MemberKeys.Keys.Should().Equal([View, Edit, Sharing], "a member's role gives every key the roles list");
    }

    [Fact]
    public void The_rules_decide_nothing_about_who_may_change_the_members()
    {
        // Which key a command requires is the application's to choose, on the command. Where the roles are kept
        // and whether something above reaches the resource say who holds a key, like the rest: not who gives one.
        // Two entries name the keys the application's commands require to change the members and the owner:
        // they are what a database that checks every row is told, so it holds a caller that reaches it past
        // the application to the same keys. Nothing else is said, and nothing here names who may.
        typeof(MembershipRules).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
            "Name", "Members", "RolesKept", "RolesKeptElsewhere", "Above", "Keys", "SeeKey", "OwnerRole", "MemberKeys", "Roles", "Codes", "Functions", "GrantTo",
            "ChangeMembersKey", "ChangeOwnerKey", "SystemScopes");
        typeof(MembershipRules).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name).Should().Equal(
            "name", "keys", "roles", "members", "seeKey", "ownerRole", "memberKeys", "codes", "functions", "grantTo", "rolesKeptElsewhere", "above", "rolesKept",
            "changeMembersKey", "changeOwnerKey", "systemScopes");

        // The member list, which is what changes the members, is made without the rules and takes no key: it
        // could not gate by one if it wanted to.
        typeof(MemberList<,,,>).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.Name)
            .Should().NotContain(nameof(MembershipRules));
    }

    [Fact]
    public void The_owners_role_is_added_with_every_key_of_the_resource_when_nobody_declares_one()
    {
        var rules = new MembershipRules("notes", [View, Edit, Sharing, "notes.see"], roles: [Drafter, Bystander], seeKey: "notes.see");

        rules.Roles.Select(role => role.Name).Should().Equal("drafter", "bystander", "owner");
        rules.Roles[^1].Keys.Should().Equal(View, Edit, Sharing, "notes.see");
        rules.KeysOf(new NamedRole("owner")).Should().Equal(View, Edit, Sharing, "notes.see");
        rules.RolesWith(Sharing).Should().Equal([new NamedRole("owner")], "no declared role lists it");
    }

    [Fact]
    public void An_owners_role_the_rules_declare_is_used_as_declared()
    {
        // Named "owner" by the host itself: nothing is added, and it gives what the host says.
        var byName = new MembershipRules("notes", Keys, roles: [Drafter, new("owner", [View, Sharing])]);
        byName.Roles.Select(role => role.Name).Should().Equal("drafter", "owner");
        byName.KeysOf(new NamedRole("owner")).Should().Equal(View, Sharing);

        // Or another role of the host's.
        var chosen = new MembershipRules("notes", Keys, roles: [Drafter, Bystander], ownerRole: "drafter");
        chosen.OwnerRole.Should().Be("drafter");
        chosen.Roles.Select(role => role.Name).Should().Equal("drafter", "bystander");
        chosen.RolesWith(Sharing).Should().BeEmpty("the rules add nothing to a declared role: whoever else holds the owner's role holds what it lists");
        chosen.OwnerHolds(Sharing).Should().BeTrue("and the owner holds the key all the same, by owning the resource");
    }

    [Fact]
    public void Every_entry_said_is_kept_as_said()
    {
        var rules = FolderMembership.Rules;

        rules.Name.Should().Be("folders");
        rules.Keys.Should().Equal(FolderKeys.Read, FolderKeys.File, FolderKeys.Staff, FolderKeys.Shred, FolderKeys.HandOver);
        (rules.Members.Kind, rules.Members.ClaimPath).Should().Be((MemberSourceKind.Claim, "app_metadata.staff"));
        rules.OwnerRole.Should().Be("keeper");
        rules.Roles.Select(role => role.Name).Should().Equal("keeper", "clerk", "visitor");
        rules.MemberKeys.Excepts.Should().BeTrue();
        rules.MemberKeys.Keys.Should().Equal(FolderKeys.Shred);
        rules.Codes.Should().BeSameAs(FolderRefusals.Membership);
        rules.Functions.Should().Be(new MembershipFunctions("folder_ids_staffed", "folder_ids_staffed_with", "folder_ids_seen", "folder_ids_held"));
        rules.GrantTo.Should().Equal(RowAccessRoles.User, "@token:archivist");
    }

    // ---------------------------------------------------------------- what the rules answer

    [Fact]
    public void The_owner_holds_every_key_of_the_resource_and_no_other()
    {
        var documents = DocumentMembership.Rules;
        var folders = FolderMembership.Rules;

        foreach (var key in documents.Keys)
        {
            documents.OwnerHolds(key).Should().BeTrue(key + " is a key of a document");
        }

        foreach (var key in folders.Keys)
        {
            folders.OwnerHolds(key).Should().BeTrue(key + " is a key of a folder");
        }

        documents.OwnerHolds(FolderKeys.Read).Should().BeFalse("a key of another resource is none of a document's");
        documents.OwnerHolds("documents.View").Should().BeFalse("a key is compared as it is written");
        folders.OwnerHolds("inspections.record").Should().BeFalse("what a list of exceptions lets a role give is not thereby a key of the resource");
    }

    [Fact]
    public void A_key_of_the_resource_that_no_role_gives_is_the_owners_alone()
    {
        var folders = FolderMembership.Rules;

        // No role lists it.
        folders.OwnerHolds(FolderKeys.HandOver).Should().BeTrue();
        folders.RolesWith(FolderKeys.HandOver).Should().BeEmpty();

        // The roles list it, and the list of what a member's role gives leaves it out: the owner holds it by
        // owning the folder, which that list has no say over.
        folders.OwnerHolds(FolderKeys.Shred).Should().BeTrue();
        folders.MembersHold(FolderKeys.Shred).Should().BeFalse();
        folders.RolesWith(FolderKeys.Shred).Should().BeEmpty();
        folders.KeysOf(FolderMembership.Keeper).Should().NotContain(FolderKeys.Shred, "the owner's role is a member's role like any other");
    }

    [Fact]
    public void Being_a_member_gives_the_see_key_and_nothing_else()
    {
        var rules = DocumentMembership.Rules;

        rules.MembershipGives(DocumentKeys.View).Should().BeTrue();
        rules.MembershipGives(DocumentKeys.Edit).Should().BeFalse();
        rules.MembershipGives(DocumentKeys.Share).Should().BeFalse();
        rules.MembersHold(DocumentKeys.View).Should().BeTrue();

        // Without a see key, being a member gives no key by itself.
        FolderMembership.Rules.MembershipGives(FolderKeys.Read).Should().BeFalse();
        FolderMembership.Rules.MembersHold(FolderKeys.Read).Should().BeTrue("a role still gives it");

        // The see key is held by a member even when the list of what a role gives leaves it out.
        var narrow = new MembershipRules("notes", Keys, roles: [Drafter], seeKey: View, memberKeys: MemberKeys.Only(Edit));
        narrow.MembersHold(View).Should().BeTrue();
        narrow.RolesWith(View).Should().BeEmpty("no role gives it: membership does");
        narrow.KeysOf(new NamedRole("drafter")).Should().Equal(Edit);
    }

    [Fact]
    public void A_members_role_gives_only_what_the_member_keys_allow()
    {
        var folders = FolderMembership.Rules;

        // Every key but the excepted one: the roles list it, and it gives nothing with them.
        folders.MembersHold(FolderKeys.File).Should().BeTrue();
        folders.MembersHold(FolderKeys.Shred).Should().BeFalse();
        folders.RolesWith(FolderKeys.Shred).Should().BeEmpty();
        folders.KeysOf(FolderMembership.Clerk).Should().Equal(FolderKeys.Read, FolderKeys.File);
        folders.KeysOf(FolderMembership.Keeper).Should().Equal(FolderKeys.Read, FolderKeys.File, FolderKeys.Staff);
        folders.MembersHold("inspections.record").Should().BeTrue("a list of exceptions lets every other key through, another module's too");

        // A list of what is given: nothing else is.
        var only = new MembershipRules("notes", Keys, roles: [Drafter, Bystander], memberKeys: MemberKeys.Only(View));
        only.MembersHold(View).Should().BeTrue();
        only.MembersHold(Edit).Should().BeFalse();
        only.RolesWith(Edit).Should().BeEmpty();
        only.KeysOf(new NamedRole("drafter")).Should().Equal(View);
        only.KeysOf(new NamedRole("owner")).Should().Equal([View], "the owner's role is a member's role like any other");
        only.OwnerHolds(Edit).Should().BeTrue("what the owner holds by owning the resource is not a member's role's to give or keep");
        only.MembersHold("inspections.record").Should().BeFalse();
    }

    [Fact]
    public void The_roles_that_give_a_key_are_the_declared_ones_in_the_order_they_are_declared()
    {
        var rules = DocumentMembership.Rules;

        rules.RolesWith(DocumentKeys.View).Should().Equal(DocumentMembership.Contributor, DocumentMembership.Onlooker, DocumentMembership.Owner);
        rules.RolesWith(DocumentKeys.Edit).Should().Equal(DocumentMembership.Contributor, DocumentMembership.Owner);
        rules.RolesWith(DocumentKeys.Share).Should().Equal(DocumentMembership.Owner);
        rules.RolesWith("documents.print").Should().BeEmpty("no role lists it");

        rules.Knows(DocumentMembership.Onlooker).Should().BeTrue();
        rules.Knows(new NamedRole("Onlooker")).Should().BeFalse("a role's name is compared as it is written");
        rules.Knows(default).Should().BeFalse();
        rules.KeysOf(new NamedRole("nobody")).Should().BeEmpty();
        rules.KeysOf(default).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- rules that do not hold together

    [Theory]
    [InlineData("")]
    [InlineData("Notes")]
    [InlineData("my notes")]
    [InlineData("9notes")]
    [InlineData("notes.")]
    [InlineData("notes_archive")]
    public void A_name_that_cannot_start_a_code_is_refused(string name)
    {
        FluentActions.Invoking(() => new MembershipRules(name, Keys, roles: [Drafter]))
            .Should().Throw<ArgumentException>().WithParameterName("name").WithMessage("*is not a name for a resource's rules*");
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("case-files")]
    [InlineData("sales.notes")]
    [InlineData("notes2")]
    public void A_name_names_the_codes_and_the_functions(string name)
    {
        var rules = new MembershipRules(name, Keys, roles: [Drafter]);

        rules.Codes[MembershipRefusals.NotFound].Should().Be(name + ".not-found");
        rules.Functions.Seen.Should().Be(name.Replace('.', '_').Replace('-', '_') + "_i_see", "a function's name has neither a dot nor a dash");
        rules.Keys.Should().Equal(Keys, "a name names nothing a key is called by: the keys are the application's own");
    }

    [Fact]
    public void The_keys_of_a_resource_are_stated_each_once_and_cover_what_its_roles_and_its_membership_give()
    {
        static Action Making(Func<MembershipRules> make) => () => make();

        Making(() => new MembershipRules("notes", null!, roles: [Drafter]))
            .Should().Throw<ArgumentNullException>().WithParameterName("keys");
        Making(() => new MembershipRules("notes", [], roles: [Drafter]))
            .Should().Throw<ArgumentException>().WithParameterName("keys").WithMessage("*states the keys it is asked about, at least one*");
        Making(() => new MembershipRules("notes", [View, " "], roles: [Bystander]))
            .Should().Throw<ArgumentException>().WithParameterName("keys").WithMessage("*a key is not blank*");
        Making(() => new MembershipRules("notes", [View, Edit, View], roles: [Bystander]))
            .Should().Throw<ArgumentException>().WithParameterName("keys").WithMessage("*list 'notes.view' twice*");

        // A key a member would hold and the owner would not: a typo, or a key that was never stated.
        Making(() => new MembershipRules("notes", [View], roles: [Drafter]))
            .Should().Throw<ArgumentException>().WithParameterName("roles")
            .WithMessage("*'drafter' lists 'notes.edit', which is not one of the resource's keys*Add it to the keys, or take it out of the role*");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], seeKey: "notes.see"))
            .Should().Throw<ArgumentException>().WithParameterName("seeKey")
            .WithMessage("*'notes.see', the key that being a member gives, is not one of the resource's keys*");
    }

    [Fact]
    public void Rules_that_do_not_hold_together_are_refused_when_they_are_made()
    {
        static Action Making(Func<MembershipRules> make) => () => make();

        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], seeKey: ""))
            .Should().Throw<ArgumentException>().WithParameterName("seeKey");

        // Members that are users hold roles the rules declare, so there is at least one.
        Making(() => new MembershipRules("notes", Keys))
            .Should().Throw<ArgumentException>().WithParameterName("roles").WithMessage("*needs at least one role*");
        Making(() => new MembershipRules("notes", Keys, roles: []))
            .Should().Throw<ArgumentException>().WithParameterName("roles");

        Making(() => new MembershipRules("notes", Keys, roles: [Drafter, new("drafter", [View])]))
            .Should().Throw<ArgumentException>().WithMessage("*'drafter' is declared twice*");
        Making(() => new MembershipRules("notes", Keys, roles: [new(" drafter", [View])]))
            .Should().Throw<ArgumentException>().WithMessage("*A role's name is 1 to 64 characters*");
        Making(() => new MembershipRules("notes", Keys, roles: [new(new string('r', 65), [View])]))
            .Should().Throw<ArgumentException>().WithMessage("*A role's name is 1 to 64 characters*");
        Making(() => new MembershipRules("notes", Keys, roles: [new("drafter", [View, " "])]))
            .Should().Throw<ArgumentException>().WithMessage("*'drafter' lists a blank key*");
        Making(() => new MembershipRules("notes", Keys, roles: [new("drafter", [View, View])]))
            .Should().Throw<ArgumentException>().WithMessage("*'drafter' lists a key twice*");

        // An owner's role that is named is one of the roles: a typo is not answered with a role nobody declared.
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], ownerRole: "dratfer"))
            .Should().Throw<ArgumentException>().WithParameterName("ownerRole").WithMessage("*'dratfer' is not one of the roles*");

        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], functions: new("Notes", "b", "c", "d")))
            .Should().Throw<ArgumentException>().WithParameterName("functions").WithMessage("*'Notes' is not a name for a function*");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], functions: new("a", "b", "c", "drop table x")))
            .Should().Throw<ArgumentException>().WithParameterName("functions");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], functions: new("a", "b", "c", new string('f', 64))))
            .Should().Throw<ArgumentException>().WithParameterName("functions");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], functions: new("a", "b", "c", "a")))
            .Should().Throw<ArgumentException>().WithParameterName("functions").WithMessage("*each has a name of its own*");

        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], grantTo: []))
            .Should().Throw<ArgumentException>().WithParameterName("grantTo");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], grantTo: [RowAccessRoles.User, ""]))
            .Should().Throw<ArgumentException>().WithParameterName("grantTo");
    }

    [Fact]
    public void Rules_are_data_that_never_changes_after_it_is_made()
    {
        List<string> stated = [View, Edit, Sharing];
        List<string> keys = [View];
        List<DeclaredRole> roles = [Drafter, new("bystander", keys)];
        List<string> grantTo = [RowAccessRoles.User];
        var rules = new MembershipRules("notes", stated, roles: roles, grantTo: grantTo);

        stated.Add("notes.print");
        roles.Add(new("auditor", [View]));
        grantTo.Add(RowAccessRoles.Anonymous);
        keys.Add(Sharing);

        rules.Keys.Should().Equal(View, Edit, Sharing);
        rules.OwnerHolds("notes.print").Should().BeFalse("a key added to the list afterwards is none of the resource's");
        rules.Roles.Select(role => role.Name).Should().Equal("drafter", "bystander", "owner");
        rules.GrantTo.Should().Equal(RowAccessRoles.User);

        // The keys of a role are the ones it was declared with: a list changed afterwards gives nobody a key.
        rules.KeysOf(new NamedRole("bystander")).Should().Equal(View);
        rules.RolesWith(Sharing).Should().Equal(new NamedRole("owner"));
        rules.Roles.Single(role => role.Name == "bystander").Keys.Should().Equal(View);
        typeof(MembershipRules).GetProperties().Should().OnlyContain(property => property.SetMethod == null, "everything that reads the rules reads the same thing");
    }

    // ---------------------------------------------------------------- where members come from

    [Fact]
    public void A_claim_is_named_by_its_path()
    {
        var claim = MemberSource.Claim("app_metadata.staff");

        (claim.Kind, claim.ClaimPath).Should().Be((MemberSourceKind.Claim, "app_metadata.staff"));
        claim.Should().Be(MemberSource.Claim("app_metadata.staff"), "a source is data: two that say the same are equal");
        (MemberSource.CallerId.Kind, MemberSource.CallerId.ClaimPath).Should().Be((MemberSourceKind.CallerId, null));
        (MemberSource.CallerId.Function, claim.Function).Should().Be((null, null), "a caller's own id and a claim are read from the caller, by no function of anybody's");

        // The path goes into what is asked of the database as well, so it is names and dots and nothing else.
        foreach (var path in new[] { "", " ", "app_metadata.", ".staff", "app metadata", "staff'; --", "9lives", "a..b" })
        {
            FluentActions.Invoking(() => MemberSource.Claim(path)).Should().Throw<ArgumentException>("'" + path + "' is not a path");
        }
    }

    [Fact]
    public void A_member_the_application_resolves_is_asked_of_the_application_and_names_its_function_for_a_database_that_answers()
    {
        var resolved = MemberSource.Resolved("depot/caller_porter");

        (resolved.Kind, resolved.ClaimPath, resolved.Function).Should().Be((MemberSourceKind.Resolved, null, "depot/caller_porter"));
        resolved.Should().Be(MemberSource.Resolved("depot/caller_porter"), "a source is data: two that say the same are equal");
        resolved.Should().NotBe(MemberSource.Resolved("depot/acting_porter"));

        // An application whose database answers nothing names no function, and says the rest all the same.
        (MemberSource.Resolved().Kind, MemberSource.Resolved().Function).Should().Be((MemberSourceKind.Resolved, null));
    }

    [Theory]
    [InlineData("depot/caller_porter")]
    [InlineData("second-depot-2/caller_Porter_2")]
    [InlineData("a/_b")]
    public void A_function_of_the_applications_is_named_by_its_owner_and_its_name(string function)
    {
        // The same name, whichever of the three it answers: a text that names a function wherever it is defined.
        MemberSource.Resolved(function).Function.Should().Be(function);
        new RolesKeptElsewhere(function).Function.Should().Be(function);
        new ReachFromAbove(function).Function.Should().Be(function);
    }

    [Theory]
    [InlineData("")]
    [InlineData("caller_porter")]
    [InlineData("depot.caller_porter")]
    [InlineData("depot/caller/porter")]
    [InlineData("Depot/caller_porter")]
    [InlineData("-depot/caller_porter")]
    [InlineData("depot-/caller_porter")]
    [InlineData("depot/9lives")]
    [InlineData("depot/caller porter")]
    [InlineData("depot/caller_porter()")]
    [InlineData("depot/x}; DROP TABLE y; --")]
    [InlineData("/caller_porter")]
    [InlineData("depot/")]
    public void What_is_not_a_logical_name_is_refused_when_it_is_said(string function)
    {
        // It goes into what is asked of the database, and a name with its schema would be asked whether or
        // not anything defines it: so an owner and a name, and nothing else.
        FluentActions.Invoking(() => MemberSource.Resolved(function)).Should().Throw<ArgumentException>().WithParameterName("function").WithMessage("*is not the logical name of a function*'owner/name'*");
        FluentActions.Invoking(() => new RolesKeptElsewhere(function)).Should().Throw<ArgumentException>().WithParameterName("function");
        FluentActions.Invoking(() => new ReachFromAbove(function)).Should().Throw<ArgumentException>().WithParameterName("function");
    }

    [Fact]
    public void Who_a_member_is_where_the_roles_come_from_and_what_is_above_are_each_said_by_itself()
    {
        MemberSource[] members = [MemberSource.CallerId, MemberSource.Claim("app_metadata.staff"), MemberSource.Resolved(), MemberSource.Resolved("depot/caller_porter")];
        RolesKeptElsewhere?[] elsewhere = [null, new(), new("depot/roles_with_key")];
        ReachFromAbove?[] above = [null, new(), new("depot/bays_where_i_hold")];

        // Any of the one with any of the others: four by three by three rules, and every one of them holds together.
        foreach (var source in members)
        {
            foreach (var kept in elsewhere)
            {
                foreach (var reach in above)
                {
                    var rules = kept is null
                        ? new MembershipRules("notes", Keys, roles: [Drafter], members: source, seeKey: View, above: reach)
                        : new MembershipRules("notes", [], members: source, seeKey: View, ownerRole: "lead", memberKeys: MemberKeys.AllBut(Sharing), rolesKeptElsewhere: kept, above: reach);

                    (rules.Members, rules.RolesKept, rules.RolesKeptElsewhere, rules.Above).Should().Be((source, false, kept, reach));

                    // What one says changes nothing of what the others say.
                    rules.Roles.Select(role => role.Name).Should().Equal(kept is null ? new[] { "drafter", "owner" } : Array.Empty<string>());
                    rules.MemberKeys.Allows(Edit).Should().BeTrue();
                    rules.MemberKeys.Allows(Sharing).Should().Be(kept is null, "a member's role gives what the rules say, wherever the roles come from");
                }
            }

            // And the third place roles come from, rows kept for the resource, with any member source and with or without reach from above.
            foreach (var reach in above)
            {
                var rules = new MembershipRules("notes", Keys, roles: [Drafter], members: source, seeKey: View, above: reach, rolesKept: true);

                (rules.Members, rules.RolesKept, rules.RolesKeptElsewhere, rules.Above).Should().Be((source, true, null, reach));
                rules.Roles.Select(role => role.Name).Should().Equal(["drafter", "owner"], "the starter roles, with the owner's added as for declared roles");
            }
        }

        // Left unsaid, a resource is what it always was: its members are users, its roles its own, and nothing above reaches it.
        var plain = new MembershipRules("notes", Keys, roles: [Drafter]);
        (plain.Members, plain.RolesKept, plain.RolesKeptElsewhere, plain.Above).Should().Be((MemberSource.CallerId, false, null, null));

        // A resource that is not seen does not exist for a caller, so what is held above would reach nobody
        // without a key that sees: rules that reach from above name one.
        FluentActions.Invoking(() => new MembershipRules("notes", Keys, roles: [Drafter], above: new()))
            .Should().Throw<ArgumentException>().WithParameterName("seeKey").WithMessage("*reached from above name the key that sees it*");
        FluentActions.Invoking(() => new MembershipRules("notes", [], ownerRole: "lead", memberKeys: MemberKeys.AllBut(), rolesKeptElsewhere: new(), above: new("depot/bays_where_i_hold")))
            .Should().Throw<ArgumentException>().WithParameterName("seeKey");
    }

    [Fact]
    public void Roles_kept_elsewhere_are_not_declared_a_second_time_and_the_rules_say_what_cuts_them()
    {
        var rules = new MembershipRules(
            "sites",
            keys: ["sites.close"],
            seeKey: "sites.view",
            ownerRole: "site-lead",
            memberKeys: MemberKeys.AllBut("sites.open", "sites.close"),
            rolesKeptElsewhere: new("works/roles_with_key"));

        rules.Roles.Should().BeEmpty("the roles a member holds are rows somebody else keeps");
        rules.OwnerRole.Should().Be("site-lead", "what the owner's role is found by where the roles are kept");
        rules.Keys.Should().Equal(["sites.close"], "what an owner holds by being the owner, next to what the role for owners gives");
        rules.OwnerHolds("sites.close").Should().BeTrue();
        rules.OwnerHolds("sites.edit").Should().BeFalse("the roles say who holds the rest");
        rules.MembersHold("sites.close").Should().BeFalse();
        rules.MembersHold("sites.open").Should().BeFalse();
        rules.MembersHold("sites.edit").Should().BeTrue();
        rules.MembersHold("inspections.record").Should().BeTrue("a key of another module's is given unless the rules keep it from members");
        rules.MembershipGives("sites.view").Should().BeTrue("the key that being a member gives need not be one the owner holds by owning: the owner is a member");
        rules.RolesWith("sites.edit").Should().BeEmpty("which of the roles give a key is asked where they are kept");
        rules.Knows(new NamedRole("site-lead")).Should().BeFalse("the rules declare no role, and know none by name");
        rules.KeysOf(new NamedRole("site-lead")).Should().BeEmpty();

        // An owner that holds what the role for owners gives, and no more, states no keys.
        new MembershipRules("sites", [], ownerRole: "site-lead", memberKeys: MemberKeys.Only("sites.view"), rolesKeptElsewhere: new())
            .Keys.Should().BeEmpty();

        static Action Making(Func<MembershipRules> make) => () => make();

        // What somebody else keeps is not declared a second time, and what only the rules can say is said.
        Making(() => new MembershipRules("sites", [], roles: [new("lead", ["sites.view"])], ownerRole: "site-lead", memberKeys: MemberKeys.Only("sites.view"), rolesKeptElsewhere: new()))
            .Should().Throw<ArgumentException>().WithParameterName("roles").WithMessage("*the roles are kept elsewhere, and declare roles as well*");
        Making(() => new MembershipRules("sites", [], memberKeys: MemberKeys.Only("sites.view"), rolesKeptElsewhere: new()))
            .Should().Throw<ArgumentException>().WithParameterName("ownerRole").WithMessage("*the rules add no owner's role*");
        Making(() => new MembershipRules("sites", [], ownerRole: "site-lead", rolesKeptElsewhere: new()))
            .Should().Throw<ArgumentException>().WithParameterName("memberKeys").WithMessage("*MemberKeys.Only or MemberKeys.AllBut*");

        // And rules that declare their roles still state their keys, whoever the members are and whatever is above.
        Making(() => new MembershipRules("sites", [], roles: [new("lead", [])], members: MemberSource.Resolved(), seeKey: "sites.view", above: new()))
            .Should().Throw<ArgumentException>().WithParameterName("keys").WithMessage("*states the keys it is asked about, at least one*");
    }

    [Fact]
    public void Roles_kept_for_the_resource_start_from_the_roles_the_rules_declare_and_no_member_holds_one_by_its_name()
    {
        var rules = new MembershipRules(
            "notes",
            keys: [View, Edit, Sharing],
            roles: [Drafter, new("auditor", [View, "audits.read", Sharing])],
            seeKey: "notes.see",
            memberKeys: MemberKeys.AllBut(Sharing),
            rolesKept: true);

        rules.RolesKept.Should().BeTrue();
        rules.RolesKeptElsewhere.Should().BeNull();

        // The roles the rules declare are the starter roles, with the owner's added as for any rules that name none.
        rules.Roles.Select(role => role.Name).Should().Equal("drafter", "auditor", "owner");
        (rules.OwnerRole, rules.Knows(new NamedRole("owner")), rules.Knows(new NamedRole("auditor")), rules.Knows(new NamedRole("clerk"))).Should().Be(("owner", true, true, false));

        // A starter role is a row in the end: its keys are held to what a member's role can give, as any
        // role's are, and not to the keys of the resource. So one may list a key of another module's, and what
        // it is made with is cut by the list of what a member's role gives.
        rules.KeysOf(new NamedRole("auditor")).Should().Equal(View, "audits.read");
        rules.KeysOf(new NamedRole("owner")).Should().Equal([View, Edit], "every key of the resource a member's role can give");

        // No member holds a starter role by its name, so no role the rules know gives a key: the rows say that.
        rules.RolesWith(View).Should().BeEmpty();
        rules.RolesWith(Edit).Should().BeEmpty();

        // The keys stated are what an owner holds by owning, as where the roles are kept elsewhere: they may be
        // none, and the key that sees need not be one of them.
        rules.OwnerHolds(Sharing).Should().BeTrue();
        rules.MembershipGives("notes.see").Should().BeTrue();
        var bare = new MembershipRules("notes", [], memberKeys: MemberKeys.Only(View, Edit), rolesKept: true);
        bare.Keys.Should().BeEmpty();
        bare.Roles.Should().ContainSingle().Which.Should().BeEquivalentTo(new DeclaredRole("owner", []), "nothing has to be there to start from but the owner's role");
        bare.MemberKeys.Keys.Should().Equal([View, Edit], "with no key stated and no starter role, what a role's keys are chosen from is said");

        // Left unsaid, what a role can give is what the starter roles list, the owner's among them.
        new MembershipRules("notes", Keys, roles: [Bystander], rolesKept: true).MemberKeys.Keys.Should().Equal(View, Edit, Sharing);

        // A declared owner's starter role is used as declared.
        var chosen = new MembershipRules("notes", Keys, roles: [Drafter, Bystander], ownerRole: "drafter", rolesKept: true);
        (chosen.OwnerRole, chosen.Roles.Count).Should().Be(("drafter", 2));

        static Action Making(Func<MembershipRules> make) => () => make();

        // Roles are kept in one place: for the resource, or elsewhere.
        Making(() => new MembershipRules("notes", [], ownerRole: "lead", memberKeys: MemberKeys.AllBut(), rolesKeptElsewhere: new(), rolesKept: true))
            .Should().Throw<ArgumentException>().WithParameterName("rolesKept").WithMessage("*kept for the resource, and that they are kept elsewhere*");

        // What holds for a declared role's name holds for a starter role's, and the owner's is one of them.
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter, Drafter], rolesKept: true)).Should().Throw<ArgumentException>().WithParameterName("roles");
        Making(() => new MembershipRules("notes", Keys, roles: [new(" drafter", [View])], rolesKept: true)).Should().Throw<ArgumentException>().WithParameterName("roles");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], ownerRole: "lead", rolesKept: true))
            .Should().Throw<ArgumentException>().WithParameterName("ownerRole").WithMessage("The owner's role 'lead' is not one of the roles.*");

        // Rules that declare the roles a member holds still need one, and say now where else roles can come from.
        Making(() => new MembershipRules("notes", Keys))
            .Should().Throw<ArgumentException>().WithParameterName("roles").WithMessage("*rolesKept: true*rolesKeptElsewhere: new()*");
    }

    [Fact]
    public void Kept_rules_under_which_a_members_role_can_give_nothing_are_refused()
    {
        static Action Making(Func<MembershipRules> make) => () => make();

        // No key stated and no starter role: the owner's role the rules add is made with no key, so what a
        // member's role can give comes out as nothing, and every key a customer chose would be refused.
        Making(() => new MembershipRules("notes", keys: [], rolesKept: true))
            .Should().Throw<ArgumentException>().WithParameterName("memberKeys")
            .WithMessage("Under the rules 'notes' a member's role can give no key at all*memberKeys: MemberKeys.Only(*");

        // Said outright, it is the same slip.
        Making(() => new MembershipRules("notes", Keys, roles: [Bystander], memberKeys: MemberKeys.Only(), rolesKept: true))
            .Should().Throw<ArgumentException>().WithParameterName("memberKeys");

        // A starter role with a key, or the keys of the resource, are enough: the roles' keys are chosen from those.
        new MembershipRules("notes", keys: [], roles: [Bystander], rolesKept: true).MemberKeys.Keys.Should().Equal(View);
        new MembershipRules("notes", Keys, rolesKept: true).MemberKeys.Keys.Should().Equal(View, Edit, Sharing);
        new MembershipRules("notes", keys: [], memberKeys: MemberKeys.AllBut(), rolesKept: true).MemberKeys.Allows(View).Should().BeTrue();

        // Rules that declare the roles are not held to it: a role that gives nothing is a member's own business there.
        new MembershipRules("notes", Keys, roles: [Bystander], memberKeys: MemberKeys.Only()).MemberKeys.Keys.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- what a database that checks rows is told

    [Fact]
    public void The_keys_that_change_the_members_and_the_owner_are_named_for_the_databases_lock_and_are_none_by_default()
    {
        var silent = new MembershipRules("notes", Keys, roles: [Drafter, Bystander]);
        (silent.ChangeMembersKey, silent.ChangeOwnerKey).Should().Be((null, null), "rules name them only for a database that checks every row itself");

        var named = new MembershipRules("notes", Keys, roles: [Drafter, Bystander], changeMembersKey: Sharing, changeOwnerKey: Edit);
        (named.ChangeMembersKey, named.ChangeOwnerKey).Should().Be((Sharing, Edit));

        // They change nothing of what anybody holds: the rules still say who holds a key, and nothing else.
        foreach (var key in Keys)
        {
            (named.OwnerHolds(key), named.MembersHold(key)).Should().Be((silent.OwnerHolds(key), silent.MembersHold(key)));
            named.RolesWith(key).Should().Equal(silent.RolesWith(key));
        }
    }

    [Fact]
    public void A_key_for_the_databases_lock_that_nobody_could_hold_is_refused()
    {
        static Action Making(Func<MembershipRules> make) => () => make();

        // The rules declare the roles and nothing reaches a note from above: a key that is none of the keys is
        // held by nobody, so the lock would keep everybody out. A slip of the pen, and said as one.
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], changeMembersKey: "notes.shar"))
            .Should().Throw<ArgumentException>().WithParameterName("changeMembersKey")
            .WithMessage("'notes.shar', the key that changes the members, is not one of the resource's keys*");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], changeOwnerKey: "notes.give"))
            .Should().Throw<ArgumentException>().WithParameterName("changeOwnerKey")
            .WithMessage("'notes.give', the key that changes the owner, is not one of the resource's keys*");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], changeMembersKey: " ")).Should().Throw<ArgumentException>().WithParameterName("changeMembersKey");
        Making(() => new MembershipRules("notes", Keys, roles: [Drafter], changeOwnerKey: "")).Should().Throw<ArgumentException>().WithParameterName("changeOwnerKey");

        // A role that is a row gives a member only what a member's role can give, wherever the row is kept:
        // a key outside that, which the owner does not hold either, is held by nobody just the same.
        Making(() => new MembershipRules("notes", keys: [], roles: [Bystander], rolesKept: true, changeMembersKey: "notes.staff"))
            .Should().Throw<ArgumentException>().WithParameterName("changeMembersKey")
            .WithMessage("'notes.staff', the key that changes the members, is a key nobody would hold*memberKeys*");
        Making(() => new MembershipRules("notes", keys: [], ownerRole: "lead", memberKeys: MemberKeys.Only(View), rolesKeptElsewhere: new(), changeOwnerKey: "notes.give"))
            .Should().Throw<ArgumentException>().WithParameterName("changeOwnerKey")
            .WithMessage("'notes.give', the key that changes the owner, is a key nobody would hold*");

        // One a row can give, one the owner holds, and the key being a member gives are each held by somebody.
        new MembershipRules("notes", keys: [], roles: [Bystander], memberKeys: MemberKeys.Only(View, "notes.staff"), rolesKept: true, changeMembersKey: "notes.staff")
            .ChangeMembersKey.Should().Be("notes.staff");
        new MembershipRules("notes", keys: ["notes.give"], roles: [Bystander], rolesKept: true, changeOwnerKey: "notes.give").ChangeOwnerKey.Should().Be("notes.give");
        new MembershipRules("notes", keys: [], ownerRole: "lead", memberKeys: MemberKeys.AllBut(), rolesKeptElsewhere: new(), changeMembersKey: "notes.staff")
            .ChangeMembersKey.Should().Be("notes.staff");
        new MembershipRules("notes", keys: [], seeKey: "notes.peek", ownerRole: "lead", memberKeys: MemberKeys.Only(View), rolesKeptElsewhere: new(), changeMembersKey: "notes.peek")
            .ChangeMembersKey.Should().Be("notes.peek");

        // Held from above, a key is one the rules cannot know: taken as said.
        new MembershipRules("notes", Keys, roles: [Drafter], seeKey: View, above: new(), changeOwnerKey: "notes.give").ChangeOwnerKey.Should().Be("notes.give");
    }

    // ---------------------------------------------------------------- the application's own work

    [Fact]
    public void The_application_itself_holds_everything_and_work_in_a_scope_only_where_the_rules_name_that_scope()
    {
        var silent = new MembershipRules("notes", Keys, roles: [Drafter]);
        silent.SystemScopes.Should().BeEmpty("no scope's work is the application's own for a resource unless its rules say so");
        silent.IsOwnWork(Caller.System).Should().BeTrue("the application itself is above the rules, as everywhere");
        silent.IsOwnWork(Caller.SystemIn("notes")).Should().BeFalse("work in a scope stays inside the rules");

        var scoped = new MembershipRules("notes", Keys, roles: [Drafter], systemScopes: ["notes", "archive-2"]);
        scoped.SystemScopes.Should().Equal("notes", "archive-2");
        scoped.IsOwnWork(Caller.System).Should().BeTrue();
        scoped.IsOwnWork(Caller.SystemIn("notes")).Should().BeTrue();
        scoped.IsOwnWork(Caller.SystemIn("archive-2")).Should().BeTrue();
        scoped.IsOwnWork(Caller.SystemIn("billing")).Should().BeFalse("another module's work is not above this resource's rules");
        scoped.IsOwnWork(Caller.SystemIn("note")).Should().BeFalse("a scope is compared whole");

        // Nobody else is ever the application's own work, whatever its token says.
        scoped.IsOwnWork(Caller.Anonymous).Should().BeFalse();
        scoped.IsOwnWork(Caller.User(Guid.NewGuid(), claim: path => path == "scope" ? "notes" : null)).Should().BeFalse();
        FluentActions.Invoking(() => scoped.IsOwnWork(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("notes module")]
    [InlineData("notes.archive")]
    [InlineData("")]
    public void A_scope_is_named_as_a_caller_is_begun_in_one(string scope)
    {
        FluentActions.Invoking(() => new MembershipRules("notes", Keys, roles: [Drafter], systemScopes: [scope]))
            .Should().Throw<ArgumentException>().WithParameterName("systemScopes").WithMessage("*is not a scope*Caller.SystemIn*");
    }

    [Fact]
    public void A_scope_is_named_once_and_the_list_is_the_rules_own()
    {
        FluentActions.Invoking(() => new MembershipRules("notes", Keys, roles: [Drafter], systemScopes: ["notes", "notes"]))
            .Should().Throw<ArgumentException>().WithParameterName("systemScopes").WithMessage("*each named once*");

        List<string> scopes = ["notes"];
        var rules = new MembershipRules("notes", Keys, roles: [Drafter], systemScopes: scopes);
        scopes.Add("billing");

        rules.SystemScopes.Should().Equal(["notes"], "the rules are what they were made with, whatever happens to the list they were given");
    }

    [Fact]
    public void A_claim_its_user_can_edit_is_no_member_source()
    {
        // A user writes its own user_metadata: whoever is known by a claim in it is whoever the user says it is.
        foreach (var path in new[] { "user_metadata.staff", "user_metadata", "user_metadata.work.code" })
        {
            FluentActions.Invoking(() => MemberSource.Claim(path))
                .Should().Throw<ArgumentException>().WithParameterName("path")
                .WithMessage("'" + path + "' is a claim its user can change*app_metadata.staff*");
        }

        // Only as the first name of the path, where it is the user's own part of the token.
        MemberSource.Claim("app_metadata.user_metadata").ClaimPath.Should().Be("app_metadata.user_metadata");
        MemberSource.Claim("app_metadata.staff").ClaimPath.Should().Be("app_metadata.staff");
        MemberSource.Claim("staff").ClaimPath.Should().Be("staff");
    }

    [Fact]
    public void A_list_of_member_keys_holds_each_key_once_and_no_blank_one()
    {
        MemberKeys.Only(View, Edit, View).Keys.Should().Equal(View, Edit);
        MemberKeys.Only().Allows(View).Should().BeFalse("nothing listed is nothing given");
        MemberKeys.AllBut().Allows(View).Should().BeTrue("nothing excepted is everything given");
        MemberKeys.AllBut(Edit).Allows(Edit).Should().BeFalse();
        MemberKeys.Only(View).Allows("notes.View").Should().BeFalse("a key is compared as it is written");

        FluentActions.Invoking(() => MemberKeys.Only(View, " ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => MemberKeys.AllBut(null!)).Should().Throw<ArgumentNullException>();
    }
}
