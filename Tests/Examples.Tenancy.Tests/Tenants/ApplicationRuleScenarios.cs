using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants;

/// <summary>
/// The application's own rules on the package's classes: a unit's cost centre and a seat's job title. They run on
/// every save, after the package's own rules and next to them, and refuse a save that breaks them as any rule of
/// the toolkit does. And the application's own decision about the catalogue: which keys manage access.
/// </summary>
/// <remarks>
/// No route changes either field, so the tests change them as the application's own code would: as system work in
/// harbor, through the Tenants module's context. Each test saves, so each makes a host of its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ApplicationRuleScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_keys_that_manage_access_are_exactly_these()
    {
        await using var host = await sample.StartAsync();

        // Written out, key by key, on purpose. Which keys manage access is an access decision: a role that holds
        // one is given only by a seat that holds it, never to oneself, and only an administrator changes such a
        // role. Marking another key, or taking a mark away, changes who may give which role in every tenant. On a
        // database that checks rows it also changes the policies, so it needs the access file exported again, a
        // migration to apply it, and the export's check to pass. A change to this list is that decision, made
        // where it can be seen; it is never a side effect of a key being added somewhere.
        host.Services.GetRequiredService<TenancyCatalogue>().AccessManagingKeys.Should().BeEquivalentTo(
        [
            "tenancy.settings.manage",
            "tenancy.units.manage",
            "tenancy.seats.manage",
            "tenancy.grants.manage",
            "tenancy.roles.manage",
            "projects.owner.change",
            "projects.crew.manage",
        ]);

        // And the administrators' pack that lists its keys holds every one of them, or the host would not start.
        host.Services.GetRequiredService<TenancyCatalogue>().Packs.Single(pack => pack.Key == SampleCatalogue.AccessAdmin).Keys
            .Should().Contain(host.Services.GetRequiredService<TenancyCatalogue>().AccessManagingKeys);
    }

    [Fact]
    public async Task A_cost_centre_out_of_its_format_is_refused_on_save()
    {
        await using var host = await sample.StartAsync();
        var north = Harbor.UnitNamed("North");

        var refused = await FluentActions.Awaiting(() => ChangeAsync(host, async tenancy =>
            {
                var organization = await tenancy.Organizations.SingleAsync(candidate => candidate.Id == Harbor.Id, Cancellation);
                organization.FindUnit(north)!.SetCostCentre("bad");
            }))
            .Should().ThrowAsync<InvariantViolationException>();

        refused.Which.InvariantViolations.Select(violation => violation.Code).Should().Equal(OrganizationUnit.CostCentreFormat.ViolationCode);

        // One that keeps the format is saved, and read back as it was written.
        await ChangeAsync(host, async tenancy =>
        {
            var organization = await tenancy.Organizations.SingleAsync(candidate => candidate.Id == Harbor.Id, Cancellation);
            organization.FindUnit(north)!.SetCostCentre("NO-101");
        });

        var saved = await ReadAsync(host, async tenancy =>
            (await tenancy.Organizations.SingleAsync(candidate => candidate.Id == Harbor.Id, Cancellation)).FindUnit(north)!.CostCentre);
        saved.Should().Be("NO-101");
    }

    [Fact]
    public async Task A_job_title_longer_than_80_characters_is_refused_on_save()
    {
        await using var host = await sample.StartAsync();
        var rhea = Harbor.SeatOf(DemoPeople.Rhea);

        var refused = await FluentActions.Awaiting(() => ChangeAsync(host, async tenancy =>
            {
                var seat = await tenancy.Seats.SingleAsync(candidate => candidate.Id == rhea, Cancellation);
                seat.ChangeJobTitle(new string('x', Seat.MaxJobTitleLength + 1));
            }))
            .Should().ThrowAsync<InvariantViolationException>();

        var violation = refused.Which.InvariantViolations.Should().ContainSingle().Which;
        violation.Code.Should().Be(Seat.JobTitleLength.ViolationCode);
        violation.Arguments["Max"].Should().Be(Seat.MaxJobTitleLength);

        await ChangeAsync(host, async tenancy =>
        {
            var seat = await tenancy.Seats.SingleAsync(candidate => candidate.Id == rhea, Cancellation);
            seat.ChangeJobTitle("Area manager, North");
        });

        var saved = await ReadAsync(host, async tenancy =>
            (await tenancy.Seats.SingleAsync(candidate => candidate.Id == rhea, Cancellation)).JobTitle);
        saved.Should().Be("Area manager, North");
    }

    /// <summary>Makes a change in a unit of work of its own, as system work in harbor, and saves it.</summary>
    private static async Task ChangeAsync(SampleFactory host, Func<TenantsContext, Task> change)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id, Harbor.Administrator.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var tenancy = scope.ServiceProvider.GetRequiredService<TenantsContext>();
            await change(tenancy);
            await tenancy.SaveChangesAsync(Cancellation);
        }
    }

    /// <summary>Reads something back in a unit of work of its own, as system work in harbor.</summary>
    private static async Task<T> ReadAsync<T>(SampleFactory host, Func<TenantsContext, Task<T>> read)
    {
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor.Id, Harbor.Administrator.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await read(scope.ServiceProvider.GetRequiredService<TenantsContext>());
        }
    }
}
