using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Persistence;
using Xunit;

namespace Shop;

/// <summary>
/// An application of one project that writes <c>[assembly: GenerateTenancyClasses]</c> and no class or id of Tenancy's:
/// it compiles, it creates its tables, and it provisions a tenant through the package's use case, on a SQLite database
/// in memory. What the tenant, its organization, its root, its roles and its first seat are stored as is read back
/// from that database. The system work it is done in is begun through the class the use cases are named through,
/// closed over the ids the switch wrote, and no line here names one of them.
/// </summary>
public sealed class ProvisioningTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public ProvisioningTests()
    {
        // In memory, the database lives as long as this connection is open.
        _connection.Open();
        _services = new ServiceCollection().AddShop(_connection).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ShopContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A_tenant_is_provisioned_through_the_classes_and_ids_the_switch_wrote()
    {
        TenancyUseCases.ProvisionedTenant provisioned;
        await using (var scope = _services.CreateAsyncScope())
        using (TenancyUseCases.BeginSystem())
        {
            provisioned = await scope.ServiceProvider.GetRequiredService<TenancyUseCases.TenantCommands>().ProvisionAsync(
                new TenancyUseCases.TenantToProvision("acme", "Acme Works", TenantShape.Flat, "Acme", Guid.NewGuid()),
                TestContext.Current.CancellationToken);
        }

        await using var read = _services.CreateAsyncScope();
        using var inTheTenant = TenancyUseCases.BeginSystemIn(provisioned.Tenant);
        var context = read.ServiceProvider.GetRequiredService<ShopContext>();

        var tenant = await context.Set<Tenant>().SingleAsync(TestContext.Current.CancellationToken);
        tenant.Id.Should().Be(provisioned.Tenant);
        tenant.Slug.Value.Should().Be("acme");
        tenant.IsActive.Should().BeTrue();

        var organization = await context.Set<Organization>().SingleAsync(TestContext.Current.CancellationToken);
        organization.Id.Should().Be(provisioned.Tenant, "an organization shares its tenant's id");
        organization.Units.Should().ContainSingle().Which.Id.Should().Be(provisioned.RootUnit);

        var seat = await context.Set<Seat>().SingleAsync(TestContext.Current.CancellationToken);
        seat.Id.Should().Be(provisioned.AdminSeat);
        seat.Status.Should().Be(SeatStatus.Active, "a seat the switch wrote is the package's, with nothing of the application's added: no name either");

        (await context.Set<Role>().Select(static role => role.Id).ToListAsync(TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo(provisioned.RolesByPack.Values);
    }

    [Fact]
    public void The_switch_wrote_every_class_and_id_the_application_left_out()
    {
        Type[] written = [typeof(Tenant), typeof(Organization), typeof(OrganizationUnit), typeof(Seat), typeof(Role), typeof(TenantId), typeof(OrganizationUnitId), typeof(SeatId), typeof(RoleId)];
        written.Select(static type => type.Namespace).Should().AllBe("Shop", "what the switch writes goes in the project's root namespace");
        written.Should().OnlyContain(static type => type.IsPublic);

        typeof(Tenant).GetCustomAttributes(inherit: false).Should().ContainSingle(static attribute => attribute is TenantAggregateAttribute<TenantId>);
        typeof(TenantId).GetCustomAttributes(inherit: false).Should().Contain(static attribute => attribute is ModuleContractAttribute);
        typeof(Tenant).Assembly.GetType("Shop.OrganizationId").Should().BeNull("an organization shares its tenant's id");
        typeof(Tenant).Assembly.GetType("Shop.Invitation").Should().BeNull("the invitation is the class an application may leave out, and the switch leaves it out");

        typeof(TenancyUseCases).BaseType!.GetGenericArguments().Should().Equal(
            typeof(Tenant), typeof(TenantId), typeof(Organization), typeof(OrganizationUnit), typeof(OrganizationUnitId), typeof(Seat), typeof(SeatId), typeof(Role), typeof(RoleId));
    }
}
