namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The rows the access questions read are worked out from the aggregates by pure functions: the closure of
/// the tree, every ancestor with every descendant, and one right per unit, role and key, which only an active
/// seat with an active role and a live key has.
/// </summary>
public class ProjectionTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private static (OrganizationUnitId Upper, OrganizationUnitId Lower, int Distance)[] Pairs(HostOrganization organization)
        => [.. TenancyProjection.ClosureOf(organization).Select(path => (path.AncestorId, path.DescendantId, path.Distance))];

    [Fact]
    public void The_closure_has_every_ancestor_descendant_pair_with_its_distance()
    {
        var harbor = new HarborBuilder().Build();
        var (root, north, northCoast, south) = (harbor.Root, harbor.North, harbor.NorthCoast, harbor.South);

        var closure = TenancyProjection.ClosureOf(harbor.Organization);

        closure.Select(path => (path.AncestorId, path.DescendantId, path.Distance)).Should().BeEquivalentTo(
        [
            (root, root, 0), (north, north, 0), (northCoast, northCoast, 0), (south, south, 0),
            (root, north, 1), (root, south, 1), (north, northCoast, 1),
            (root, northCoast, 2),
        ]);
        closure.Should().OnlyContain(path => path.TenantId == harbor.Tenant.Id);
    }

    [Fact]
    public void A_move_changes_only_the_pairs_of_the_moved_subtree()
    {
        var harbor = new HarborBuilder().Build();
        var organization = harbor.Organization;
        var southBay = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), harbor.South, "South Bay", TenantShape.Hierarchical).Id;
        var before = Pairs(organization);

        organization.MoveUnit<SeatId>(harbor.North, southBay);

        var after = Pairs(organization);
        var moved = new[] { harbor.North, harbor.NorthCoast };
        after.Where(pair => !moved.Contains(pair.Lower)).Should().BeEquivalentTo(
            before.Where(pair => !moved.Contains(pair.Lower)), "no pair of a unit outside the moved subtree changes");
        after.Except(before).Should().BeEquivalentTo(
        [
            (southBay, harbor.North, 1), (harbor.South, harbor.North, 2), (harbor.Root, harbor.North, 3),
            (southBay, harbor.NorthCoast, 2), (harbor.South, harbor.NorthCoast, 3), (harbor.Root, harbor.NorthCoast, 4),
        ]);
        before.Except(after).Should().BeEquivalentTo([(harbor.Root, harbor.North, 1), (harbor.Root, harbor.NorthCoast, 2)]);
    }

    [Fact]
    public void The_closure_of_a_cyclic_tree_throws_instead_of_looping()
    {
        var harbor = new HarborBuilder().Build();

        // North under North Coast, which is under North: what no method allows, written as a bad import would.
        typeof(OrganizationUnitEntity<OrganizationUnitId>).GetProperty(nameof(HostUnit.ParentId))!
            .GetSetMethod(nonPublic: true)!.Invoke(harbor.Organization.FindUnit(harbor.North), [(OrganizationUnitId?)harbor.NorthCoast]);

        FluentActions.Invoking(() => TenancyProjection.ClosureOf(harbor.Organization))
            .Should().Throw<InvalidOperationException>().WithMessage("*above itself*");
    }

    [Fact]
    public void Rights_are_one_row_per_unit_role_and_key_with_the_grant_period()
    {
        var harbor = new HarborBuilder().Build();
        var seat = harbor.NewSeat("Bert");
        var operatorRole = harbor.RolesByPack[HostCatalogue.OperatorPack];
        var watcher = harbor.RolesByPack[HostCatalogue.WatcherPack];
        var ends = Now.AddDays(30);
        seat.Place(harbor.North, primary: true, Now, placedBy: null);
        seat.Place(harbor.South, primary: false, Now, placedBy: null);
        seat.Grant(harbor.North, operatorRole.Id, operatorRole.Facts, GrantPeriod.Between(Now, ends), null, null);
        seat.Grant(harbor.North, watcher.Id, watcher.Facts, GrantPeriod.Open(Now), null, null);
        seat.Grant(harbor.South, watcher.Id, watcher.Facts, GrantPeriod.Open(Now.AddDays(1)), null, null);

        var rights = TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), harbor.FactsOf, harbor.Catalogue);

        rights.Select(right => (right.UnitId, right.RoleId, right.Key, right.StartsAt, right.EndsAt)).Should().BeEquivalentTo(
        [
            (harbor.North, operatorRole.Id, HostCatalogue.WidgetChange, Now, (DateTimeOffset?)ends),
            (harbor.North, operatorRole.Id, HostCatalogue.WidgetCreate, Now, (DateTimeOffset?)ends),
            (harbor.North, operatorRole.Id, HostCatalogue.WidgetRead, Now, (DateTimeOffset?)ends),
            (harbor.North, watcher.Id, HostCatalogue.WidgetRead, Now, (DateTimeOffset?)null),
            (harbor.South, watcher.Id, HostCatalogue.WidgetRead, Now.AddDays(1), (DateTimeOffset?)null),
        ]);
        rights.Should().OnlyContain(right => right.TenantId == seat.TenantId && right.SeatId == seat.Id);
    }

    [Fact]
    public void A_seat_that_is_not_active_has_no_rights()
    {
        var harbor = new HarborBuilder().Build();
        var administrator = harbor.Administrator;
        var grants = TenancyProjection.GrantsOf(administrator);

        TenancyProjection.RightsOf(administrator.TenantId, administrator.Id, SeatStatus.Active, grants, harbor.FactsOf, harbor.Catalogue)
            .Should().HaveCount(harbor.Catalogue.LiveKeys.Count, "the administrators' role holds every live key, at the root");

        TenancyProjection.RightsOf(administrator.TenantId, administrator.Id, SeatStatus.Suspended, grants, harbor.FactsOf, harbor.Catalogue).Should().BeEmpty();
        TenancyProjection.RightsOf(administrator.TenantId, administrator.Id, SeatStatus.Deactivated, grants, harbor.FactsOf, harbor.Catalogue).Should().BeEmpty();
    }

    [Fact]
    public void An_archived_role_conveys_nothing()
    {
        var harbor = new HarborBuilder().Build();
        var seat = harbor.NewSeat("Bert");
        var watcher = harbor.RolesByPack[HostCatalogue.WatcherPack];
        seat.Place(harbor.North, primary: true, Now, placedBy: null);
        seat.Grant(harbor.North, watcher.Id, watcher.Facts, GrantPeriod.Open(Now), null, null);

        watcher.Archive<SeatId>();

        TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), harbor.FactsOf, harbor.Catalogue).Should().BeEmpty();
        TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), _ => null, harbor.Catalogue)
            .Should().BeEmpty("a role that is not found conveys nothing either");
    }

    [Fact]
    public void A_retired_key_conveys_nothing()
    {
        var gauge = new Permission("gauges.read", "Gauges", "Read gauges");
        var before = New.Catalogue(gauge);
        var after = New.Catalogue(gauge with { Retired = true });
        var role = New.Role(before, "Gauge reader", "gauges.read", HostCatalogue.WidgetRead);
        var seat = New.Seat();
        var unit = OrganizationUnitId.CreateSequential();
        seat.Place(unit, primary: true, Now, placedBy: null);
        seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Open(Now), null, null);

        TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), _ => role.Facts, before)
            .Select(right => right.Key).Should().BeEquivalentTo(["gauges.read", HostCatalogue.WidgetRead]);
        TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), _ => role.Facts, after)
            .Select(right => right.Key).Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public void A_key_the_catalogue_no_longer_knows_conveys_nothing()
    {
        var withGauges = New.Catalogue(new Permission("gauges.read", "Gauges", "Read gauges"));
        var withoutGauges = New.Catalogue();
        var role = New.Role(withGauges, "Gauge reader", "gauges.read", HostCatalogue.WidgetRead);
        var seat = New.Seat();
        var unit = OrganizationUnitId.CreateSequential();
        seat.Place(unit, primary: true, Now, placedBy: null);
        seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Open(Now), null, null);

        withoutGauges.Knows("gauges.read").Should().BeFalse();
        TenancyProjection.RightsOf(seat.TenantId, seat.Id, seat.Status, TenancyProjection.GrantsOf(seat), _ => role.Facts, withoutGauges)
            .Select(right => right.Key).Should().Equal(HostCatalogue.WidgetRead);
    }
}
