namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The fields an application adds to its tenant and seat classes are set while the tenant is provisioned:
/// after the instances are made and before the tenant is activated, so they are saved with everything else,
/// and a callback that throws leaves nothing behind.
/// </summary>
public class ProvisioningHooksTests
{
    private static HostTenancy.TenantToProvision Harbor(Action<HostTenant>? configureTenant = null, Action<HostSeat>? configureFirstSeat = null)
        => new(
            "harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", "company", Guid.NewGuid(), "Ada",
            ConfigureTenant: configureTenant,
            ConfigureFirstSeat: configureFirstSeat);

    private static Task<HostTenancy.ProvisionedTenant> Provision(Harness harness, HostTenancy.TenantToProvision command)
        => harness.Run(HostCaller.System, h => h.Tenants.ProvisionAsync(command, default));

    [Fact]
    public async Task The_host_fields_set_at_provisioning_are_saved_with_the_tenant()
    {
        var harness = new Harness(New.Catalogue());

        var provisioned = await Provision(harness, Harbor(
            tenant =>
            {
                tenant.MarkAsDemo();
                tenant.AddNote("Seeded for the demonstration.");
            },
            seat => seat.ChangeJobTitle("Harbor master")));

        harness.Store.SaveCount.Should().Be(1, "the application's fields go in the save that provisions");
        var tenant = harness.Store.Tenant(provisioned.Tenant);
        tenant.IsDemo.Should().BeTrue();
        tenant.Notes.Should().ContainSingle().Which.Text.Should().Be("Seeded for the demonstration.");
        tenant.Status.Should().Be(TenantStatus.Active);
        harness.Store.Seat(provisioned.AdminSeat).JobTitle.Should().Be("Harbor master");
    }

    [Fact]
    public async Task The_hooks_see_the_tenant_before_it_is_activated_and_the_first_seat_as_an_administrator()
    {
        var harness = new Harness(New.Catalogue());
        var seen = new List<string>();

        var provisioned = await Provision(harness, Harbor(
            tenant => seen.Add("tenant " + tenant.Slug.Value + " " + tenant.Status),
            seat => seen.Add("seat " + seat.DisplayName + " with " + seat.Placements.Single().Grants.Count + " grant at its " + seat.Placements.Count + " placement")));

        seen.Should().Equal(
            ["tenant harbor Provisioning", "seat Ada with 1 grant at its 1 placement"],
            "the tenant's callback runs first, each runs once, and neither sees a tenant in use yet");
        harness.Store.Tenant(provisioned.Tenant).Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task An_event_raised_in_a_hook_goes_out_with_the_provisioning()
    {
        var harness = new Harness(New.Catalogue());

        var provisioned = await Provision(harness, Harbor(configureFirstSeat: seat => seat.Rename("Ada Harbor")));

        harness.Store.Seat(provisioned.AdminSeat).DisplayName.Should().Be("Ada Harbor");
        var events = harness.Store.SavedEvents.Select(saved => saved.GetType().GetGenericTypeDefinition()).ToList();
        events.Should().Contain(typeof(SeatRenamed<,>));
        events.IndexOf(typeof(SeatRenamed<,>)).Should().BeGreaterThan(events.IndexOf(typeof(SeatAdded<,>)), "the seat was added before the callback changed it");
        harness.Store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_hook_that_throws_leaves_nothing_saved()
    {
        var harness = new Harness(New.Catalogue());

        await FluentActions.Awaiting(() => Provision(harness, Harbor(configureTenant: _ => throw new InvalidOperationException("no such plan"))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such plan");
        await FluentActions.Awaiting(() => Provision(harness, Harbor(configureFirstSeat: _ => throw new InvalidOperationException("no such title"))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such title");

        harness.Store.SaveCount.Should().Be(0);
        harness.Store.HasTenant(new TenantId(101)).Should().BeFalse();
        harness.Store.HasTenant(new TenantId(102)).Should().BeFalse();

        // The slug was never taken: the same tenant is provisioned once the callback holds.
        var provisioned = await Provision(harness, Harbor(tenant => tenant.MarkAsDemo()));
        harness.Store.Tenant(provisioned.Tenant).IsDemo.Should().BeTrue();
    }

    [Fact]
    public async Task A_refusal_from_a_hook_stops_the_provisioning_as_a_refusal()
    {
        var harness = new Harness(New.Catalogue());

        // The application's own rule about its own field, checked where the field is set.
        await Refused.WithCodeAsync(TenancyRefusals.NameInvalid, () => Provision(harness, Harbor(configureFirstSeat: seat => seat.Rename(" "))));

        harness.Store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Without_hooks_provisioning_is_as_it_was()
    {
        var harness = new Harness(New.Catalogue());

        var provisioned = await Provision(harness, Harbor());

        harness.Store.Tenant(provisioned.Tenant).IsDemo.Should().BeFalse();
        harness.Store.Seat(provisioned.AdminSeat).JobTitle.Should().BeNull();
    }
}
