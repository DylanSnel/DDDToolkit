using System.Reflection;
using DDDToolkit.Interfaces;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The package's parents and the application's classes, wired by the generator: the host writes one-line
/// classes (or classes with members of its own only), and gets the package's aggregates closed over its own
/// ids, made by the package, with the package's rules running first and its own after.
/// </summary>
public class TemplateWiringTests
{
    [Fact]
    public void Host_classes_derive_from_the_parents_closed_over_the_hosts_ids()
    {
        typeof(HostSeat).BaseType.Should().Be(typeof(SeatAggregate<SeatId, TenantId, OrganizationUnitId, RoleId>));
        typeof(HostTenant).BaseType.Should().Be(typeof(TenantAggregate<TenantId>));
        typeof(HostOrganization).BaseType.Should().Be(typeof(OrganizationAggregate<TenantId, HostUnit, OrganizationUnitId>));
        typeof(HostUnit).BaseType.Should().Be(typeof(OrganizationUnitEntity<OrganizationUnitId>));
        typeof(HostRole).BaseType.Should().Be(typeof(RoleAggregate<RoleId, TenantId>));

        // The one class an application may leave out: its own id first, the other four taken from the classes above.
        typeof(HostInvitation).BaseType.Should().Be(typeof(InvitationAggregate<InvitationId, TenantId, OrganizationUnitId, RoleId, SeatId>));
    }

    [Fact]
    public void Host_instances_are_created_through_the_generated_constructor()
    {
        var identity = Guid.NewGuid();
        var id = SeatId.CreateSequential();

        var seat = TenancyInstances.NewSeat<HostSeat, SeatId, TenantId, OrganizationUnitId, RoleId>(id, new TenantId(7), identity, " Ada ");

        seat.Id.Should().Be(id);
        seat.TenantId.Should().Be(new TenantId(7));
        seat.Identity.Should().Be(identity);
        seat.DisplayName.Should().Be("Ada", "names are trimmed");
        seat.Status.Should().Be(SeatStatus.Active);
        seat.PendingEvents().RaisedExactly<SeatAdded<TenantId, SeatId>>()
            .SingleEvent<SeatAdded<TenantId, SeatId>>().Should().Match<SeatAdded<TenantId, SeatId>>(added => added.TenantId == new TenantId(7) && added.SeatId == id);

        foreach (var oneLiner in new[] { typeof(HostOrganization), typeof(HostRole), typeof(HostSeat), typeof(HostTenant), typeof(HostUnit), typeof(HostInvitation) })
        {
            oneLiner.GetConstructors().Should().BeEmpty(oneLiner.Name + " declares no constructor, and the generated one is not public");
            oneLiner.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes).Should().NotBeNull();
        }

        New.Role(New.Catalogue(), "Watcher", HostCatalogue.WidgetRead).Should().BeOfType<HostRole>();
        New.Organization().Root.Should().BeOfType<HostUnit>("the organization makes the root through the host's own unit class");
    }

    [Fact]
    public void A_class_the_package_cannot_make_is_named_with_the_way_out()
    {
        FluentActions.Invoking(() => TenancyInstances.NewTenant<HandWrittenTenant, TenantId, SeatId>(new TenantId(1), TenantSlug.Create("harbor").ToValid(), TenantShape.Flat))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*HandWrittenTenant*Declare it with its template attribute*");
    }

    [Fact]
    public void The_packages_rules_run_before_the_hosts_and_name_the_host_class()
    {
        var seat = New.Seat();
        seat.ChangeJobTitle(new string('x', HostSeat.MaxJobTitleLength + 1));
        Break(seat, "DisplayName", string.Empty);

        var violations = seat.GetInvariantViolations();

        violations.Select(violation => violation.Code).Should().Equal(TenancyRefusals.NameInvalid, "host.seat.job-title");
        violations.Should().OnlyContain(violation => violation.EntityType == typeof(HostSeat));
        violations[0].Arguments["What"].Should().Be("display-name");
    }

    [Fact]
    public void A_host_rule_about_its_own_field_runs()
    {
        var seat = New.Seat();
        seat.GetInvariantViolations().Should().BeEmpty();

        seat.ChangeJobTitle(new string('x', HostSeat.MaxJobTitleLength + 1));

        var violation = seat.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("host.seat.job-title");
        violation.EntityId.Should().Be(seat.Id);
    }

    [Fact]
    public void A_host_entity_on_a_template_class_is_walked_by_its_rules()
    {
        var tenant = New.Tenant();
        tenant.AddNote("Fine.");
        tenant.GetInvariantViolations().Should().BeEmpty();

        var blank = tenant.AddNote("  ");

        var violation = tenant.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("host.tenant.note");
        violation.EntityType.Should().Be<TenantNote>();
        violation.EntityId.Should().Be(blank.Id);
    }

    [Fact]
    public void The_organization_walks_the_hosts_units()
    {
        var organization = New.Organization();
        var north = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), organization.Root.Id, "North", TenantShape.Hierarchical);
        north.Should().BeOfType<HostUnit>();

        north.SetCostCentre("NL-001");
        organization.GetInvariantViolations().Should().BeEmpty();

        north.SetCostCentre("north");

        var violation = organization.GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("host.unit.cost-centre");
        violation.EntityType.Should().Be<HostUnit>();
        violation.EntityId.Should().Be(north.Id);
    }

    [Fact]
    public void Package_mutators_are_not_virtual()
    {
        var parents = new[]
        {
            typeof(TenantAggregate<>), typeof(OrganizationAggregate<,,>), typeof(OrganizationUnitEntity<>),
            typeof(SeatAggregate<,,,>), typeof(RoleAggregate<,>), typeof(InvitationAggregate<,,,,>),
        };

        var introduced = parents
            .SelectMany(parent => parent.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.IsAbstract || (method.IsVirtual && !method.IsFinal && method.GetBaseDefinition() == method))
            .Select(method => method.DeclaringType!.Name + "." + method.Name);

        introduced.Should().BeEmpty("a host adds behaviour with members, rules and events of its own; it never overrides what the package decides");
    }

    /// <summary>A tenant class derived by hand, with only a constructor the package cannot call.</summary>
    private sealed class HandWrittenTenant : TenantAggregate<TenantId>
    {
        public HandWrittenTenant(string origin) => Origin = origin;

        public string Origin { get; }
    }

    /// <summary>Puts a parent's private property in a state its methods never would, to see the rule catch it.</summary>
    private static void Break(object aggregate, string property, object? value)
    {
        var declared = aggregate.GetType().BaseType!.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!;
        declared.GetSetMethod(nonPublic: true)!.Invoke(aggregate, [value]);
    }
}
