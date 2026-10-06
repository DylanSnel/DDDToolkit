using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// <c>UseDDDToolkit</c> alone wires every context of a host, whatever each has to do with Tenancy: <c>AddTenancy</c>
/// brings its save check to all of them, without naming one, and each context's model says what the check does there.
/// Tenancy's own context writes the closure and the rights a save changes, a module's context that keeps rows to a
/// tenant refuses a row of another, and a context that keeps none saves as it would without Tenancy. The chain written
/// out before the one call, and the parts taken one by one after <c>UseDDDToolkitCore</c>, wire the same.
/// </summary>
public sealed class OneCallTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_call_wires_tenancys_context_a_modules_and_one_that_keeps_nothing_to_a_tenant_each_as_its_model_asks()
    {
        var database = new SqliteDatabase();
        using var services = new TestServices(
            database: database,
            ownsDatabase: true,
            configure: registered => registered.AddDbContext<NoticeBoardContext>((provider, options) => options.UseSqlite(database.Connection).UseDDDToolkit(provider)));

        await using (var scope = services.Scope())
        {
            var notices = scope.ServiceProvider.GetRequiredService<NoticeBoardContext>();
            notices.GetService<IRelationalDatabaseCreator>().CreateTables();

            // Each has the save check once, after the toolkit's interceptors, and passes the check of its wiring.
            foreach (var context in new DbContext[] { scope.ServiceProvider.GetRequiredService<TestTenancyContext>(), scope.ServiceProvider.GetRequiredService<TestWidgetContext>(), notices })
            {
                var interceptors = InterceptorsOf(context);
                interceptors.OfType<TenancySaveInterceptor>().Should().ContainSingle("{0} is wired by the one call", context.GetType().Name);
                interceptors.FindIndex(interceptor => interceptor is TenancySaveInterceptor).Should().BeGreaterThan(interceptors.FindIndex(interceptor => interceptor is DatabaseRefusalInterceptor));
                FluentActions.Invoking(() => TenancyChecks.EnsureWired(context)).Should().NotThrow();
            }
        }

        // Tenancy's own context: the save writes the closure of the organization and the rights of its seats.
        var harbor = await services.ProvisionAsync("harbor");
        var orchard = await services.ProvisionAsync("orchard");
        services.Database.CountRows("OrganizationUnitPaths").Should().BeGreaterThan(0);
        services.Database.CountRows("SeatRights").Should().BeGreaterThan(0);

        // A module's context that keeps its widgets to a tenant: a widget of another tenant is refused.
        await using (var scope = services.Scope())
        {
            var widgets = scope.ServiceProvider.GetRequiredService<TestWidgetContext>();
            using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
            {
                widgets.Widgets.Add(new Widget { Id = Guid.NewGuid(), TenantId = orchard.Tenant, UnitId = OrganizationUnitId.CreateSequential(), Name = "not ours" });
                await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => widgets.SaveChangesAsync(Cancellation));
            }
        }

        // A context that keeps nothing to a tenant saves with nobody at all, as it would without Tenancy.
        await using (var scope = services.Scope())
        {
            var notices = scope.ServiceProvider.GetRequiredService<NoticeBoardContext>();
            using (TenancyCallers.BeginNone())
            {
                notices.Notices.Add(new Notice { Id = Guid.NewGuid(), Text = "The yard is closed on Sunday." });
                await notices.SaveChangesAsync(Cancellation);
            }
        }

        services.Database.CountRows("Widgets").Should().Be(0);
        services.Database.CountRows("Notices").Should().Be(1);
    }

    [Theory]
    [InlineData(Wiring.Wired)]
    [InlineData(Wiring.WrittenOut)]
    [InlineData(Wiring.OneByOne)]
    public async Task The_one_call_the_chain_written_out_and_the_parts_one_by_one_wire_alike(Wiring wiring)
    {
        using var services = new TestServices(wiring);

        await using (var scope = services.Scope())
        {
            foreach (var context in new DbContext[] { scope.ServiceProvider.GetRequiredService<TestTenancyContext>(), scope.ServiceProvider.GetRequiredService<TestWidgetContext>() })
            {
                InterceptorsOf(context).Select(interceptor => interceptor is TenancySaveInterceptor ? typeof(TenancySaveInterceptor) : interceptor.GetType()).Take(5).Should().Equal(
                    typeof(PublishDomainEventsInterceptor), typeof(InvariantInterceptor), typeof(AggregateVersionInterceptor), typeof(DatabaseRefusalInterceptor), typeof(TenancySaveInterceptor));
            }
        }

        (await services.ProvisionAsync("harbor")).Tenant.Should().NotBe(default(TenantId));
        services.Database.CountRows("SeatRights").Should().BeGreaterThan(0);
    }

    private static List<IInterceptor> InterceptorsOf(DbContext context)
        => [.. context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? []];

    /// <summary>A notice of a board everybody reads: nothing of it belongs to a tenant.</summary>
    public sealed class Notice
    {
        public Guid Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }

    /// <summary>A context of the host's that has nothing to do with Tenancy, next to Tenancy's and a module's.</summary>
    public sealed class NoticeBoardContext(DbContextOptions<NoticeBoardContext> options) : DbContext(options)
    {
        public DbSet<Notice> Notices => Set<Notice>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Notice>(notice =>
            {
                notice.ToTable("Notices");
                notice.HasKey(row => row.Id);
                notice.Property(row => row.Id).ValueGeneratedNever();
            });
    }
}
