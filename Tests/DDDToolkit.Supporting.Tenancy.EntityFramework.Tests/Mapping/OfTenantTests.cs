using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// A class that marks its tenant <c>[OfTenant]</c> in the domain is kept to the caller's tenant by
/// <c>AddDDDToolkitConventions()</c>, with no line in its context: the same filter, the same save check and the same
/// fixed column as <c>ScopeToTenant</c> gives. A context's own <c>ScopeToTenant</c> wins. A marker that cannot hold
/// a tenant refuses the model, and a context that forgot the conventions is refused at its first save.
/// </summary>
public sealed class OfTenantTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void A_marked_class_is_kept_to_its_tenant_as_ScopeToTenant_keeps_one()
    {
        using var marked = new Module(map => map.Entity<Gadget>().HasKey(gadget => gadget.Id));
        using var byHand = new Module(map => map.Entity<Widget>(widget =>
        {
            widget.HasKey(row => row.Id);
            widget.ScopeToTenant(row => row.TenantId);
        }));

        var gadget = marked.Model.FindEntityType(typeof(Gadget))!;
        var widget = byHand.Model.FindEntityType(typeof(Widget))!;

        TenancyModel.TenantPropertyOf(gadget)!.Name.Should().Be(nameof(Gadget.TenantId));
        gadget.FindDeclaredQueryFilter(TenancyQueryFilter.Name)!.Expression!.ToString()
            .Should().Be(widget.FindDeclaredQueryFilter(TenancyQueryFilter.Name)!.Expression!.ToString(), "the filter is the one ScopeToTenant writes");
        gadget.FindProperty(nameof(Gadget.TenantId))!.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw, "a row never moves to another tenant");
        Requirements(marked.Model).Should().Equal(Requirements(byHand.Model), "the model states that its context needs the save check");
    }

    [Fact]
    public void ScopeToTenant_on_a_marked_class_wins()
    {
        using var context = new Module(map => map.Entity<Gadget>(gadget =>
        {
            gadget.HasKey(row => row.Id);
            gadget.ScopeToTenant(row => row.LentTo);
        }));

        var gadget = context.Model.FindEntityType(typeof(Gadget))!;
        TenancyModel.TenantPropertyOf(gadget)!.Name.Should().Be(nameof(Gadget.LentTo));
        gadget.FindProperty(nameof(Gadget.TenantId))!.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Save, "the convention left the class to the context's call");
    }

    [Fact]
    public void A_derived_class_is_kept_with_its_root_and_an_owned_one_through_its_owner()
    {
        using var context = new Module(map =>
        {
            map.Entity<Gadget>().HasKey(gadget => gadget.Id);
            map.Entity<PoweredGadget>();
            map.Entity<Kit>(kit =>
            {
                kit.HasKey(row => row.Id);
                kit.OwnsMany(row => row.Parts);
            });
        });

        TenancyModel.TenantPropertyOf(context.Model.FindEntityType(typeof(PoweredGadget))!)!.Name.Should().Be(nameof(Gadget.TenantId));
        context.Model.FindEntityType(typeof(PoweredGadget))!.FindDeclaredQueryFilter(TenancyQueryFilter.Name).Should().BeNull("a filter is the root's");
        TenancyModel.TenantPropertyOf(context.Model.FindEntityType(typeof(Kit))!)!.Name.Should().Be(nameof(Kit.TenantId));
        TenancyModel.TenantPropertyOf(context.Model.FindEntityType(typeof(KitPart))!).Should().BeNull("an owned row is kept through its owner");
    }

    [Fact]
    public void A_marker_that_cannot_hold_a_tenant_refuses_the_model()
    {
        Refusal(map => map.Entity<TwoTenants>().HasKey(row => row.Id))
            .Should().Be("'TwoTenants' marks First and Second with [OfTenant]. A row belongs to one tenant: mark one property.");
        Refusal(map => map.Entity<MaybeATenant>().HasKey(row => row.Id))
            .Should().Be("'MaybeATenant.TenantId' is marked [OfTenant], and its type is TenantId?. The tenant of a row is the application's tenant id, never nullable: a struct id, as TenantId.");
        Refusal(map => map.Entity<NamedTenant>().HasKey(row => row.Id))
            .Should().StartWith("'NamedTenant.TenantId' is marked [OfTenant], and its type is String.");
        Refusal(map =>
            {
                map.Entity<Box>().HasKey(row => row.Id);
                map.Entity<MarkedBox>();
            })
            .Should().Be("'MarkedBox.Owner' is marked [OfTenant], and 'MarkedBox' derives from 'Box', which the context stores with it. "
                + "A tenant filter is the root's: mark the tenant of the root of the hierarchy.");

        static string Refusal(Action<ModelBuilder> map)
        {
            using var context = new Module(map);
            return FluentActions.Invoking(() => context.Model).Should().Throw<InvalidOperationException>().Which.Message;
        }
    }

    [Fact]
    public async Task A_marked_class_reads_and_writes_its_callers_tenant_only()
    {
        using var database = new SqliteDatabase();
        using var services = new TestServices(afterTenancy: registered => registered.AddDbContext<GadgetContext>(
            (provider, options) => options.UseSqlite(database.Connection).UseDDDToolkit(provider)));
        var harbor = await services.ProvisionAsync("harbor");
        var orchard = await services.ProvisionAsync("orchard");

        await using var scope = services.Scope();
        var gadgets = scope.ServiceProvider.GetRequiredService<GadgetContext>();
        gadgets.GetService<IRelationalDatabaseCreator>().CreateTables();

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(orchard.Tenant))
        {
            gadgets.Add(new Gadget { Id = Guid.NewGuid(), TenantId = orchard.Tenant, Name = "orchard's" });
            await gadgets.SaveChangesAsync(Cancellation);
        }

        using (TenancyCallers.Begin(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat)))
        {
            gadgets.Add(new Gadget { Id = Guid.NewGuid(), TenantId = harbor.Tenant, Name = "harbor's" });
            await gadgets.SaveChangesAsync(Cancellation);

            (await gadgets.Set<Gadget>().Select(gadget => gadget.Name).ToListAsync(Cancellation)).Should().Equal(["harbor's"], "the tenant filter reads the caller's tenant");

            var other = new Gadget { Id = Guid.NewGuid(), TenantId = orchard.Tenant, Name = "not ours" };
            gadgets.Add(other);
            await Refused.WithCodeAsync(TenancyRefusals.OtherTenant, () => gadgets.SaveChangesAsync(Cancellation));
            gadgets.Entry(other).State = EntityState.Detached;
        }
    }

    [Fact]
    public async Task A_context_without_the_conventions_is_refused_at_its_first_save()
    {
        using var database = new SqliteDatabase();
        using var services = new TestServices(afterTenancy: registered => registered.AddDbContext<GadgetContextWithoutConventions>(
            (provider, options) => options.UseSqlite(database.Connection).UseDDDToolkit(provider)));

        await using var scope = services.Scope();
        var gadgets = scope.ServiceProvider.GetRequiredService<GadgetContextWithoutConventions>();
        gadgets.GetService<IRelationalDatabaseCreator>().CreateTables();

        using (TenancyCallers.Begin(HostCaller.InSeat(new TenantId(71), SeatId.CreateSequential())))
        {
            gadgets.Add(new Gadget { Id = Guid.NewGuid(), TenantId = new TenantId(72), Name = "unchecked" });
            await FluentActions.Awaiting(() => gadgets.SaveChangesAsync(Cancellation)).Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "These entity types mark their tenant with [OfTenant] but are not kept to a tenant: Gadget. "
                + "Call configurationBuilder.AddDDDToolkitConventions() in ConfigureConventions, which keeps them to the caller's tenant, or ScopeToTenant on each.");
        }
    }

    /// <summary>The requirements of parts a model states, as annotations.</summary>
    private static IEnumerable<string> Requirements(IModel model)
        => model.GetAnnotations()
            .Where(annotation => annotation.Name.StartsWith(ContextPartRequirements.AnnotationPrefix, StringComparison.Ordinal))
            .Select(annotation => annotation.Name + "=" + annotation.Value);

    /// <summary>A module's class that marks its tenant, and has another tenant id a context may keep it to instead.</summary>
    public class Gadget
    {
        public Guid Id { get; set; }

        [OfTenant]
        public TenantId TenantId { get; set; }

        public TenantId LentTo { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Stored with <see cref="Gadget"/>, in its table: kept with its root.</summary>
    public sealed class PoweredGadget : Gadget
    {
        public int Watts { get; set; }
    }

    public sealed class Kit
    {
        public Guid Id { get; set; }

        [OfTenant]
        public TenantId TenantId { get; set; }

        public List<KitPart> Parts { get; set; } = [];
    }

    /// <summary>Owned by a kit: its marker maps nothing.</summary>
    public sealed class KitPart
    {
        [OfTenant]
        public TenantId TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public sealed class TwoTenants
    {
        public Guid Id { get; set; }

        [OfTenant]
        public TenantId First { get; set; }

        [OfTenant]
        public TenantId Second { get; set; }
    }

    public sealed class MaybeATenant
    {
        public Guid Id { get; set; }

        [OfTenant]
        public TenantId? TenantId { get; set; }
    }

    public sealed class NamedTenant
    {
        public Guid Id { get; set; }

        [OfTenant]
        public string TenantId { get; set; } = string.Empty;
    }

    /// <summary>A class that marks no tenant.</summary>
    public class Box
    {
        public Guid Id { get; set; }
    }

    /// <summary>Stored with an unmarked <see cref="Box"/>, and marking a tenant of its own.</summary>
    public sealed class MarkedBox : Box
    {
        [OfTenant]
        public TenantId Owner { get; set; }
    }

    /// <summary>A module's context with nothing for the tenant: the conventions keep its gadgets to one.</summary>
    public sealed class GadgetContext(DbContextOptions<GadgetContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Gadget>().HasKey(gadget => gadget.Id);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }

    /// <summary>The same context without the toolkit's conventions: nothing keeps its gadgets to a tenant.</summary>
    public sealed class GadgetContextWithoutConventions(DbContextOptions<GadgetContextWithoutConventions> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Gadget>().HasKey(gadget => gadget.Id);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.AddTenancyConverters();
    }

    /// <summary>A model of the test's own, on Postgres without a connection, built for each instance.</summary>
    private sealed class Module(Action<ModelBuilder> map)
        : DbContext(new DbContextOptionsBuilder<Module>().UseNpgsql("Host=model-only").ReplaceService<IModelCacheKeyFactory, PerInstance>().Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => map(modelBuilder);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }

        /// <summary>Every instance builds its own model: the mapping is the test's, not the class's.</summary>
        private sealed class PerInstance : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context, designTime);
        }
    }
}
