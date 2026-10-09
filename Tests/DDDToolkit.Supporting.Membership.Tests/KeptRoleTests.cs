using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// A role kept for a resource, as an application meets it: one line declares the role class and names the
/// resource it is a role of, and the class is an aggregate of the application's own. It is made, renamed,
/// given keys and archived, each under the resource's rules and each saying what happened; the package raises
/// no event and knows nothing of whose a role is. Two role classes are declared in one project, the TestHost:
/// the plots', which the host wraps in words and events of its own, and the sheds', which adds nothing.
/// </summary>
public sealed class KeptRoleTests
{
    private static readonly GardenId Meadow = GardenId.CreateSequential();

    private static MembershipRules Sheds => ShedMembership.Rules;

    private static ShedRole Role(string name = "Lender", string? description = null, params string[] keys)
        => new(ShedRoleId.CreateSequential(), new KeptRoleDraft(name, description, keys));

    // ---------------------------------------------------------------- the template

    [Fact]
    public void Each_role_class_derives_from_the_parent_closed_over_its_own_id_and_is_an_aggregate_of_the_hosts()
    {
        typeof(PlotRole).BaseType.Should().Be(typeof(KeptRoleAggregate<PlotRoleId>));
        typeof(ShedRole).BaseType.Should().Be(typeof(KeptRoleAggregate<ShedRoleId>));
        typeof(KeptRoleAggregate<PlotRoleId>).BaseType.Should().Be(typeof(AggregateRoot<PlotRoleId>), "a role is an aggregate beside the resource, with a version of its own");

        // What the host added is the host's: the plots' roles are a garden's, and the package has no such column.
        typeof(PlotRole).GetProperty(nameof(PlotRole.GardenId))!.DeclaringType.Should().Be(typeof(PlotRole));
        typeof(KeptRoleAggregate<>).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(property => property.Name)
            .Should().BeEquivalentTo("Name", "Description", "Keys", "Status", "MadeFrom");
    }

    [Fact]
    public void The_role_template_names_the_resource_and_may_be_declared_once_for_each_kind_of_resource()
    {
        var template = typeof(KeptRoleAttribute<,>).GetCustomAttribute<AggregateRootTemplateAttribute>()!;

        template.Parent.Should().Be(typeof(KeptRoleAggregate<>));
        template.AllowSeveral.Should().BeTrue("an application has roles of plots and roles of sheds, each a class of its own");
        typeof(KeptRoleAttribute<,>).GetCustomAttributes<TemplateArgumentAttribute>().Should().BeEmpty("nothing is taken from another class");

        static Type[] DeclaredWith(Type roleClass)
            => roleClass.GetCustomAttributes().Single(attribute => attribute.GetType().IsGenericType && attribute.GetType().GetGenericTypeDefinition() == typeof(KeptRoleAttribute<,>))
                .GetType().GetGenericArguments();

        // The second type is the resource: how the roles of a resource are found. The parent takes the id alone.
        DeclaredWith(typeof(PlotRole)).Should().Equal(typeof(PlotRoleId), typeof(Plot));
        DeclaredWith(typeof(ShedRole)).Should().Equal(typeof(ShedRoleId), typeof(Shed));
        typeof(KeptRoleAggregate<>).GetGenericArguments().Should().ContainSingle();

        // A member of the resource holds a role by the role class's id.
        typeof(PlotGardener).BaseType.Should().Be(typeof(MemberEntity<PlotGardenerId, UserId, PlotRoleId>));
        typeof(ShedHand).BaseType.Should().Be(typeof(MemberEntity<ShedHandId, UserId, ShedRoleId>));
    }

    [Fact]
    public void Nothing_about_a_role_is_changed_but_through_what_it_offers_and_nothing_it_decides_is_overridden()
    {
        var parent = typeof(KeptRoleAggregate<>);

        parent.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true })
            .Select(property => property.Name)
            .Should().BeEmpty("a role's state changes through its operations, each held to the resource's rules");

        parent.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Should().BeSubsetOf(
                ["Gives", "Rename", "SetKeys", "Archive", "EnsureInvariants", "EnsureOwnInvariants", "GetInvariantViolations", "GetOwnInvariantViolations"],
                "a role is renamed, given keys and archived, says what it gives, and, as the generator writes for every aggregate, answers about its rules");

        parent.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.IsAbstract || (method.IsVirtual && !method.IsFinal && method.GetBaseDefinition() == method))
            .Select(method => method.Name)
            .Should().BeEmpty("a host adds columns, rules and events of its own; it never overrides what the package decides");

        // Made through a constructor the host's class calls from its own, and no other way from outside.
        parent.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- made

    [Fact]
    public void A_role_is_made_in_use_with_its_name_trimmed_and_its_keys_each_once_in_ordinal_order()
    {
        var role = Role("  Lender\t", "  Lends the tools out. ", ShedKeys.Stock, " " + ShedKeys.Open, ShedKeys.LendTools, ShedKeys.Stock);

        role.Name.Should().Be("Lender");
        role.Description.Should().Be("Lends the tools out.");
        role.Keys.Should().Equal(ShedKeys.Open, ShedKeys.Stock, ShedKeys.LendTools);
        role.Status.Should().Be(KeptRoleStatus.Active);
        role.MadeFrom.Should().BeNull("a role a customer makes came from no starter role");
        role.GetInvariantViolations().Should().BeEmpty();
        role.DomainEvents.Should().BeEmpty("the package raises nothing: what a host tells the world about its roles is the host's to say");

        // No description is an empty one, and no key is a role that gives nothing yet.
        var bare = Role("Visitor");
        (bare.Description, bare.Keys.Count).Should().Be((string.Empty, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void A_name_that_is_empty_once_trimmed_is_refused(string? name)
    {
        var refusal = Refused.With(ShedMembership.Codes, MembershipRefusals.RoleNameInvalid, () => Role(name!));

        refusal.Code.Should().Be("sheds.role-name-invalid");
        refusal.Message.Should().Be("Enter 1 to 120 characters.");
        refusal.Arguments.Should().Contain("Min", 1).And.Contain("Max", KeptRoleAggregate<ShedRoleId>.MaxNameLength).And.Contain(RefusalException.FieldArgument, "name");

        // Renaming is held to the same, and leaves the role as it was.
        var role = Role();
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleNameInvalid, () => role.Rename(name!, null, Sheds));
        role.Name.Should().Be("Lender");
    }

    [Fact]
    public void A_name_and_a_description_are_no_longer_than_a_role_allows_counted_without_the_space_around_them()
    {
        var longest = new string('n', KeptRoleAggregate<ShedRoleId>.MaxNameLength);
        Role(" " + longest + " ").Name.Should().Be(longest);
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleNameInvalid, () => Role(longest + "n"));

        var wordy = new string('d', KeptRoleAggregate<ShedRoleId>.MaxDescriptionLength);
        Role(description: wordy + "  ").Description.Should().Be(wordy);

        // The one rule is about both texts, and says which input it is about.
        var refusal = Refused.With(ShedMembership.Codes, MembershipRefusals.RoleNameInvalid, () => Role(description: wordy + "d"));
        refusal.Message.Should().Be("Enter 0 to 1000 characters.");
        refusal.Arguments[RefusalException.FieldArgument].Should().Be("description");
    }

    // ---------------------------------------------------------------- the keys

    [Fact]
    public void A_key_must_be_one_the_resources_rules_let_a_members_role_give()
    {
        // The sheds' rules let a role give every key but selling: another module's among them, without naming it.
        Role(keys: [ShedKeys.Open, ShedKeys.LendTools, "anything.else"]).Keys.Should().Equal("anything.else", ShedKeys.Open, ShedKeys.LendTools);
        var kept = Refused.With(ShedMembership.Codes, MembershipRefusals.KeyNotForMembers, () => Role(keys: [ShedKeys.Open, ShedKeys.Sell]));
        kept.Code.Should().Be("sheds.key-not-for-members");
        kept.Message.Should().Be("A role cannot give these keys here: sheds.sell.");
        kept.Arguments.Should().Contain("Keys", ShedKeys.Sell).And.Contain(RefusalException.FieldArgument, "keys");

        // The plots' rules list what a role may give: any other key is refused, and every one refused is named.
        var listed = Refused.With(PlotMembership.Codes, MembershipRefusals.KeyNotForMembers, () => new PlotRole(
            PlotRoleId.CreateSequential(), Meadow, new KeptRoleDraft("Seller", null, [PlotKeys.Water, PlotKeys.Sell, "compost.turn", PlotKeys.Fence])));
        listed.Arguments["Keys"].Should().Be("compost.turn, plots.sell");

        // A blank key is no key: refused like one the rules keep from a role.
        Refused.With(ShedMembership.Codes, MembershipRefusals.KeyNotForMembers, () => Role(keys: [ShedKeys.Open, "  "]));
        Refused.With(ShedMembership.Codes, MembershipRefusals.KeyNotForMembers, () => Role(keys: [null!]));
    }

    [Fact]
    public void Setting_the_keys_answers_what_came_in_and_what_went_out_and_refuses_before_anything_changes()
    {
        var role = Role(keys: [ShedKeys.Open, ShedKeys.Stock]);

        var set = role.SetKeys([ShedKeys.LendTools, ShedKeys.Open, " " + ShedKeys.LendTools], Sheds);

        (set.Changed, role.Keys.Count).Should().Be((true, 2));
        set.Added.Should().Equal(ShedKeys.LendTools);
        set.Removed.Should().Equal(ShedKeys.Stock);
        role.Keys.Should().Equal(ShedKeys.Open, ShedKeys.LendTools);

        // The keys it has already, in any order and said twice: nothing came in, nothing went out.
        var same = role.SetKeys([ShedKeys.LendTools, ShedKeys.Open, ShedKeys.Open], Sheds);
        (same.Changed, same.Added.Count, same.Removed.Count).Should().Be((false, 0, 0));

        // Refused as a whole: a key the rules keep from a role takes the others with it, and the role is as it was.
        Refused.With(ShedMembership.Codes, MembershipRefusals.KeyNotForMembers, () => role.SetKeys([ShedKeys.Stock, ShedKeys.Sell], Sheds));
        role.Keys.Should().Equal(ShedKeys.Open, ShedKeys.LendTools);

        // No key at all is a role that gives nothing: allowed, and said as what went out.
        role.SetKeys([], Sheds).Removed.Should().Equal(ShedKeys.Open, ShedKeys.LendTools);
        role.Keys.Should().BeEmpty();
        FluentActions.Invoking(() => role.SetKeys(null!, Sheds)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_role_gives_a_key_while_it_is_in_use_holds_it_and_the_rules_let_a_role_give_it()
    {
        var role = Role(keys: [ShedKeys.Open, ShedKeys.LendTools]);

        role.Gives(ShedKeys.Open, Sheds).Should().BeTrue();
        role.Gives(ShedKeys.LendTools, Sheds).Should().BeTrue();
        role.Gives(ShedKeys.Stock, Sheds).Should().BeFalse("it does not hold it");
        role.Gives("sheds.Open", Sheds).Should().BeFalse("a key is compared as it is written");

        // What a row holds from before the rules changed is cut by what a role may give now.
        Break.Set(role, nameof(ShedRole.Keys), new[] { ShedKeys.Open, ShedKeys.Sell });
        role.Gives(ShedKeys.Sell, Sheds).Should().BeFalse();
        role.Gives(ShedKeys.Open, Sheds).Should().BeTrue();

        // And an archived role gives nothing at all.
        role.Archive(Sheds);
        role.Gives(ShedKeys.Open, Sheds).Should().BeFalse();
        role.Keys.Should().Contain(ShedKeys.Open, "what it held is still there to read");
    }

    // ---------------------------------------------------------------- renamed and archived

    [Fact]
    public void Renaming_answers_whether_anything_changed()
    {
        var role = Role("Lender", "Lends the tools out.");

        role.Rename("  Librarian ", " Lends, and gets them back. ", Sheds).Should().BeTrue();
        (role.Name, role.Description).Should().Be(("Librarian", "Lends, and gets them back."));

        role.Rename("Librarian", "Lends, and gets them back.", Sheds).Should().BeFalse("the name and the description it has already");
        role.Rename(" Librarian ", "Lends, and gets them back. ", Sheds).Should().BeFalse("space around a text is not part of it");
        role.Rename("Librarian", null, Sheds).Should().BeTrue("the description went");
        role.Description.Should().BeEmpty();
        role.Rename("librarian", null, Sheds).Should().BeTrue("a name is compared as it is written");
    }

    [Fact]
    public void An_archived_role_stays_as_it_was_and_changes_no_more()
    {
        var role = Role("Lender", "Lends the tools out.", ShedKeys.Open, ShedKeys.LendTools);

        role.Archive(Sheds);

        role.Status.Should().Be(KeptRoleStatus.Archived);
        (role.Name, role.Description).Should().Be(("Lender", "Lends the tools out."));
        role.Keys.Should().Equal(ShedKeys.Open, ShedKeys.LendTools);
        role.GetInvariantViolations().Should().BeEmpty();

        // Not renamed, given keys or archived again: each refused under the resource's code, with nothing changed.
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => role.Rename("Librarian", null, Sheds)).Code.Should().Be("sheds.role-archived");
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => role.SetKeys([ShedKeys.Open], Sheds));
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => role.Archive(Sheds));
        (role.Name, role.Keys.Count).Should().Be(("Lender", 2));

        // That it is archived is said before anything else about the change is looked at.
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => role.Rename("  ", null, Sheds));
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => role.SetKeys([ShedKeys.Sell], Sheds));
    }

    [Fact]
    public void A_role_remembers_the_starter_role_it_was_made_from_and_the_owners_is_not_archived_whatever_it_is_called()
    {
        // The sheds' rules declare the keeper, the role every owner of a shed holds.
        var keeper = new ShedRole(ShedRoleId.CreateSequential(), StarterRoles.Missing<ShedRoleId>(Sheds, []).Single());
        (keeper.Name, keeper.MadeFrom).Should().Be((ShedMembership.Keeper, ShedMembership.Keeper));

        // Renamed and given other keys like any role, and still the one made from that starter role.
        keeper.Rename("Shed boss", "Runs the shed.", Sheds).Should().BeTrue();
        keeper.SetKeys([ShedKeys.Open], Sheds).Removed.Should().Equal(ShedKeys.Stock);
        keeper.MadeFrom.Should().Be(ShedMembership.Keeper);

        // Not archived: nobody could be made an owner after, and there is no way back from archived.
        Refused.With(ShedMembership.Codes, MembershipRefusals.OwnerRoleStays, () => keeper.Archive(Sheds)).Code.Should().Be("sheds.owner-role-stays");
        keeper.Status.Should().Be(KeptRoleStatus.Active);

        // A role made from another starter role is archived like any other: the plots' tender.
        var tender = new PlotRole(PlotRoleId.CreateSequential(), Meadow, StarterRoles.Missing<PlotRoleId>(PlotMembership.Rules, []).Single(draft => draft.MadeFrom == PlotMembership.Tender));
        tender.PutAway();
        (tender.Status, tender.MadeFrom).Should().Be((KeptRoleStatus.Archived, PlotMembership.Tender));

        // What a role was made from is one of the roles its resource's rules declare, and a draft made from
        // what a customer entered names none.
        FluentActions.Invoking(() => new ShedRole(ShedRoleId.CreateSequential(), new KeptRoleDraft("Boss", null, [], MadeFrom: PlotMembership.Tender)))
            .Should().Throw<ArgumentException>().WithMessage("*made from the starter role 'tender', and the rules 'sheds' declare no role of that name*StarterRoles.Missing*");
        new ShedRole(ShedRoleId.CreateSequential(), new KeptRoleDraft("Boss", null, [], MadeFrom: " ")).MadeFrom.Should().BeNull();
    }

    // ---------------------------------------------------------------- the rules it is held to

    [Fact]
    public void A_role_is_made_and_changed_only_under_rules_that_say_the_roles_are_kept()
    {
        var role = Role();
        var declared = DocumentMembership.Rules;

        // A document's roles are the ones its rules declare: a row made under them would be held by nobody and give nothing.
        FluentActions.Invoking(() => role.Rename("Librarian", null, declared))
            .Should().Throw<ArgumentException>().WithParameterName("rules").WithMessage("The rules 'documents' do not say the resource's roles are kept*rolesKept: true*");
        FluentActions.Invoking(() => role.SetKeys([DocumentKeys.View], declared)).Should().Throw<ArgumentException>().WithParameterName("rules");
        FluentActions.Invoking(() => role.Archive(declared)).Should().Throw<ArgumentException>().WithParameterName("rules");
        FluentActions.Invoking(() => StarterRoles.Missing<ShedRoleId>(declared, [])).Should().Throw<ArgumentException>().WithParameterName("rules");

        FluentActions.Invoking(() => role.Rename("Librarian", null, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => role.SetKeys([], null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => role.Archive(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => role.Gives(ShedKeys.Open, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ShedRole(ShedRoleId.CreateSequential(), null!)).Should().Throw<ArgumentNullException>();
        (role.Name, role.Status).Should().Be(("Lender", KeptRoleStatus.Active));
    }

    [Fact]
    public void A_refusal_carries_the_code_of_the_resource_whose_rules_were_handed_over()
    {
        // Two kinds of resource keep roles in one project, and a client reads from a code which one refused it.
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleNameInvalid, () => Role(" ")).Code.Should().StartWith("sheds.");
        Refused.With(PlotMembership.Codes, MembershipRefusals.RoleNameInvalid, () => new PlotRole(PlotRoleId.CreateSequential(), Meadow, new KeptRoleDraft(" ", null, [])))
            .Code.Should().StartWith("plots.");
    }

    [Fact]
    public void The_two_rules_a_role_checks_itself_hold_what_went_round_its_operations()
    {
        var role = Role("Lender", null, ShedKeys.Open, ShedKeys.LendTools);
        role.GetInvariantViolations().Should().BeEmpty();

        // A name with space around it, as a row written by other hands might have.
        Break.Set(role, nameof(ShedRole.Name), " Lender");
        var named = role.GetInvariantViolations().Should().ContainSingle().Which;
        named.Code.Should().Be(MembershipRefusals.RoleNameInvalid, "a role does not know whose it is, and reports the rule by the package's name");
        (named.EntityType, named.EntityId).Should().Be((typeof(ShedRole), (object)role.Id));
        named.Message.Should().Be("Enter 1 to 120 characters.");

        Break.Set(role, nameof(ShedRole.Name), "Lender");
        Break.Set(role, nameof(ShedRole.Description), new string('d', KeptRoleAggregate<ShedRoleId>.MaxDescriptionLength + 1));
        role.GetInvariantViolations().Should().ContainSingle().Which.Message.Should().Be("Enter 0 to 1000 characters.");
        Break.Set(role, nameof(ShedRole.Description), string.Empty);

        // Keys out of order, said twice, or blank.
        foreach (var keys in new[] { new[] { ShedKeys.Stock, ShedKeys.Open }, [ShedKeys.Open, ShedKeys.Open], [ShedKeys.Open, " "] })
        {
            Break.Set(role, nameof(ShedRole.Keys), keys);
            role.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be(MembershipRefusals.RoleKeysNotNormalized);
        }

        // Whether a key is one a role may give is not checked after the fact: the rules may have changed since,
        // and the row is cut where it is read.
        Break.Set(role, nameof(ShedRole.Keys), new[] { ShedKeys.Open, ShedKeys.Sell });
        role.GetInvariantViolations().Should().BeEmpty();
        FluentActions.Invoking(role.EnsureInvariants).Should().NotThrow();
    }

    [Fact]
    public void What_happened_to_a_role_is_the_hosts_to_tell_and_the_package_raises_nothing()
    {
        // The sheds' role class adds nothing: every operation of the package's, and no event.
        var shed = Role(keys: [ShedKeys.Open]);
        shed.Rename("Librarian", null, Sheds);
        shed.SetKeys([ShedKeys.Stock], Sheds);
        shed.Archive(Sheds);
        shed.DomainEvents.Should().BeEmpty();

        // The plots' role class raises its own from what each operation answered.
        var plot = new PlotRole(PlotRoleId.CreateSequential(), Meadow, new KeptRoleDraft("Fencer", null, [PlotKeys.See]));
        plot.CallIt("Fencer", null);
        plot.HaveItGive(PlotKeys.See);
        plot.CallIt("Hedger", null);
        plot.HaveItGive(PlotKeys.See, PlotKeys.Fence);
        plot.PutAway();

        plot.DomainEvents.Select(raised => raised.GetType()).Should().Equal(
            [typeof(PlotRoleMade), typeof(PlotRoleRenamed), typeof(PlotRoleKeysChanged), typeof(PlotRolePutAway)],
            "what changed nothing answered so, and raised nothing");
        plot.DomainEvents.OfType<PlotRoleKeysChanged>().Single().Added.Should().Equal(PlotKeys.Fence);
        plot.GardenId.Should().Be(Meadow, "whose a role is, is the host's own column, set in the host's own constructor");
    }

    // ---------------------------------------------------------------- the starter roles

    [Fact]
    public void The_starter_roles_are_the_roles_the_rules_declare_each_as_the_draft_its_row_is_made_from()
    {
        var drafts = StarterRoles.Missing<PlotRoleId>(PlotMembership.Rules, []);

        drafts.Select(draft => (draft.Name, draft.MadeFrom, draft.Description)).Should().Equal(
            (PlotMembership.Tender, PlotMembership.Tender, null),
            (PlotMembership.Waterer, PlotMembership.Waterer, null),
            (MembershipRules.DefaultOwnerRole, MembershipRules.DefaultOwnerRole, null));
        drafts[0].Keys.Should().Equal(PlotKeys.See, PlotKeys.Plant, PlotKeys.Water);
        drafts[2].Keys.Should().Equal([PlotKeys.See, PlotKeys.Plant, PlotKeys.Water, PlotKeys.Fence], "the owner's role the rules add gives every key of a plot a member's role can give: not selling");

        // A host makes each with its own constructor, and may call it otherwise from the start: what it was made from stays.
        var named = new PlotRole(PlotRoleId.CreateSequential(), Meadow, drafts[1] with { Name = "Watering can", Description = "Waters." });
        (named.Name, named.MadeFrom, named.Status).Should().Be(("Watering can", PlotMembership.Waterer, KeptRoleStatus.Active));
    }

    [Fact]
    public void A_starter_role_is_made_once_for_a_scope_whatever_became_of_it()
    {
        var rules = PlotMembership.Rules;
        List<PlotRole> meadow = [.. StarterRoles.Missing<PlotRoleId>(rules, []).Select(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft))];

        // Asked again with the roles the scope has: nothing is missing.
        StarterRoles.Missing(rules, meadow).Should().BeEmpty();

        // Renamed, given other keys, archived: told by what it was made from, so still there.
        meadow[0].CallIt("Grower", null);
        meadow[0].HaveItGive(PlotKeys.See);
        meadow[1].PutAway();
        StarterRoles.Missing(rules, meadow).Should().BeEmpty();

        // A role a customer made with a starter role's name is no starter role: that one is still to be made.
        var orchard = new List<PlotRole> { new(PlotRoleId.CreateSequential(), GardenId.CreateSequential(), new KeptRoleDraft(PlotMembership.Tender, null, [PlotKeys.See])) };
        StarterRoles.Missing(rules, orchard).Select(draft => draft.MadeFrom).Should().Equal(PlotMembership.Tender, PlotMembership.Waterer, MembershipRules.DefaultOwnerRole);

        // Only what is missing: a scope that has the owner's role alone is given the other two.
        StarterRoles.Missing(rules, meadow.Where(role => role.MadeFrom == rules.OwnerRole)).Select(draft => draft.MadeFrom).Should().Equal(PlotMembership.Tender, PlotMembership.Waterer);

        // Which scope is meant is said by the roles handed over, and by nothing else.
        FluentActions.Invoking(() => StarterRoles.Missing<PlotRoleId>(rules, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => StarterRoles.Missing<PlotRoleId>(null!, [])).Should().Throw<ArgumentNullException>();
    }
}
