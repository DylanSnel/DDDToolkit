using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The tenants' listing over a database: an operator reads every tenant by slug, a page at a time, each page in
/// one statement that looks past the tenant filter and counts the active seats where the rows are. Who may ask is
/// decided before the store is reached; what an operator's database role may read is the policies' to say, and is
/// proven on Postgres under them.
/// </summary>
public abstract class TenantListingTests(TestDatabases databases) : IAsyncLifetime
{
    private const string OperatorRole = "operator";

    private static readonly Guid Odette = Guid.NewGuid();

    private TestServices _services = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();
        _services.Provider.GetRequiredService<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>().OperatorTokenRoles.Add(OperatorRole);
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task An_operator_reads_every_tenant_by_slug_a_page_in_one_statement()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard", TenantShape.Flat);
        var anvil = await _services.ProvisionAsync("anvil");
        await _services.SeatAtAsync(harbor, "Grace", harbor.RootUnit, HostCatalogue.WatcherStarterRole);
        var sue = await _services.SeatAtAsync(harbor, "Sue", harbor.RootUnit, HostCatalogue.WatcherStarterRole);
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().SuspendAsync(sue, Cancellation));
        await _services.BySystemIn(orchard.Tenant, services => services.Tenants().SuspendAsync("Paused", Cancellation));

        _services.Commands.Reset();
        var first = await PageAsync(after: null, size: 2);

        _services.Commands.Count.Should().Be(1, "a page is one statement, the seats counted where they are");
        _services.Commands.Sent[0].TenancyUser!.Kind.Should().Be(TenancyUserKind.Nobody, "an operator acts in no tenant, and the read looks past the tenant filter");
        first.Items.Select(Listed).Should().Equal(
            (anvil.Tenant, "anvil", "Anvil Works", TenantStatus.Active, 1),
            (harbor.Tenant, "harbor", "Harbor Works", TenantStatus.Active, 2));
        first.Next.Should().NotBeNull();

        _services.Commands.Reset();
        var second = await PageAsync(first.Next, size: 2);

        _services.Commands.Count.Should().Be(1);
        second.Items.Select(Listed).Should().Equal((orchard.Tenant, "orchard", "Orchard Works", TenantStatus.Suspended, 1));
        second.Next.Should().BeNull();

        // No identity and no name of a seat is read: the statement names neither column.
        _services.Commands.Commands.Should().OnlyContain(command => !command.Contains("Identity") && !command.Contains("DisplayName"));
    }

    [Fact]
    public async Task Anyone_else_is_refused_before_the_database_is_asked()
    {
        var harbor = await _services.ProvisionAsync("harbor");

        _services.Commands.Reset();
        await using (var scope = _services.Scope())
        using (TestAccess.BeginAs(CurrentUserSnapshot.User(Guid.NewGuid())))
        using (TenancyUsers.Begin(HostUser.InSeat(harbor.Tenant, harbor.AdminSeat)))
        {
            await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => scope.ServiceProvider.GetRequiredService<TenantManager>().ListAsync(null, 10, Cancellation));
        }

        await using (var scope = _services.Scope())
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            await Refused.WithCodeAsync(TenancyRefusals.OperatorsOnly, () => scope.ServiceProvider.GetRequiredService<TenantManager>().ListAsync(null, 10, Cancellation));
        }

        _services.Commands.Commands.Should().BeEmpty("neither a tenant's administrator nor system work in a tenant is an operator");
    }

    private async Task<TenantManager.TenantPage> PageAsync(string? after, int size)
    {
        await using var scope = _services.Scope();
        using (TestAccess.BeginAs(CurrentUserSnapshot.User(Odette, OperatorRole)))
        using (TenancyUsers.Begin(HostUser.Nobody(TenancyRefusals.NotSeated)))
        {
            return await scope.ServiceProvider.GetRequiredService<TenantManager>().ListAsync(after, size, Cancellation);
        }
    }

    /// <summary>What a listing shows of a tenant: its id, slug, name, status and active seats.</summary>
    private static (TenantId, string, string, TenantStatus, int) Listed(TenantManager.TenantListing listing)
        => (listing.Tenant.Id, listing.Tenant.Slug.Value, listing.Name, listing.Tenant.Status, listing.ActiveSeats);
}

/// <summary>The tenants' listing, on SQLite in memory.</summary>
public sealed class TenantListingTestsOnSqlite() : TenantListingTests(TestDatabases.Sqlite);

/// <summary>The tenants' listing, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql.</summary>
public sealed class TenantListingTestsOnPostgres(PostgresDatabases postgres) : TenantListingTests(postgres);
