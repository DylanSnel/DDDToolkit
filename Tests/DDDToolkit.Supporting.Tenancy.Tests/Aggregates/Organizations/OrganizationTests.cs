using DDDToolkit.Exceptions;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The organization is one tree: one root, no unit under itself, at most 32 levels, and units archived
/// rather than deleted. Every change is refused before it breaks that, and says so with one event.
/// </summary>
public class OrganizationTests
{
    private const TenantShape Tree = TenantShape.Hierarchical;

    private static OrganizationUnitId Add(HostOrganization organization, OrganizationUnitId parent, string name = "Unit")
        => organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), parent, name, Tree).Id;

    /// <summary>A chain of <paramref name="levels"/> units below <paramref name="top"/>; returns the deepest.</summary>
    private static OrganizationUnitId Chain(HostOrganization organization, OrganizationUnitId top, int levels)
    {
        var unit = top;
        for (var level = 0; level < levels; level++)
        {
            unit = Add(organization, unit, "Level " + level);
        }

        return unit;
    }

    [Fact]
    public void A_new_organization_has_exactly_one_active_root()
    {
        var organization = New.Organization(tenant: 3, name: " Harbor Works ");

        organization.Id.Should().Be(new TenantId(3), "an organization shares its tenant's id");
        organization.Name.Should().Be("Harbor Works");
        var root = organization.Units.Should().ContainSingle().Which;
        root.Should().BeSameAs(organization.Root);
        root.IsRoot.Should().BeTrue();
        root.ParentId.Should().BeNull();
        root.Status.Should().Be(UnitStatus.Active);
        root.Name.Should().Be("Harbor Works");

        var added = organization.PendingEvents().RaisedExactly<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>()
            .SingleEvent<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>();
        added.Should().Be(new OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>(new TenantId(3), root.Id, null, By: null)
        {
            EventId = added.EventId,
            OccurredAt = added.OccurredAt,
        });
        organization.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_flat_tenant_refuses_units_below_the_root()
    {
        var organization = New.Organization();

        Refused.With(TenancyRefusals.FlatTenant, () => organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), organization.Root.Id, "North", TenantShape.Flat));
        organization.Units.Should().ContainSingle();
    }

    [Fact]
    public void Adding_under_an_unknown_or_archived_parent_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        organization.ArchiveUnit<SeatId>(north);

        Refused.With(TenancyRefusals.UnitNotFound, () => Add(organization, OrganizationUnitId.CreateSequential()));
        Refused.With(TenancyRefusals.UnitNotActive, () => Add(organization, north)).Arguments["Unit"].Should().Be(north);
    }

    [Fact]
    public void A_blank_name_is_name_invalid()
    {
        var organization = New.Organization();

        Refused.With(TenancyRefusals.NameInvalid, () => Add(organization, organization.Root.Id, " "))
            .Arguments["What"].Should().Be("unit-name");
        organization.Units.Should().ContainSingle("a refused unit is never added");
    }

    [Fact]
    public void A_new_unit_is_the_applications_own_class_with_its_fields_at_their_defaults()
    {
        var organization = New.Organization();

        var north = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), organization.Root.Id, "North", Tree);
        north.SetCostCentre("NO-001");

        north.Should().BeOfType<HostUnit>().And.BeSameAs(organization.FindUnit(north.Id));
        organization.Root.CostCentre.Should().BeNull("a field of the application's starts at its default");
        organization.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_unit_whose_callback_throws_leaves_the_organization_as_it_was()
    {
        var organization = New.Organization();
        var events = organization.DomainEvents.Count;
        var seen = new List<string>();

        Refused.With(TenancyRefusals.FlatTenant, () => organization.AddUnit<SeatId>(
            OrganizationUnitId.CreateSequential(), organization.Root.Id, "Refused", TenantShape.Flat, configure: unit => seen.Add(unit.Name)));
        FluentActions.Invoking(() => organization.AddUnit<SeatId>(
                OrganizationUnitId.CreateSequential(),
                organization.Root.Id,
                "Ghost",
                Tree,
                configure: unit =>
                {
                    seen.Add(unit.Name);
                    throw new InvalidOperationException("no such kind");
                }))
            .Should().Throw<InvalidOperationException>().WithMessage("no such kind");

        seen.Should().Equal(["Ghost"], "the callback runs after every check, and only for a unit that passed them");
        organization.Units.Should().ContainSingle("the organization takes a unit in only once its callback returns");
        organization.DomainEvents.Should().HaveCount(events, "and raises nothing for a unit it did not take in");

        var north = organization.AddUnit<SeatId>(
            OrganizationUnitId.CreateSequential(), organization.Root.Id, "North", Tree, configure: unit => unit.SetCostCentre("NO-001"));
        organization.FindUnit(north.Id)!.CostCentre.Should().Be("NO-001");
        organization.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void The_33rd_level_is_refused()
    {
        var organization = New.Organization();
        var deepest = Chain(organization, organization.Root.Id, OrganizationAggregate<TenantId, HostUnit, OrganizationUnitId>.MaxDepth - 1);
        organization.DepthOf(deepest).Should().Be(32);

        var refusal = Refused.With(TenancyRefusals.DepthExceeded, () => Add(organization, deepest));

        refusal.Arguments["Max"].Should().Be(32);
        refusal.Message.Should().Be("The tree may have at most 32 levels.");
        organization.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void Moving_the_root_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id);

        Refused.With(TenancyRefusals.RootImmovable, () => organization.MoveUnit<SeatId>(organization.Root.Id, north));
    }

    [Fact]
    public void Moving_under_itself_or_a_descendant_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        var northCoast = Add(organization, north, "North Coast");

        Refused.With(TenancyRefusals.Cycle, () => organization.MoveUnit<SeatId>(north, north));
        Refused.With(TenancyRefusals.Cycle, () => organization.MoveUnit<SeatId>(north, northCoast));
        organization.FindUnit(north)!.ParentId.Should().Be(organization.Root.Id);
    }

    [Fact]
    public void Moving_to_the_current_parent_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id);

        Refused.With(TenancyRefusals.SameParent, () => organization.MoveUnit<SeatId>(north, organization.Root.Id));
    }

    [Fact]
    public void Moving_an_archived_unit_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        var south = Add(organization, organization.Root.Id, "South");
        var southBay = Add(organization, south, "South Bay");
        organization.ArchiveUnit<SeatId>(southBay);
        organization.ArchiveUnit<SeatId>(south);

        Refused.With(TenancyRefusals.UnitArchived, () => organization.MoveUnit<SeatId>(southBay, north));
        Refused.With(TenancyRefusals.UnitNotActive, () => organization.MoveUnit<SeatId>(north, south));
        Refused.With(TenancyRefusals.UnitNotFound, () => organization.MoveUnit<SeatId>(OrganizationUnitId.CreateSequential(), north));
        Refused.With(TenancyRefusals.UnitNotFound, () => organization.MoveUnit<SeatId>(north, OrganizationUnitId.CreateSequential()));
    }

    [Fact]
    public void Moving_counts_the_height_of_the_whole_subtree()
    {
        var organization = New.Organization();
        var moved = Add(organization, organization.Root.Id, "Moved");
        Chain(organization, moved, 2);
        organization.HeightOf(moved).Should().Be(3);

        var depth29 = Chain(organization, organization.Root.Id, 28);
        var depth30 = Add(organization, depth29);
        organization.DepthOf(depth30).Should().Be(30);

        Refused.With(TenancyRefusals.DepthExceeded, () => organization.MoveUnit<SeatId>(moved, depth30));

        organization.MoveUnit<SeatId>(moved, depth29);
        organization.DepthOf(moved).Should().Be(30);
        organization.GetInvariantViolations().Should().BeEmpty("the deepest unit it carried is at level 32");
    }

    [Fact]
    public void Archiving_the_root_is_refused()
    {
        var organization = New.Organization();

        Refused.With(TenancyRefusals.RootNotArchivable, () => organization.ArchiveUnit<SeatId>(organization.Root.Id));
    }

    [Fact]
    public void Archiving_with_active_children_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        var northCoast = Add(organization, north, "North Coast");

        Refused.With(TenancyRefusals.UnitHasActiveChildren, () => organization.ArchiveUnit<SeatId>(north));

        organization.ArchiveUnit<SeatId>(northCoast);
        organization.ArchiveUnit<SeatId>(north);
        organization.IsActiveUnit(north).Should().BeFalse();
        organization.FindUnit(north)!.Status.Should().Be(UnitStatus.Archived);
        organization.Units.Should().HaveCount(3, "a unit is archived, never deleted");
    }

    [Fact]
    public void Archiving_twice_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id);
        organization.ArchiveUnit<SeatId>(north);

        Refused.With(TenancyRefusals.UnitArchived, () => organization.ArchiveUnit<SeatId>(north));
        Refused.With(TenancyRefusals.UnitNotFound, () => organization.ArchiveUnit<SeatId>(OrganizationUnitId.CreateSequential()));
    }

    [Fact]
    public void Renaming_an_archived_unit_is_refused()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        organization.ArchiveUnit<SeatId>(north);

        Refused.With(TenancyRefusals.UnitArchived, () => organization.RenameUnit<SeatId>(north, "Northern"));
        Refused.With(TenancyRefusals.UnitNotFound, () => organization.RenameUnit<SeatId>(OrganizationUnitId.CreateSequential(), "Northern"));
        organization.FindUnit(north)!.Name.Should().Be("North");
    }

    [Fact]
    public void Renaming_to_the_same_name_raises_nothing()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        var scenario = organization.AsScenario().IgnorePendingEvents();

        scenario.When(tree => tree.RenameUnit<SeatId>(north, "  North ")).RaisedNothing();
        scenario.When(tree => tree.Rename<SeatId>("Harbor Works")).RaisedNothing();
        scenario.WhenThrows<RefusalException>(tree => tree.RenameUnit<SeatId>(north, new string('n', 201))).Code.Should().Be(TenancyRefusals.NameInvalid);
    }

    [Fact]
    public void Each_change_raises_one_event_with_ids_only()
    {
        var organization = New.Organization();
        var tenant = organization.Id;
        var root = organization.Root.Id;
        var scenario = organization.AsScenario().IgnorePendingEvents();
        var north = OrganizationUnitId.CreateSequential();
        var south = OrganizationUnitId.CreateSequential();

        scenario.When(tree => tree.AddUnit<SeatId>(north, root, "North", Tree))
            .RaisedExactly<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>()
            .SingleEvent<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>()
            .Should().Match<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>(added =>
                added.TenantId == tenant && added.UnitId == north && added.ParentId == root);
        scenario.When(tree => tree.AddUnit<SeatId>(south, root, "South", Tree));

        scenario.When(tree => tree.RenameUnit<SeatId>(north, "Northern"))
            .RaisedExactly<OrganizationUnitRenamed<TenantId, OrganizationUnitId, SeatId>>()
            .SingleEvent<OrganizationUnitRenamed<TenantId, OrganizationUnitId, SeatId>>()
            .Should().Match<OrganizationUnitRenamed<TenantId, OrganizationUnitId, SeatId>>(renamed => renamed.TenantId == tenant && renamed.UnitId == north);

        scenario.When(tree => tree.MoveUnit<SeatId>(north, south))
            .RaisedExactly<OrganizationUnitMoved<TenantId, OrganizationUnitId, SeatId>>()
            .SingleEvent<OrganizationUnitMoved<TenantId, OrganizationUnitId, SeatId>>()
            .Should().Match<OrganizationUnitMoved<TenantId, OrganizationUnitId, SeatId>>(moved =>
                moved.TenantId == tenant && moved.UnitId == north && moved.FromParentId == root && moved.ToParentId == south);

        scenario.When(tree => tree.ArchiveUnit<SeatId>(north))
            .RaisedExactly<OrganizationUnitArchived<TenantId, OrganizationUnitId, SeatId>>()
            .SingleEvent<OrganizationUnitArchived<TenantId, OrganizationUnitId, SeatId>>()
            .Should().Match<OrganizationUnitArchived<TenantId, OrganizationUnitId, SeatId>>(archived => archived.TenantId == tenant && archived.UnitId == north);

        scenario.When(tree => tree.Rename<SeatId>("Harbor Works North"))
            .RaisedExactly<OrganizationRenamed<TenantId, SeatId>>()
            .SingleEvent<OrganizationRenamed<TenantId, SeatId>>().TenantId.Should().Be(tenant);
        organization.Name.Should().Be("Harbor Works North");
    }

    [Fact]
    public void A_tree_broken_behind_the_methods_back_is_reported_and_never_loops()
    {
        var organization = New.Organization();
        var north = Add(organization, organization.Root.Id, "North");
        var northCoast = Add(organization, north, "North Coast");

        // North under North Coast, which is under North: what no method allows, written as a bad import would.
        typeof(OrganizationUnitEntity<OrganizationUnitId>).GetProperty(nameof(HostUnit.ParentId))!
            .GetSetMethod(nonPublic: true)!.Invoke(organization.FindUnit(north), [(OrganizationUnitId?)northCoast]);

        organization.GetInvariantViolations().Select(violation => violation.Code).Should().Contain(TenancyRefusals.Cycle);
        organization.AncestorsOf(northCoast).Should().Equal(northCoast, north);
        organization.SubtreeOf(north).Should().BeEquivalentTo([north, northCoast]);
        organization.HeightOf(north).Should().Be(2);
    }

    [Fact]
    public void A_valid_tree_breaks_no_invariant()
    {
        var harbor = new HarborBuilder().Build();
        var organization = harbor.Organization;

        organization.GetInvariantViolations().Should().BeEmpty();
        organization.DepthOf(harbor.Root).Should().Be(1);
        organization.DepthOf(harbor.NorthCoast).Should().Be(3);
        organization.AncestorsOf(harbor.NorthCoast).Should().Equal(harbor.NorthCoast, harbor.North, harbor.Root);
        organization.SubtreeOf(harbor.North).Should().Equal(harbor.North, harbor.NorthCoast);
        organization.SubtreeOf(harbor.Root).Should().BeEquivalentTo([harbor.Root, harbor.North, harbor.NorthCoast, harbor.South]);
        organization.HeightOf(harbor.Root).Should().Be(3);
        organization.HeightOf(harbor.South).Should().Be(1);
        organization.IsActiveUnit(harbor.South).Should().BeTrue();
        organization.FindUnit(OrganizationUnitId.CreateSequential()).Should().BeNull();
    }
}
