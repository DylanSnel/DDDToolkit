using DDDToolkit.Composition;
using DDDToolkit.EntityFramework;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// A context that keeps rows to a tenant cannot do without Tenancy's save check, however it is wired: its model says
/// so, and the toolkit refuses its first save where the save check is missing, before anything is written. That holds
/// where nothing registered Tenancy, so <c>UseDDDToolkit</c> had no save check to add, and where the context was given
/// the toolkit's base alone; neither needs the start-up checks to be run. And the save check comes after every other
/// part, wherever a host puts a part of its own, so no row a part adds slips past it.
/// </summary>
public sealed class RequiredSaveCheckTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_context_that_keeps_rows_to_a_tenant_in_a_host_that_never_registered_tenancy_is_refused_naming_AddTenancy()
    {
        using var database = new SqliteDatabase();
        var registered = new ServiceCollection();
        registered.AddDDDToolkitEntityFramework();
        registered.AddDbContext<TestWidgetContext>((provider, options) => options.UseSqlite(database.Connection).UseDDDToolkit(provider));
        await using var services = registered.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        const string Refusal =
            "'TestWidgetContext' cannot do without the part tenancy.save-check, which its model requires, and its options have no TenancySaveInterceptor, so its saves would go without it. " +
            "No registration brought it. Register it with services.AddTenancy<…, TContext>(…) of DDDToolkit.Supporting.Tenancy.EntityFramework, and options.UseDDDToolkit(serviceProvider) puts it on every context.";

        await using (var scope = services.CreateAsyncScope())
        {
            var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
            widgets.GetService<IRelationalDatabaseCreator>().CreateTables();

            // A seat of one tenant saves a widget of another: refused at the first save, without any start-up check run.
            using (TenancyCallers.Begin(HostCaller.InSeat(new TenantId(71), SeatId.CreateSequential())))
            {
                widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = new TenantId(72), UnitId = OrganizationUnitId.CreateSequential(), Name = "not ours" });
                await FluentActions.Awaiting(() => widgets.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(Refusal);
            }

            // Tenancy's own check, run by hand, says the same of the registration.
            FluentActions.Invoking(() => TenancyChecks.EnsureWired(widgets)).Should().Throw<InvalidOperationException>()
                .WithMessage("'TestWidgetContext' keeps entities to a tenant but has no TenancySaveInterceptor*Tenancy is not registered*services.AddTenancy<…, TContext>(…)*");
        }

        database.CountRows("Widgets").Should().Be(0, "nothing was written");

        // The start-up check the toolkit's registration brings stops such a host before its first request.
        var check = registered.GetStartupChecks().Registered.Single(each => each.Name == EntityFrameworkChecks.ToolkitWiredCheck);
        await FluentActions.Awaiting(() => check.RunAsync(services, Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(Refusal);
    }

    [Fact]
    public async Task A_context_given_the_base_alone_without_UseTenancy_is_refused_at_its_first_save_naming_the_calls()
    {
        using var services = new TestServices(Wiring.WithoutTenancy);

        await using var scope = services.Scope();
        var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
        using (TenancyCallers.Begin(HostCaller.InSeat(new TenantId(71), SeatId.CreateSequential())))
        {
            widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = new TenantId(71), UnitId = OrganizationUnitId.CreateSequential(), Name = "ours" });
            await FluentActions.Awaiting(() => widgets.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "'TestWidgetContext' cannot do without the part tenancy.save-check, which its model requires, and its options have no TenancySaveInterceptor, so its saves would go without it. " +
                "Configure the context with options.UseDDDToolkit(serviceProvider), which puts on every part the registrations brought; after options.UseDDDToolkitCore(serviceProvider), add options.UseTenancy(serviceProvider).");
        }

        services.Database.CountRows("Widgets").Should().Be(0);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(300)]
    [InlineData(int.MaxValue - 1)]
    public async Task A_part_of_a_hosts_own_at_any_position_comes_before_the_save_check_and_cannot_slip_a_row_past_it(int position)
    {
        var addsARow = new AddsAWidget();
        using var services = new TestServices(afterTenancy: registered => registered.AddContextPart(
            new ContextPart<DbContextOptionsBuilder>("tests.adds-a-row", position, (options, _) => options.AddInterceptors(addsARow))));
        var harbor = await services.ProvisionAsync("harbor");
        var orchard = await services.ProvisionAsync("orchard");

        await using (var scope = services.Scope())
        {
            var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
            var interceptors = widgets.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!.ToList();
            interceptors.FindIndex(interceptor => interceptor is AddsAWidget).Should().BeLessThan(
                interceptors.FindIndex(interceptor => interceptor is TenancySaveInterceptor), "the save check is the last part, at the highest position there is");

            // Harbor saves a widget of its own; the part adds one of orchard's to the same save.
            addsARow.Tenant = orchard.Tenant;
            using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
            {
                widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = harbor.Tenant, UnitId = harbor.RootUnit, Name = "ours" });
                await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => widgets.SaveChangesAsync(Cancellation));
            }
        }

        services.Database.CountRows("Widgets").Should().Be(0, "the row the part added was checked like any other, and the save refused");
    }

    /// <summary>What a part of a host's own might do in a save: add a row, here a widget of the tenant a test names.</summary>
    private sealed class AddsAWidget : SaveChangesInterceptor
    {
        public TenantId? Tenant { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is TestWidgetContext widgets && Tenant is { } tenant)
            {
                widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = tenant, UnitId = OrganizationUnitId.CreateSequential(), Name = "slipped in" });
            }

            return ValueTask.FromResult(result);
        }
    }
}
