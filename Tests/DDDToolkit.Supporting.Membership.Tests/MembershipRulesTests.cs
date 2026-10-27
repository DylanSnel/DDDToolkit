using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Membership.TestHost.Binders;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The rules of access through a resource's members are one declaration, made through the resource's type and
/// checked when it is made: what it reads off the resource, what it defaults to, what it answers about a permission and a
/// role, and what it refuses to hold together. They say who holds which permission, and nothing about who may change the
/// members.
/// </summary>
public sealed class MembershipRulesTests
{
    private const string View = "notes.view";

    private const string Edit = "notes.edit";

    private const string Sharing = "notes.share";

    private static readonly ResourcePermission[] Permissions =
    [
        new(View, EveryMember: true),
        new(Edit),
        new(Sharing, InRoles: false, ManagesMembers: true, NamesOwner: true),
    ];

    private static readonly DeclaredRole Drafter = new("drafter", [View, Edit]);

    private static readonly DeclaredRole Bystander = new("bystander", [View]);

    // ---------------------------------------------------------------- what the resource says

    [Fact]
    public void Made_through_the_type_the_rules_read_the_name_the_codes_the_scope_and_the_permissions_off_the_resource()
    {
        var rules = DocumentMembership.Rules;

        rules.Resource.Should().Be(typeof(Document));
        rules.Name.Should().Be("documents", "a class's name, made plural and lower case");
        rules.Codes.Should().BeSameAs(DocumentRefusals.Membership, "the codes the document declares, where it declares some");
        rules.Functions.Should().Be(new MembershipFunctions("documents_as_member", "documents_as_member_with", "documents_i_see", "documents_where_i_hold", "documents_fellow_members"));
        rules.Members.Should().BeSameAs(MemberSource.Inferred, "who a member is follows from what it is known by unless the rules say otherwise");
        rules.Roles.Kind.Should().Be(RolesKind.Fixed);
        rules.ReachFromAbove.Should().BeNull("a resource is reached through its members and by its owner unless the rules say otherwise");
        rules.Permissions.Select(permission => permission.Name).Should().Equal(DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share);
        rules.OwnerPermissions.Should().Equal(DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share);
        rules.EveryMemberPermission.Should().Be(DocumentPermissions.View);
        (rules.ManagesMembersPermission, rules.NamesOwnerPermission).Should().Be((DocumentPermissions.Share, DocumentPermissions.Share));
        rules.SystemScopes.Should().BeEmpty("the documents' rules let no scope through, systemScopes: []");
    }

    [Fact]
    public void A_resource_refuses_under_the_codes_written_for_it_where_it_declares_none()
    {
        var rules = MembershipRules.For<Binder>(Roles.Fixed([new("reader", [BinderPermissions.Read])]));

        rules.Name.Should().Be("binders");
        rules.Codes.All.Should().Equal(MembershipCodes.Under("binders").All, "a resource refuses under its own name");
        rules.Codes.Should().BeSameAs(Binder.Codes, "the generator writes them on the class, where the member list reads them too");
        rules.SystemScopes.Should().Equal(["filing"], "rules that say nothing let the scope of the module that declares the resource through, as a scope is written");
    }

    [Fact]
    public void The_rules_decide_nothing_about_who_may_change_the_members()
    {
        // Which permission a command requires is the application's to choose, on the command. Two settings of the permissions
        // name the ones the application's commands require to change the members and the owner: they are what a
        // database that checks every row is told, so it holds a caller that reaches it past the application to the
        // same permissions. Nothing else is said, and nothing here names who may.
        typeof(MembershipRules).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
            "Resource", "Name", "Codes", "Functions", "Members", "Roles", "ReachFromAbove", "Permissions", "OwnerPermissions", "EveryMemberPermission", "RolePermissions",
            "ManagesMembersPermission", "NamesOwnerPermission", "SystemScopes");
        typeof(MembershipRules).GetMethod(nameof(MembershipRules.For))!.GetParameters().Select(parameter => parameter.Name)
            .Should().Equal("roles", "members", "reachFromAbove", "systemScopes");
        typeof(MembershipRules).GetConstructors().Should().BeEmpty("rules are made through the resource's type");

        // The member list, which is what changes the members, is made without the rules and takes no permission: it
        // could not gate by one if it wanted to.
        typeof(MemberList<,,,>).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.Name)
            .Should().NotContain(nameof(MembershipRules));
    }

    [Fact]
    public void Every_entry_said_is_kept_as_said()
    {
        var rules = FolderMembership.Rules;

        rules.Name.Should().Be("folders");
        (rules.Members.Kind, rules.Members.ClaimPath).Should().Be((MemberSourceKind.Claim, "app_metadata.staff"));
        rules.Roles.Declared.Select(role => role.Name).Should().Equal("keeper", "clerk", "visitor");
        rules.RolePermissions.Excepts.Should().BeFalse("the roles take no permissions of other modules");
        rules.RolePermissions.Permissions.Should().Equal(FolderPermissions.Read, FolderPermissions.File, FolderPermissions.Staff);
        rules.OwnerPermissions.Should().Equal(FolderPermissions.Read, FolderPermissions.File, FolderPermissions.Staff, FolderPermissions.Shred, FolderPermissions.HandOver);
        rules.EveryMemberPermission.Should().BeNull("no permission of a folder is held by every member");
        (rules.ManagesMembersPermission, rules.NamesOwnerPermission).Should().Be((FolderPermissions.Staff, FolderPermissions.HandOver));
        rules.Codes.Should().BeSameAs(FolderRefusals.Membership);
        rules.Functions.Should().Be(new MembershipFunctions("folders_as_member", "folders_as_member_with", "folders_i_see", "folders_where_i_hold", "folders_fellow_members"));
        rules.SystemScopes.Should().Equal(FolderMembership.Scope);
    }

    [Fact]
    public void The_rules_of_a_resource_are_made_from_the_permissions_every_class_declares_on_it()
    {
        MembershipPermissions.Declare<Pad>("Tests.PadPermissions", [new("pads.read", EveryMember: true), new("pads.write")], elsewhere: ["pads.order"]);
        MembershipPermissions.Declare<Pad>("Tests.MorePadPermissions", [new("pads.tear", InRoles: false)]);

        var rules = MembershipRules.For<Pad>(Roles.Custom(otherPermissions: OtherPermissions.All));

        // Each class's in the order it declares them, and the classes in the order of their names.
        rules.Permissions.Select(permission => permission.Name).Should().Equal("pads.tear", "pads.read", "pads.write");
        rules.SystemScopes.Should().Equal("stationery");
        rules.RolePermissions.Allows("labels.print").Should().BeTrue("the roles take permissions of other modules");
        rules.RolePermissions.Allows("pads.order").Should().BeFalse("a permission the module declares at another level is no permission of a pad's role, whatever the roles take");
        rules.RolePermissions.Allows("pads.tear").Should().BeFalse("no role gives what is marked InRoles = false");

        // A class declared again says what it said: two assemblies that declare it see one class.
        FluentActions.Invoking(() => MembershipPermissions.Declare<Pad>("Tests.PadPermissions", [new("pads.read", EveryMember: true), new("pads.write")], elsewhere: ["pads.order"]))
            .Should().NotThrow();
        FluentActions.Invoking(() => MembershipPermissions.Declare<Pad>("Tests.PadPermissions", [new("pads.read")]))
            .Should().Throw<InvalidOperationException>().WithMessage("*declares other permissions on Pad*");
    }

    [Fact]
    public void Rules_of_a_resource_nothing_declares_a_permission_on_are_refused()
    {
        FluentActions.Invoking(() => MembershipRules.For<Blotter>(Roles.Custom()))
            .Should().Throw<ArgumentException>().WithMessage("No permission is declared on Blotter*[On<Blotter>]*[Permissions]*");
    }

    // ---------------------------------------------------------------- what the rules answer

    [Fact]
    public void The_owner_holds_the_permissions_marked_owner_by_owning_the_resource_and_no_other()
    {
        var rules = Notes([new(View, EveryMember: true), new(Edit), new(Sharing, Owner: false)], Roles.Fixed([Drafter, Bystander]));

        rules.OwnerPermissions.Should().Equal(View, Edit);
        rules.OwnerHolds(View).Should().BeTrue();
        rules.OwnerHolds(Edit).Should().BeTrue();
        rules.OwnerHolds(Sharing).Should().BeFalse("marked Owner = false: the owner holds it only as anybody would");
        rules.OwnerHolds("notes.print").Should().BeFalse("a permission of nobody's is held by no owner");
    }

    [Fact]
    public void No_role_is_the_owners_and_a_role_called_owner_is_a_role_like_any_other()
    {
        var rules = Notes(Permissions, Roles.Fixed([Drafter, new("owner", [View])]));

        rules.Roles.Declared.Select(role => role.Name).Should().Equal("drafter", "owner");
        rules.PermissionsOf(new NamedRole("owner")).Should().Equal([View], "it gives what it lists, to whoever holds it");
        rules.RolesWith(Edit).Should().Equal([new NamedRole("drafter")]);

        var unnamed = Notes(Permissions, Roles.Fixed([Drafter]));
        unnamed.Roles.Declared.Select(role => role.Name).Should().Equal(["drafter"], "nothing is added for the owner, which holds no role");
        unnamed.Knows(new NamedRole("owner")).Should().BeFalse();
        unnamed.PermissionsOf(new NamedRole("owner")).Should().BeEmpty();
    }

    [Fact]
    public void Being_a_member_gives_the_permission_every_member_holds_and_nothing_else()
    {
        var rules = Notes(Permissions, Roles.Fixed([Drafter]));

        rules.MembershipGives(View).Should().BeTrue();
        rules.MembershipGives(Edit).Should().BeFalse("only through a role");
        rules.MembersHold(View).Should().BeTrue();
        rules.MembersHold(Edit).Should().BeTrue("a role can give it");
        rules.MembersHold(Sharing).Should().BeFalse("no role gives it, and being a member does not");

        var none = Notes([new(View), new(Edit)], Roles.Fixed([Drafter]));
        none.EveryMemberPermission.Should().BeNull();
        none.MembershipGives(View).Should().BeFalse("being a member gives nothing unless a permission says so");
    }

    [Fact]
    public void A_role_gives_the_permissions_marked_in_roles_and_where_the_roles_take_them_permissions_of_other_modules_but_those_excepted()
    {
        var fixedRoles = Notes(Permissions, Roles.Fixed([Drafter, Bystander]));
        fixedRoles.RolePermissions.Excepts.Should().BeFalse();
        fixedRoles.RolePermissions.Permissions.Should().Equal(View, Edit);
        fixedRoles.RolePermissions.Allows("labels.print").Should().BeFalse("roles in code give the resource's permissions and nothing else");

        var custom = Notes(Permissions, Roles.Custom(otherPermissions: OtherPermissions.AllBut("depot.manage")), elsewhere: ["notes.open"]);
        custom.RolePermissions.Excepts.Should().BeTrue();
        custom.RolePermissions.Allows(View).Should().BeTrue();
        custom.RolePermissions.Allows("labels.print").Should().BeTrue("a permission of another module the roles take");
        custom.RolePermissions.Allows("depot.manage").Should().BeFalse("excepted by name");
        custom.RolePermissions.Allows(Sharing).Should().BeFalse("marked InRoles = false");
        custom.RolePermissions.Allows("notes.open").Should().BeFalse("declared by the resource's module at another level: held at a unit, never through a role on a note");

        var external = Notes(Permissions, Roles.External(new DatabaseFunction("org", "roles_with_permission")));
        external.RolePermissions.Excepts.Should().BeFalse("no permissions of other modules unless the roles take them");
        external.Roles.Function.Should().Be(new DatabaseFunction("org", "roles_with_permission"));
    }

    [Fact]
    public void The_fixed_roles_that_give_a_permission_are_the_declared_ones_in_the_order_they_are_declared()
    {
        var rules = Notes(Permissions, Roles.Fixed([Drafter, Bystander]));

        rules.RolesWith(View).Should().Equal(new NamedRole("drafter"), new NamedRole("bystander"));
        rules.RolesWith(Edit).Should().Equal([new NamedRole("drafter")]);
        rules.RolesWith(Sharing).Should().BeEmpty();
        rules.PermissionsOf(new NamedRole("drafter")).Should().Equal(View, Edit);
        rules.PermissionsOf(new NamedRole("auditor")).Should().BeEmpty("a role the rules do not declare gives nothing");

        // Rows of a customer's, or of the application's own, are read where they are kept.
        Notes(Permissions, Roles.Custom(starters: [Drafter])).RolesWith(View).Should().BeEmpty();
        Notes(Permissions, Roles.External()).RolesWith(View).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- names

    [Theory]
    [InlineData("Notes")]
    [InlineData("1notes")]
    [InlineData("notes_")]
    [InlineData("notes book")]
    [InlineData("")]
    public void A_name_that_cannot_start_a_code_is_refused(string name)
    {
        FluentActions.Invoking(() => MembershipRules.Declare(typeof(Pad), name, Permissions, Roles.Fixed([Drafter])))
            .Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Theory]
    [InlineData("notes", "notes.already-member", "notes_i_see")]
    [InlineData("crane-boards", "crane-boards.already-member", "crane_boards_i_see")]
    [InlineData("school.notes", "school.notes.already-member", "school_notes_i_see")]
    public void A_name_names_the_codes_and_the_functions(string name, string code, string seen)
    {
        var rules = MembershipRules.Declare(typeof(Pad), name, Permissions, Roles.Fixed([Drafter]));

        rules.Codes[MembershipRefusals.AlreadyMember].Should().Be(code);
        rules.Functions.Seen.Should().Be(seen);
    }

    // ---------------------------------------------------------------- what is refused

    [Fact]
    public void Permissions_are_each_declared_once_and_none_is_blank()
    {
        Making(() => Notes([new(View), new(View)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("'notes.view' is declared on Pad twice*");
        Making(() => Notes([new(View), new(" ")], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("*not blank*");
        Making(() => Notes([], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("No permission is declared on Pad*");
    }

    [Fact]
    public void One_permission_at_most_is_held_by_every_member_locks_the_members_and_locks_the_owner()
    {
        Making(() => Notes([new(View, EveryMember: true), new(Edit, EveryMember: true)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("Two permissions on Pad are marked EveryMember*'notes.view' and 'notes.edit'*");
        Making(() => Notes([new(View, ManagesMembers: true), new(Edit, ManagesMembers: true)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("Two permissions on Pad are marked ManagesMembers*");
        Making(() => Notes([new(View, NamesOwner: true), new(Edit, NamesOwner: true)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("Two permissions on Pad are marked NamesOwner*");

        // One permission may lock both, as a document's that decides who it is shared with does.
        Notes([new(View, ManagesMembers: true, NamesOwner: true)], Roles.Fixed([Bystander])).NamesOwnerPermission.Should().Be(View);
    }

    [Fact]
    public void A_role_in_code_lists_the_permissions_a_role_gives_and_nothing_else()
    {
        Making(() => Notes(Permissions, Roles.Fixed([new("drafter", [View, "notes.print"])])))
            .Should().Throw<ArgumentException>().WithMessage("The role 'drafter' lists 'notes.print', which is no permission on Pad*");
        Making(() => Notes(Permissions, Roles.Fixed([new("drafter", [View, Sharing])])))
            .Should().Throw<ArgumentException>().WithMessage("The role 'drafter' lists 'notes.share', which no role gives on Pad*InRoles = false*");
        FluentActions.Invoking(() => Roles.Fixed([]))
            .Should().Throw<ArgumentException>().WithMessage("Fixed roles are at least one role*");
        FluentActions.Invoking(() => Roles.Fixed([Drafter, new("drafter", [View])]))
            .Should().Throw<ArgumentException>().WithMessage("The role 'drafter' is declared twice*");
        FluentActions.Invoking(() => Roles.Fixed([new(" padded", [View])]))
            .Should().Throw<ArgumentException>().WithMessage("A role's name is 1 to*");
    }

    [Fact]
    public void A_starter_role_lists_what_a_role_a_customer_makes_could_give()
    {
        Making(() => Notes(Permissions, Roles.Custom(starters: [new("drafter", [View, Sharing])])))
            .Should().Throw<ArgumentException>().WithMessage("The starter role 'drafter' lists 'notes.share', which a role cannot give on Pad*");
        Making(() => Notes(Permissions, Roles.Custom(starters: [new("printer", ["labels.print"])])))
            .Should().Throw<ArgumentException>("a permission of another module, where the roles take none");
        Notes(Permissions, Roles.Custom(starters: [new("printer", ["labels.print"])], otherPermissions: OtherPermissions.All))
            .Roles.Declared.Single().Permissions.Should().Equal("labels.print");

        // Rows of which none could ever give anything are refused, since every permission would be refused with them.
        Making(() => Notes([new(View, InRoles: false)], Roles.Custom()))
            .Should().Throw<ArgumentException>().WithMessage("No permission on Pad can be given by a role*OtherPermissions.All*");
        Notes([new(View, InRoles: false)], Roles.Custom(otherPermissions: OtherPermissions.All)).RolePermissions.Allows("labels.print").Should().BeTrue();
    }

    [Fact]
    public void A_lock_nobody_could_pass_is_refused()
    {
        Making(() => Notes([new(View, EveryMember: true), new(Sharing, Owner: false, InRoles: false, ManagesMembers: true)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("'notes.share' is marked ManagesMembers, and nobody would hold it on Pad*");
        Making(() => Notes([new(View, EveryMember: true), new(Sharing, Owner: false, InRoles: false, NamesOwner: true)], Roles.Fixed([Bystander])))
            .Should().Throw<ArgumentException>().WithMessage("'notes.share' is marked NamesOwner*");

        // Held from above, it is passed by whoever holds it there.
        Notes([new(View, EveryMember: true), new(Sharing, Owner: false, InRoles: false, NamesOwner: true)], Roles.Fixed([Bystander]), reachFromAbove: true)
            .NamesOwnerPermission.Should().Be(Sharing);
    }

    [Fact]
    public void Reach_from_above_sees_a_resource_by_the_permission_every_member_holds()
    {
        Making(() => Notes([new(View), new(Edit)], Roles.Fixed([Bystander]), reachFromAbove: true))
            .Should().Throw<ArgumentException>().WithParameterName("reachFromAbove").WithMessage("*no permission on it is held by every member*[On<Pad>(EveryMember = true)]*");
        Making(() => Notes([new(View, EveryMember: true, FromAbove: false), new(Edit)], Roles.Fixed([Bystander]), reachFromAbove: true))
            .Should().Throw<ArgumentException>().WithParameterName("reachFromAbove").WithMessage("*FromAbove = false*");
    }

    [Fact]
    public void A_permission_reaches_from_above_unless_it_says_otherwise_and_only_where_the_rules_let_one()
    {
        var reached = Notes([new(View, EveryMember: true), new(Edit), new(Sharing, FromAbove: false)], Roles.Fixed([Bystander]), reachFromAbove: true);
        reached.ReachesFromAbove(View).Should().BeTrue();
        reached.ReachesFromAbove(Edit).Should().BeTrue();
        reached.ReachesFromAbove("labels.print").Should().BeTrue("a permission of another module held above reaches the resource as well");
        reached.ReachesFromAbove(Sharing).Should().BeFalse("marked FromAbove = false: held there, it gives nothing on the resource");

        Notes(Permissions, Roles.Fixed([Bystander])).ReachesFromAbove(View).Should().BeFalse("the rules let nothing reach from above");
    }

    [Fact]
    public void Reach_from_above_is_the_function_that_follows_from_the_place_or_the_applications_own()
    {
        ReachFromAbove? package = true;
        package!.Function.Should().BeNull("beside Tenancy, the function follows from the type of the place: its unit id");

        ReachFromAbove? none = false;
        none.Should().BeNull("false says what leaving it out says");

        ReachFromAbove own = new DatabaseFunction("depot", "bays_where_i_hold");
        own.Function!.LogicalName.Should().Be("depot/bays_where_i_hold");

        Notes(Permissions, Roles.Fixed([Bystander]), reachFromAbove: own).ReachFromAbove!.Function.Should().Be(new DatabaseFunction("depot", "bays_where_i_hold"));
    }

    [Fact]
    public void Rules_are_data_that_never_changes_after_it_is_made()
    {
        List<ResourcePermission> stated = [new(View, EveryMember: true), new(Edit)];
        List<string> permissions = [View];
        List<DeclaredRole> roles = [Drafter, new("bystander", permissions)];
        List<string> scopes = ["notes"];
        var rules = Notes(stated, Roles.Fixed(roles), systemScopes: scopes);

        stated.Add(new("notes.print"));
        roles.Add(new("auditor", [View]));
        permissions.Add(Edit);
        scopes.Add("billing");

        rules.Permissions.Select(permission => permission.Name).Should().Equal(View, Edit);
        rules.OwnerHolds("notes.print").Should().BeFalse("a permission added to the list afterwards is none of the resource's");
        rules.Roles.Declared.Select(role => role.Name).Should().Equal("drafter", "bystander");
        rules.PermissionsOf(new NamedRole("bystander")).Should().Equal([View], "the permissions of a role are the ones it was declared with");
        rules.SystemScopes.Should().Equal(["notes"]);
        typeof(MembershipRules).GetProperties().Should().OnlyContain(property => property.SetMethod == null, "everything that reads the rules reads the same thing");
    }

    // ---------------------------------------------------------------- whose system work holds everything

    [Fact]
    public void System_work_holds_everything_and_work_in_a_scope_where_the_rules_let_that_scope_through()
    {
        var own = Notes(Permissions, Roles.Fixed([Drafter]), scope: "notes");
        own.SystemScopes.Should().Equal(["notes"], "the module's own scope unless the rules say otherwise");
        own.IsOwnWork(CurrentUserSnapshot.System).Should().BeTrue("the application itself is above the rules, as everywhere");
        own.IsOwnWork(CurrentUserSnapshot.SystemIn("notes")).Should().BeTrue("the module's own work");
        own.IsOwnWork(CurrentUserSnapshot.SystemIn("tenancy")).Should().BeFalse("another module's work is not above this resource's rules");
        own.IsOwnWork(CurrentUserSnapshot.SystemIn("note")).Should().BeFalse("a scope is compared whole");

        // Nobody else is ever the application's own work, whatever its token says.
        own.IsOwnWork(CurrentUserSnapshot.Anonymous).Should().BeFalse();
        own.IsOwnWork(CurrentUserSnapshot.User(Guid.NewGuid(), claim: path => path == "scope" ? "notes" : null)).Should().BeFalse();
        FluentActions.Invoking(() => own.IsOwnWork(null!)).Should().Throw<ArgumentNullException>();

        // A resource of no module has no scope of its own.
        Notes(Permissions, Roles.Fixed([Drafter])).SystemScopes.Should().BeEmpty();
    }

    [Fact]
    public void Scopes_said_replace_the_modules_own_and_none_said_turn_system_work_in_a_scope_off()
    {
        var replaced = Notes(Permissions, Roles.Fixed([Drafter]), scope: "notes", systemScopes: ["imports"]);
        replaced.SystemScopes.Should().Equal(["imports"], "the scopes named and no other");
        replaced.IsOwnWork(CurrentUserSnapshot.SystemIn("notes")).Should().BeFalse("the module's own is named among them to keep it");

        var kept = Notes(Permissions, Roles.Fixed([Drafter]), scope: "notes", systemScopes: ["notes", "imports"]);
        kept.IsOwnWork(CurrentUserSnapshot.SystemIn("notes")).Should().BeTrue();
        kept.IsOwnWork(CurrentUserSnapshot.SystemIn("imports")).Should().BeTrue();

        var off = Notes(Permissions, Roles.Fixed([Drafter]), scope: "notes", systemScopes: []);
        off.SystemScopes.Should().BeEmpty();
        off.IsOwnWork(CurrentUserSnapshot.SystemIn("notes")).Should().BeFalse("no system work in a scope at all");
        off.IsOwnWork(CurrentUserSnapshot.System).Should().BeTrue("the application itself still is");
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("notes module")]
    [InlineData("notes.archive")]
    [InlineData("")]
    public void A_scope_is_named_as_a_caller_is_begun_in_one(string scope)
    {
        Making(() => Notes(Permissions, Roles.Fixed([Drafter]), systemScopes: [scope]))
            .Should().Throw<ArgumentException>().WithParameterName("systemScopes").WithMessage("*is not a scope*BeginSystem(scope)*");
    }

    [Fact]
    public void A_scope_is_named_once()
    {
        Making(() => Notes(Permissions, Roles.Fixed([Drafter]), systemScopes: ["notes", "notes"]))
            .Should().Throw<ArgumentException>().WithParameterName("systemScopes").WithMessage("*each named once*");
    }

    // ---------------------------------------------------------------- where members come from

    [Fact]
    public void Who_a_member_is_follows_from_what_it_is_known_by_unless_the_rules_say_otherwise()
    {
        (MemberSource.Inferred.Kind, MemberSource.Inferred.ClaimPath, MemberSource.Inferred.Function).Should().Be((MemberSourceKind.Inferred, null, null));
        Notes(Permissions, Roles.Fixed([Drafter])).Members.Should().BeSameAs(MemberSource.Inferred);
        Notes(Permissions, Roles.Fixed([Drafter]), members: MemberSource.CallerId).Members.Should().BeSameAs(MemberSource.CallerId);
    }

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
        MemberSource.Claim("staff").ClaimPath.Should().Be("staff");
    }

    [Fact]
    public void A_member_the_application_resolves_is_asked_of_the_application_and_names_its_function_for_a_database_that_answers()
    {
        var porter = new DatabaseFunction("depot", "caller_porter");
        var resolved = MemberSource.Resolved(porter);

        (resolved.Kind, resolved.ClaimPath, resolved.Function).Should().Be((MemberSourceKind.Resolved, null, porter));
        resolved.Should().Be(MemberSource.Resolved(new DatabaseFunction("depot", "caller_porter")), "a source is data: two that say the same are equal");
        resolved.Should().NotBe(MemberSource.Resolved(new DatabaseFunction("depot", "acting_porter")));

        // An application whose database answers nothing names no function, and says the rest all the same.
        (MemberSource.Resolved().Kind, MemberSource.Resolved().Function).Should().Be((MemberSourceKind.Resolved, null));
    }

    [Theory]
    [InlineData("depot", "caller_porter")]
    [InlineData("second-depot-2", "caller_Porter_2")]
    [InlineData("a", "_b")]
    public void A_function_is_named_by_its_owner_and_its_name(string owner, string name)
    {
        var function = new DatabaseFunction(owner, name);

        (function.Owner, function.Name).Should().Be((owner, name));
        function.LogicalName.Should().Be(owner + "/" + name);
        function.ToString().Should().Be(function.LogicalName);
    }

    [Theory]
    [InlineData("", "caller_porter", "owner")]
    [InlineData("Depot", "caller_porter", "owner")]
    [InlineData("-depot", "caller_porter", "owner")]
    [InlineData("depot-", "caller_porter", "owner")]
    [InlineData("depot.x", "caller_porter", "owner")]
    [InlineData("depot", "", "name")]
    [InlineData("depot", "9lives", "name")]
    [InlineData("depot", "caller porter", "name")]
    [InlineData("depot", "caller_porter()", "name")]
    [InlineData("depot", "x}; DROP TABLE y; --", "name")]
    [InlineData("depot", "caller/porter", "name")]
    public void What_is_not_the_name_of_a_function_is_refused_when_it_is_said(string owner, string name, string parameter)
    {
        // It goes into what is asked of the database, and a name with its schema would be asked whether or not anything
        // defines it: so an owner and a name, and nothing else.
        FluentActions.Invoking(() => new DatabaseFunction(owner, name)).Should().Throw<ArgumentException>().WithParameterName(parameter);
    }

    // ---------------------------------------------------------------- support

    /// <summary>Rules of a note pad, made from what is said here, as a test of the rules makes them.</summary>
    private static MembershipRules Notes(
        IReadOnlyList<ResourcePermission> permissions,
        Roles roles,
        MemberSource? members = null,
        ReachFromAbove? reachFromAbove = null,
        IReadOnlyList<string>? systemScopes = null,
        string? scope = null,
        IReadOnlyList<string>? elsewhere = null)
        => MembershipRules.Declare(typeof(Pad), "notes", permissions, roles, members, reachFromAbove, systemScopes, scope, elsewhere: elsewhere);

    private static Func<MembershipRules> Making(Func<MembershipRules> make) => make;

    /// <summary>A resource of the tests' own, as the generator would write it onto a class.</summary>
    private sealed class Pad : IMembershipResource
    {
        public static string MembershipName => "pads";

        public static MembershipCodes MembershipCodes { get; } = MembershipCodes.Under("pads");

        public static string? MembershipScope => "stationery";
    }

    /// <summary>A resource nothing declares a permission on.</summary>
    private sealed class Blotter : IMembershipResource
    {
        public static string MembershipName => "blotters";

        public static MembershipCodes MembershipCodes { get; } = MembershipCodes.Under("blotters");

        public static string? MembershipScope => null;
    }
}
