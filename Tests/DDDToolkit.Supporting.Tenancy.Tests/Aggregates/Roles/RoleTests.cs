using DDDToolkit.Exceptions;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A role's keys come from the catalogue, expanded, distinct and sorted. A key retired or removed later may
/// stay where it is, and is never added anew.
/// </summary>
public class RoleTests
{
    private const string Polish = "widget.polish";

    /// <summary>The host's catalogue with one more key, <see cref="Polish"/>, live or retired.</summary>
    private static TenancyCatalogue WithPolish(bool retired)
        => TenancyCatalogue.Build(HostCatalogue.Application, [new Permission(Polish, "Widgets", "Polish widgets", Retired: retired)]);

    [Fact]
    public void Keys_come_from_the_catalogue_only()
    {
        var catalogue = New.Catalogue();

        var refusal = Refused.With(TenancyRefusals.UnknownPermission, () => New.Role(catalogue, "Flyer", HostCatalogue.WidgetRead, "widget.fly", "nothing"));
        refusal.Arguments["Keys"].Should().Be("widget.fly, nothing");
        refusal.Message.Should().Be("These keys are unknown or retired: widget.fly, nothing.");

        var role = New.Role(catalogue, "Watcher", HostCatalogue.WidgetRead);
        role.AsScenario().WhenThrows<RefusalException>(candidate => candidate.SetKeys<SeatId>(["widget.fly"], catalogue))
            .Code.Should().Be(TenancyRefusals.UnknownPermission);
        role.Keys.Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public void Implied_keys_are_expanded_sorted_and_distinct()
    {
        var role = New.Role(New.Catalogue(), "Operator", HostCatalogue.WidgetCreate, " widget.change ", HostCatalogue.WidgetChange);

        role.Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        role.Holds(HostCatalogue.WidgetRead).Should().BeTrue("widget.change implies it");
        role.Facts.Should().BeEquivalentTo(new RoleFacts(true, role.Keys));
        role.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_retired_key_cannot_be_added()
    {
        var catalogue = WithPolish(retired: true);

        Refused.With(TenancyRefusals.UnknownPermission, () => New.Role(catalogue, "Polisher", Polish)).Arguments["Keys"].Should().Be(Polish);

        var role = New.Role(catalogue, "Watcher", HostCatalogue.WidgetRead);
        Refused.With(TenancyRefusals.UnknownPermission, () => role.SetKeys<SeatId>([HostCatalogue.WidgetRead, Polish], catalogue));
    }

    [Fact]
    public void A_role_keeping_a_retired_key_may_be_saved_again()
    {
        var role = New.Role(WithPolish(retired: false), "Polisher", Polish, HostCatalogue.WidgetRead);
        role.DrainEvents();
        var retired = WithPolish(retired: true);

        role.AsScenario().When(candidate => candidate.SetKeys<SeatId>(role.Keys.ToArray(), retired)).RaisedNothing();
        role.Keys.Should().Contain(Polish);

        role.AsScenario().When(candidate => candidate.SetKeys<SeatId>([Polish, HostCatalogue.WidgetChange], retired))
            .RaisedExactly<RoleKeysChanged<TenantId, RoleId, SeatId>>();
        role.Keys.Should().Equal(HostCatalogue.WidgetChange, Polish, HostCatalogue.WidgetRead);
        role.GetInvariantViolations().Should().BeEmpty();

        role.SetKeys<SeatId>([HostCatalogue.WidgetRead], retired);
        role.Keys.Should().Equal(HostCatalogue.WidgetRead);
        Refused.With(TenancyRefusals.UnknownPermission, () => role.SetKeys<SeatId>([Polish], retired));
    }

    [Fact]
    public void A_key_removed_from_the_catalogue_may_stay_on_its_role()
    {
        var role = New.Role(WithPolish(retired: false), "Polisher", Polish);
        var removed = New.Catalogue();
        removed.Knows(Polish).Should().BeFalse();

        role.SetKeys<SeatId>([Polish, HostCatalogue.WidgetRead], removed);

        role.Keys.Should().Equal(Polish, HostCatalogue.WidgetRead);
        role.Holds(Polish).Should().BeTrue("the role keeps the key; it grants nothing, because the catalogue has no such key");
        removed.IsLive(Polish).Should().BeFalse();
    }

    [Fact]
    public void An_archived_role_refuses_new_keys_and_a_second_archive()
    {
        var catalogue = New.Catalogue();
        var scenario = New.Role(catalogue, "Watcher", HostCatalogue.WidgetRead).AsScenario().IgnorePendingEvents();

        scenario.When(role => role.Archive<SeatId>())
            .RaisedExactly<RoleArchived<TenantId, RoleId, SeatId>>();
        scenario.Subject.Status.Should().Be(RoleStatus.Archived);
        scenario.Subject.Facts.IsActive.Should().BeFalse();

        scenario.WhenThrows<RefusalException>(role => role.SetKeys<SeatId>([HostCatalogue.WidgetChange], catalogue)).Code.Should().Be(TenancyRefusals.RoleArchived);
        scenario.WhenThrows<RefusalException>(role => role.Archive<SeatId>()).Code.Should().Be(TenancyRefusals.RoleArchived);
        scenario.WhenThrows<RefusalException>(role => role.Rename<SeatId>("Former watcher", "")).Code.Should().Be(TenancyRefusals.RoleArchived);
    }

    [Fact]
    public void A_name_is_1_to_120_characters()
    {
        var catalogue = New.Catalogue();

        var blank = Refused.With(TenancyRefusals.NameInvalid, () => New.Role(catalogue, "  ", HostCatalogue.WidgetRead));
        blank.Arguments["What"].Should().Be("role-name");
        blank.Arguments["Min"].Should().Be(1);
        blank.Arguments["Max"].Should().Be(120);
        blank.Message.Should().Be("Enter 1 to 120 characters.");
        Refused.With(TenancyRefusals.NameInvalid, () => New.Role(catalogue, new string('r', 121), HostCatalogue.WidgetRead));

        var role = New.Role(catalogue, " " + new string('r', 120) + " ", HostCatalogue.WidgetRead);
        role.Name.Should().HaveLength(120);

        Refused.With(TenancyRefusals.NameInvalid, () => role.Rename<SeatId>("Watcher", new string('d', 1001))).Arguments["What"].Should().Be("role-description");

        role.DrainEvents();
        role.AsScenario().When(candidate => candidate.Rename<SeatId>(" Watcher ", " Looks at widgets "))
            .RaisedExactly<RoleRenamed<TenantId, RoleId, SeatId>>();
        role.Name.Should().Be("Watcher");
        role.Description.Should().Be("Looks at widgets");
        role.AsScenario().When(candidate => candidate.Rename<SeatId>("Watcher", "Looks at widgets")).RaisedNothing();
    }

    [Fact]
    public void Pack_provenance_is_set_at_creation_only()
    {
        var catalogue = New.Catalogue();
        var pack = catalogue.Packs.Single(candidate => candidate.Key == HostCatalogue.WatcherPack);

        var role = TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), new TenantId(1), new RoleDraft(pack.Name, pack.Description, pack.Keys, pack.Key), catalogue);

        role.FromPack.Should().Be(HostCatalogue.WatcherPack);
        role.PendingEvents().RaisedExactly<RoleCreated<TenantId, RoleId, SeatId>>()
            .SingleEvent<RoleCreated<TenantId, RoleId, SeatId>>().FromPack.Should().Be(HostCatalogue.WatcherPack);

        role.Rename<SeatId>("Looker", "Looks");
        role.SetKeys<SeatId>([HostCatalogue.WidgetChange], catalogue);
        role.FromPack.Should().Be(HostCatalogue.WatcherPack, "the copy is the tenant's own now, and still says where it came from");
        typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(role.FromPack))!.SetMethod!.IsPrivate.Should().BeTrue();

        New.Role(catalogue, "By hand", HostCatalogue.WidgetRead).FromPack.Should().BeNull();
    }

    [Fact]
    public void Setting_the_same_keys_raises_nothing()
    {
        var catalogue = New.Catalogue();
        var role = New.Role(catalogue, "Operator", HostCatalogue.WidgetChange);
        role.DrainEvents();

        role.AsScenario().When(candidate => candidate.SetKeys<SeatId>([HostCatalogue.WidgetChange], catalogue))
            .RaisedNothing();
        role.AsScenario().When(candidate => candidate.SetKeys<SeatId>([HostCatalogue.WidgetRead, HostCatalogue.WidgetChange, HostCatalogue.WidgetChange], catalogue))
            .RaisedNothing();

        role.AsScenario().When(candidate => candidate.SetKeys<SeatId>([HostCatalogue.WidgetRead], catalogue))
            .RaisedExactly<RoleKeysChanged<TenantId, RoleId, SeatId>>();
    }
}
