using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Each of Tenancy's unique indexes says in the mapping which refusal a save that breaks it gets. A use case
/// checks the rule first; when another command gets in between its check and its save, the index gives the answer
/// the check would have, with nothing registered to translate it, on SQLite and on Postgres alike.
/// </summary>
public abstract class IndexRefusalTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Tenancys_unique_indexes_answer_their_refusals_from_the_index()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", harbor.RootUnit);
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().PlaceAsync(grace, north, primary: false, Cancellation));
        _services.Provider.GetServices<ITenancySaveFailures>().Should().BeEmpty("no translator is registered: the indexes answer for themselves");

        // A tenant's slug. Orchard is provisioned by another command while this one saves: the check that the slug
        // is free passed, and the index refuses the save.
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            _services.Hook.BeforeSave(scope.ServiceProvider.Tenancy(), () => _services.ProvisionAsync("orchard"));

            var refusal = await Refused.WithCodeAsync(TenancyRefusals.SlugTaken, () => scope.ServiceProvider.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision("orchard", "Second Orchard", TenantShape.Flat, "Orchard", Guid.NewGuid(), "Dan"),
                Cancellation));
            Same(refusal, TenancyRefusals.Of(TenancyRefusals.SlugTaken, ("Slug", "orchard")));
            refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for, so a log of it names the index");
        }

        _services.Hook.Fired.Should().BeTrue();

        // A person's seat in a tenant.
        var bert = Guid.NewGuid();
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            _services.Hook.BeforeSave(services.Tenancy(), () => _services.AddSeatAsync(harbor.Tenant, bert, "Bert"));

            Same(
                await Refused.WithCodeAsync(TenancyRefusals.IdentityHasSeat, () => services.Seats().AddSeatAsync(bert, "Bert again", Cancellation)),
                TenancyRefusals.Of(TenancyRefusals.IdentityHasSeat));
        });

        // A role's name in a tenant, ignoring case: the index is on the normalized name, and the refusal names the
        // name as the role has it.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            _services.Hook.BeforeSave(services.Tenancy(), () => _services.BySystemIn(harbor.Tenant, racing =>
                racing.Roles().CreateAsync("Élan", "Keeps going", [HostCatalogue.WidgetRead], Cancellation)));

            Same(
                await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => services.Roles().CreateAsync(" élan ", "Shouts", [HostCatalogue.WidgetRead], Cancellation)),
                TenancyRefusals.Of(TenancyRefusals.RoleNameTaken, ("Name", "élan")));
        });

        // The other two rules the aggregates keep themselves, so only a write that goes past them meets the index:
        // a context without the toolkit's checks, saving a second root and a second primary placement.
        await using (var past = PastTheAggregates())
        using (TenancyCallers.Begin(HostCaller.SystemIn(harbor.Tenant)))
        {
            var organization = await past.Set<HostOrganization>().SingleAsync(row => row.Id == harbor.Tenant, Cancellation);
            past.Entry(organization.Units.Single(unit => unit.Id == north)).Property(unit => unit.ParentId).CurrentValue = null;

            Same(
                await Refused.WithCodeAsync(TenancyRefusals.OneRoot, () => past.SaveChangesAsync(Cancellation)),
                TenancyRefusals.Of(TenancyRefusals.OneRoot));
        }

        await using (var past = PastTheAggregates())
        using (TenancyCallers.Begin(HostCaller.SystemIn(harbor.Tenant)))
        {
            var seat = await past.Set<HostSeat>().SingleAsync(row => row.Id == grace, Cancellation);
            past.Entry(seat.Placements.Single(placement => placement.UnitId == north)).Property(placement => placement.IsPrimary).CurrentValue = true;

            Same(
                await Refused.WithCodeAsync(TenancyRefusals.SecondPrimary, () => past.SaveChangesAsync(Cancellation)),
                TenancyRefusals.Of(TenancyRefusals.SecondPrimary));
        }

        // Nothing of the refused saves was written.
        _services.Database.CountRows("Tenants").Should().Be(2, "harbor and the orchard that got there first");
        _services.Database.CountRows("Seats").Should().Be(4, "an administrator each, grace and the one Bert");
        _services.Database.CountRows("Roles").Should().Be(harbor.RolesByPack.Count * 2 + 1, "each tenant's roles and the one Élan");
    }

    [Fact]
    public async Task The_same_name_or_person_in_another_tenant_breaks_no_index()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard");

        // The same name in two tenants, and the same person in two tenants: each index is per tenant.
        var person = Guid.NewGuid();
        foreach (var tenant in new[] { harbor, orchard })
        {
            await _services.BySystemIn(tenant.Tenant, services => services.Roles().CreateAsync("Élan", "Keeps going", [HostCatalogue.WidgetRead], Cancellation));
            await _services.AddSeatAsync(tenant.Tenant, person, "Bert");
        }

        _services.Database.CountRows("Seats").Should().Be(4);
    }

    /// <summary>
    /// A context on the test's database that saves what it is given: no invariants, no versions, none of
    /// Tenancy's checks. Only the interceptor that answers what the database refuses.
    /// </summary>
    private TestTenancyContext PastTheAggregates()
        => new(_services.Database.Options<TestTenancyContext>().AddInterceptors(new DatabaseRefusalInterceptor()).Options);

    /// <summary>The refusal the index gave is the one the use case gives: its code, kind, text and arguments.</summary>
    private static void Same(RefusalException fromTheIndex, RefusalException fromTheUseCase)
    {
        fromTheIndex.Code.Should().Be(fromTheUseCase.Code);
        fromTheIndex.Kind.Should().Be(fromTheUseCase.Kind);
        fromTheIndex.Message.Should().Be(fromTheUseCase.Message);
        fromTheIndex.Arguments.Should().BeEquivalentTo(fromTheUseCase.Arguments);
    }
}

/// <summary>The indexes' refusals on SQLite.</summary>
public sealed class IndexRefusalTestsOnSqlite() : IndexRefusalTests(TestDatabases.Sqlite);

/// <summary>The indexes' refusals on Postgres, as the owner of the tables.</summary>
public sealed class IndexRefusalTestsOnPostgres(PostgresDatabases postgres) : IndexRefusalTests(postgres);
