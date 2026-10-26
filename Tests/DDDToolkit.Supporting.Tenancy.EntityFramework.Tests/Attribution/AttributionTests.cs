using System.Reflection;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Who wrote a row first and who changed it last, kept on the row: an entity kept to a tenant that calls
/// <c>RecordsWhoChanged</c> gets six columns, which every save fills from the Tenancy caller it runs as. A seat is
/// recorded as the seat, an operator by its identity, the application's own work by its kind alone, and who wrote
/// the row first never changes.
/// <para>
/// Harbor, with Grace and Lin as operators of widgets at its root. The widgets' context keeps who changed a widget.
/// </para>
/// </summary>
public abstract class AttributionTests(TestDatabases databases) : IAsyncLifetime
{
    private static readonly Guid Odette = Guid.NewGuid();

    private TestServices _services = null!;
    private HostTenancy.ProvisionedTenant _harbor = null!;
    private SeatId _grace;
    private SeatId _lin;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();
        _harbor = await _services.ProvisionAsync("harbor");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _harbor.RootUnit, HostCatalogue.OperatorPack);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _harbor.RootUnit, HostCatalogue.OperatorPack);
    }

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_new_row_records_its_writer_as_creator_and_changer()
    {
        var pump = await AddAsync(HostCaller.InSeat(_harbor.Tenant, _grace), "Pump");

        (await RecordedAsync(pump)).Should().Be(new Recorded(_grace, "seat", null, _grace, "seat", null));
    }

    [Fact]
    public async Task A_change_records_only_who_changed_it()
    {
        var pump = await AddAsync(HostCaller.InSeat(_harbor.Tenant, _grace), "Pump");

        _services.Commands.Reset();
        await RenameAsync(HostCaller.InSeat(_harbor.Tenant, _lin), pump, "Big pump");

        (await RecordedAsync(pump)).Should().Be(new Recorded(_grace, "seat", null, _lin, "seat", null), "Grace wrote it first, and Lin changed it last");

        // Who wrote it first is not written again: the statement that changes the row sets the seat that changed
        // it, next to the name, and leaves those columns out.
        var update = _services.Commands.Commands.Should().ContainSingle(command => command.Contains("UPDATE ")).Which;
        update.Should().Contain(TenancyAttribution.ChangedBySeat).And.NotContain("CreatedBy");

        // A save that changes nothing of the row leaves who changed it last, whoever makes it.
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            var widgets = services.Widgets();
            await widgets.Widgets.SingleAsync(widget => widget.Id == pump, Cancellation);
            await widgets.SaveChangesAsync(Cancellation);
        });
        (await RecordedAsync(pump)).ChangedBySeat.Should().Be(_lin);

        // And a row that goes records nothing: there is no row left to say so.
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            var widgets = services.Widgets();
            widgets.Widgets.Remove(await widgets.Widgets.SingleAsync(widget => widget.Id == pump, Cancellation));
            await widgets.SaveChangesAsync(Cancellation);
        });
    }

    [Fact]
    public async Task An_operator_is_recorded_by_identity_and_no_seat()
    {
        var pump = await AddAsync(HostCaller.InSeat(_harbor.Tenant, _grace), "Pump");
        Guid valve;

        // What an operator asked for is carried out by system work that names the operator.
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(_harbor.Tenant, Odette, scope: "widgets"))
        {
            var widgets = scope.ServiceProvider.Widgets();
            (await widgets.Widgets.SingleAsync(widget => widget.Id == pump, Cancellation)).Name = "Checked pump";
            valve = widgets.Widgets.Add(NewWidget("Valve")).Entity.Id;
            await widgets.SaveChangesAsync(Cancellation);
        }

        (await RecordedAsync(pump)).Should().Be(new Recorded(_grace, "seat", null, null, "operator", Odette), "the seat that wrote it stays, and the operator changed it");
        (await RecordedAsync(valve)).Should().Be(new Recorded(null, "operator", Odette, null, "operator", Odette));
    }

    [Fact]
    public async Task System_work_records_its_kind_and_no_seat()
    {
        // System work done for a seat is the system's: the seat it is done for is not who changed the row.
        Guid pump;
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginSystemIn(_harbor.Tenant, (SeatId?)_grace, scope: "widgets"))
        {
            var widgets = scope.ServiceProvider.Widgets();
            pump = widgets.Widgets.Add(NewWidget("Pump")).Entity.Id;
            await widgets.SaveChangesAsync(Cancellation);
        }

        (await RecordedAsync(pump)).Should().Be(new Recorded(null, "system", null, null, "system", null));

        // A link's token is its own kind, and no seat's either: the columns hold a seat only where a seat acted.
        await using (var scope = _services.Scope())
        using (TenancyWork.BeginTokenIn(_harbor.Tenant, _lin, scope: "widgets"))
        {
            var widgets = scope.ServiceProvider.Widgets();
            (await widgets.Widgets.SingleAsync(widget => widget.Id == pump, Cancellation)).Name = "Pump, seen";
            await widgets.SaveChangesAsync(Cancellation);
        }

        (await RecordedAsync(pump)).Should().Be(new Recorded(null, "system", null, null, "token", null));
    }

    [Fact]
    public async Task Who_wrote_a_row_first_is_fixed()
    {
        var pump = await AddAsync(HostCaller.InSeat(_harbor.Tenant, _grace), "Pump");

        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            var widgets = services.Widgets();
            var row = widgets.Entry(await widgets.Widgets.SingleAsync(widget => widget.Id == pump, Cancellation));
            row.Property(TenancyAttribution.CreatedBySeat).CurrentValue = _lin;

            await FluentActions.Awaiting(() => widgets.SaveChangesAsync(Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*CreatedBySeat*read-only after it has been saved*");
        });

        (await RecordedAsync(pump)).Should().Be(new Recorded(_grace, "seat", null, _grace, "seat", null), "nothing of the refused save was written");
    }

    [Fact]
    public async Task The_columns_are_the_models_and_the_first_three_and_the_tenant_are_fixed()
    {
        await using var scope = _services.Scope();
        var model = scope.ServiceProvider.Widgets().Model;
        var widget = model.FindEntityType(typeof(Widget))!;

        TenancyModel.RecordsWhoChanged(widget).Should().BeTrue();
        model.GetEntityTypes().Where(entityType => TenancyModel.RecordsWhoChanged(entityType)).Should().ContainSingle("the rows of the read model keep nothing of the kind");
        scope.ServiceProvider.Tenancy().Model.GetEntityTypes().Should().NotContain(entityType => TenancyModel.RecordsWhoChanged(entityType));

        TenancyAttribution.Columns.Should().Equal(
            TenancyAttribution.CreatedBySeat, TenancyAttribution.CreatedByKind, TenancyAttribution.CreatedByIdentity,
            TenancyAttribution.ChangedBySeat, TenancyAttribution.ChangedByKind, TenancyAttribution.ChangedByIdentity);
        foreach (var column in TenancyAttribution.Columns)
        {
            var property = widget.FindProperty(column)!;
            property.IsShadowProperty().Should().BeTrue(column + " is on the row, and not on the class");
            property.GetAfterSaveBehavior().Should().Be(
                column.StartsWith("Created", StringComparison.Ordinal) ? PropertySaveBehavior.Throw : PropertySaveBehavior.Save,
                column + ": who wrote the row first is fixed, and who changed it last is written with every change");
        }

        widget.FindProperty(TenancyAttribution.ChangedBySeat)!.ClrType.Should().Be<SeatId?>();
        widget.FindProperty(TenancyAttribution.ChangedByIdentity)!.ClrType.Should().Be<Guid?>();
        var kind = widget.FindProperty(TenancyAttribution.ChangedByKind)!;
        kind.IsNullable.Should().BeFalse();
        kind.GetMaxLength().Should().Be(TenancyAttribution.KindLength);

        // The tenant of a row is fixed as well, in a module's model and in Tenancy's own.
        widget.FindProperty(nameof(Widget.TenantId))!.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw);
        foreach (var entityType in scope.ServiceProvider.Tenancy().Model.GetEntityTypes())
        {
            if (TenancyModel.TenantPropertyOf(entityType) is { } tenant)
            {
                tenant.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw, entityType.DisplayName() + " never moves to another tenant");
            }
        }
    }

    [Fact]
    public void The_call_without_the_seats_id_is_generated_where_the_seat_is_declared()
    {
        // The TestHost declares the seat class, so the generator closes RecordsWhoChanged over its seat id there:
        // an internal wrapper with the entity still to choose.
        var generated = typeof(TestTenancyContext).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Should().ContainSingle(method => method.Name == nameof(TenancyAttribution.RecordsWhoChanged)).Which;

        generated.DeclaringType!.IsNotPublic.Should().BeTrue();
        generated.GetGenericArguments().Should().ContainSingle("the seat's id is filled in, and the entity is the caller's to name");
    }

    [Fact]
    public async Task A_part_stored_in_its_owners_row_changes_who_changed_the_row()
    {
        await using var scope = _services.Scope();
        await using var parcels = new ParcelContext(Wired<ParcelContext>(scope.ServiceProvider));
        parcels.GetService<IRelationalDatabaseCreator>().CreateTables();

        var parcel = new Parcel { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, Label = new Label { Text = "Fragile" } };
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _grace)))
        {
            parcels.Add(parcel);
            await parcels.SaveChangesAsync(Cancellation);
        }

        // Lin changes the label alone: a part of the parcel's row, which Entity Framework tracks on its own.
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _lin)))
        {
            parcel.Label.Text = "This side up";
            await parcels.SaveChangesAsync(Cancellation);
        }

        parcels.ChangeTracker.Clear();
        using (TenancyCallers.Begin(HostCaller.SystemIn(_harbor.Tenant)))
        {
            var read = await parcels.Set<Parcel>()
                .Select(row => new
                {
                    row.Label.Text,
                    Created = EF.Property<SeatId?>(row, TenancyAttribution.CreatedBySeat),
                    Changed = EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat),
                })
                .SingleAsync(Cancellation);

            read.Text.Should().Be("This side up");
            read.Created.Should().Be(_grace);
            read.Changed.Should().Be(_lin, "the label is stored in the parcel's row, so changing it changes the row");
        }
    }

    [Fact]
    public async Task A_part_in_a_table_of_its_own_changes_who_changed_an_aggregate_with_a_version()
    {
        await using var scope = _services.Scope();
        await using var crates = new CrateContext(Wired<CrateContext>(scope.ServiceProvider));
        crates.GetService<IRelationalDatabaseCreator>().CreateTables();

        // A crate is an aggregate with a version, a pallet a plain entity: each has parts in a table of their own.
        var crate = new Crate { Id = Guid.NewGuid(), TenantId = _harbor.Tenant };
        crate.Slats.Add(new Slat { Id = Guid.NewGuid(), Wood = "Pine" });
        var pallet = new Pallet { Id = Guid.NewGuid(), TenantId = _harbor.Tenant };
        pallet.Planks.Add(new Slat { Id = Guid.NewGuid(), Wood = "Pine" });
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _grace)))
        {
            crates.AddRange(crate, pallet);
            await crates.SaveChangesAsync(Cancellation);
        }

        // Lin changes a slat of each, and nothing of the crate or the pallet themselves.
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _lin)))
        {
            crate.Slats[0].Wood = "Oak";
            pallet.Planks[0].Wood = "Oak";
            await crates.SaveChangesAsync(Cancellation);
        }

        crates.ChangeTracker.Clear();
        using (TenancyCallers.Begin(HostCaller.SystemIn(_harbor.Tenant)))
        {
            var ofTheCrate = await crates.Set<Crate>()
                .Select(row => new
                {
                    row.Version,
                    Wood = row.Slats.Select(slat => slat.Wood).Single(),
                    Created = EF.Property<SeatId?>(row, TenancyAttribution.CreatedBySeat),
                    Changed = EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat),
                })
                .SingleAsync(Cancellation);

            // The aggregate's version changes with every change of its parts, so its row is written, and says who.
            ofTheCrate.Wood.Should().Be("Oak");
            ofTheCrate.Version.Should().Be(2);
            ofTheCrate.Created.Should().Be(_grace);
            ofTheCrate.Changed.Should().Be(_lin, "a change of a part is a change of the aggregate, whose row is written with it");

            var ofThePallet = await crates.Set<Pallet>()
                .Select(row => new
                {
                    Wood = row.Planks.Select(plank => plank.Wood).Single(),
                    Changed = EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat),
                })
                .SingleAsync(Cancellation);

            // Without a version nothing of the pallet's own row changed, and the row is not written: it keeps who
            // changed it last.
            ofThePallet.Wood.Should().Be("Oak");
            ofThePallet.Changed.Should().Be(_grace, "the pallet's row was not written, so it says what it said");
        }
    }

    [Fact]
    public async Task A_type_stored_in_the_table_of_its_parent_keeps_who_changed_it_from_the_root()
    {
        await using var scope = _services.Scope();
        await using var derived = new DerivedContext(Wired<DerivedContext>(scope.ServiceProvider));

        // A sealed parcel that keeps who changed it, in the table of all parcels, which do not: the other rows of
        // that table would keep nothing, and under Postgres' trigger could not be written at all.
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _grace)))
        {
            derived.Add(new SealedParcel { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, Seal = "Wax" });

            _services.Commands.Reset();
            await FluentActions.Awaiting(() => derived.SaveChangesAsync(Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*stored in the table of the type they derive from, which does not: SealedParcel.*call RecordsWhoChanged on the type at the root of the hierarchy instead.");
            _services.Commands.Commands.Should().BeEmpty("the model is refused before the first command");
        }

        // Called on the root, every type of the hierarchy keeps it.
        await using var rooted = new RootedContext(Wired<RootedContext>(scope.ServiceProvider));
        TenancyModel.RecordsWhoChanged(rooted.Model.FindEntityType(typeof(SealedParcel))!).Should().BeTrue("a type keeps what the type it derives from keeps");
        rooted.GetService<IRelationalDatabaseCreator>().CreateTables();
        var wax = new SealedParcel { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, Seal = "Wax" };
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _lin)))
        {
            rooted.Add(wax);
            await rooted.SaveChangesAsync(Cancellation);
        }

        rooted.Entry(wax).Property(TenancyAttribution.ChangedBySeat).CurrentValue.Should().Be(_lin);
        rooted.Entry(wax).Property(TenancyAttribution.CreatedByKind).CurrentValue.Should().Be("seat");
    }

    [Fact]
    public async Task An_entity_that_records_who_changed_it_is_kept_to_a_tenant()
    {
        await using var scope = _services.Scope();
        await using var unscoped = new UnscopedContext(Wired<UnscopedContext>(scope.ServiceProvider));

        // Who changed a row is the Tenancy caller of the save, which only a tenant's row is sure to have.
        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _grace)))
        {
            unscoped.Add(new Parcel { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, Label = new Label { Text = "Loose" } });

            _services.Commands.Reset();
            await FluentActions.Awaiting(() => unscoped.SaveChangesAsync(Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*keep who changed their rows, with RecordsWhoChanged, but are not kept to a tenant: Parcel.*call ScopeToTenant on the entity as well.");
            _services.Commands.Commands.Should().BeEmpty("the model is refused before the first command");
        }
    }

    [Fact]
    public async Task Columns_mapped_for_another_id_than_the_seats_are_refused_at_the_save()
    {
        await using var scope = _services.Scope();
        await using var mistyped = new MistypedContext(Wired<MistypedContext>(scope.ServiceProvider));

        using (TenancyCallers.Begin(HostCaller.InSeat(_harbor.Tenant, _grace)))
        {
            mistyped.Add(new Parcel { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, Label = new Label { Text = "Mislabeled" } });

            await FluentActions.Awaiting(() => mistyped.SaveChangesAsync(Cancellation))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*keeps who changed it as a RoleId, and the seat that saves it is a SeatId*");
        }
    }

    /// <summary>What a widget's row says of who wrote it first and who changed it last.</summary>
    private sealed record Recorded(SeatId? CreatedBySeat, string CreatedByKind, Guid? CreatedByIdentity, SeatId? ChangedBySeat, string ChangedByKind, Guid? ChangedByIdentity);

    private Widget NewWidget(string name) => new() { Id = Guid.NewGuid(), TenantId = _harbor.Tenant, UnitId = _harbor.RootUnit, Name = name };

    private Task<Guid> AddAsync(HostCaller caller, string name)
        => _services.RunAsync(caller, async services =>
        {
            var widgets = services.Widgets();
            var widget = widgets.Widgets.Add(NewWidget(name)).Entity;
            await widgets.SaveChangesAsync(Cancellation);
            return widget.Id;
        });

    private Task RenameAsync(HostCaller caller, Guid widget, string name)
        => _services.RunAsync(caller, async services =>
        {
            var widgets = services.Widgets();
            (await widgets.Widgets.SingleAsync(row => row.Id == widget, Cancellation)).Name = name;
            await widgets.SaveChangesAsync(Cancellation);
        });

    private Task<Recorded> RecordedAsync(Guid widget)
        => _services.BySystemIn(_harbor.Tenant, services => services.Widgets().Widgets
            .Where(row => row.Id == widget)
            .Select(row => new Recorded(
                EF.Property<SeatId?>(row, TenancyAttribution.CreatedBySeat),
                EF.Property<string>(row, TenancyAttribution.CreatedByKind),
                EF.Property<Guid?>(row, TenancyAttribution.CreatedByIdentity),
                EF.Property<SeatId?>(row, TenancyAttribution.ChangedBySeat),
                EF.Property<string>(row, TenancyAttribution.ChangedByKind),
                EF.Property<Guid?>(row, TenancyAttribution.ChangedByIdentity)))
            .SingleAsync(Cancellation));

    /// <summary>Options for a context of a test's own on the test's database, wired as an application wires a context.</summary>
    private DbContextOptions<TContext> Wired<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        var options = _services.Database.Options<TContext>();
        options.UseDDDToolkit(services).UseTenancy(services).AddInterceptors(_services.Commands);
        return options.Options;
    }

    /// <summary>A parcel with a label, which is stored in the parcel's own row.</summary>
    private sealed class Parcel
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public Label Label { get; set; } = new();
    }

    private sealed class Label
    {
        public string Text { get; set; } = string.Empty;
    }

    private abstract class ParcelsContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Parcel>(parcel =>
            {
                parcel.ToTable("Parcels");
                parcel.HasKey(row => row.Id);
                parcel.Property(row => row.Id).ValueGeneratedNever();
                parcel.OwnsOne(row => row.Label);
                Keep(parcel);
            });

        protected abstract void Keep(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Parcel> parcel);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }

    /// <summary>Parcels kept to a tenant, which keep who changed them.</summary>
    private sealed class ParcelContext(DbContextOptions<ParcelContext> options) : ParcelsContext(options)
    {
        protected override void Keep(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Parcel> parcel)
        {
            parcel.ScopeToTenant(row => row.TenantId);
            parcel.RecordsWhoChanged<Parcel, SeatId>();
        }
    }

    /// <summary>Parcels that keep who changed them, and are kept to no tenant: a mistake the first save names.</summary>
    private sealed class UnscopedContext(DbContextOptions<UnscopedContext> options) : ParcelsContext(options)
    {
        protected override void Keep(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Parcel> parcel)
            => parcel.RecordsWhoChanged<Parcel, SeatId>();
    }

    /// <summary>An aggregate with a version, whose slats are stored in a table of their own.</summary>
    private sealed class Crate : IAggregateRoot
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public long Version { get; private set; }

        public List<Slat> Slats { get; } = [];
    }

    /// <summary>A plain entity, with no version, whose planks are stored in a table of their own.</summary>
    private sealed class Pallet
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public List<Slat> Planks { get; } = [];
    }

    private sealed class Slat
    {
        public Guid Id { get; set; }

        public string Wood { get; set; } = string.Empty;
    }

    /// <summary>Crates and pallets kept to a tenant, each keeping who changed it, with their parts in tables of their own.</summary>
    private sealed class CrateContext(DbContextOptions<CrateContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Crate>(crate =>
            {
                crate.ToTable("Crates");
                crate.Property(row => row.Id).ValueGeneratedNever();
                crate.OwnsMany(row => row.Slats, slats =>
                {
                    slats.ToTable("CrateSlats");
                    slats.WithOwner().HasForeignKey("CrateId");
                    slats.HasKey(slat => slat.Id);
                    slats.Property(slat => slat.Id).ValueGeneratedNever();
                });
                crate.ScopeToTenant(row => row.TenantId);
                crate.RecordsWhoChanged<Crate, SeatId>();
            });

            modelBuilder.Entity<Pallet>(pallet =>
            {
                pallet.ToTable("Pallets");
                pallet.Property(row => row.Id).ValueGeneratedNever();
                pallet.OwnsMany(row => row.Planks, planks =>
                {
                    planks.ToTable("PalletPlanks");
                    planks.WithOwner().HasForeignKey("PalletId");
                    planks.HasKey(plank => plank.Id);
                    planks.Property(plank => plank.Id).ValueGeneratedNever();
                });
                pallet.ScopeToTenant(row => row.TenantId);
                pallet.RecordsWhoChanged<Pallet, SeatId>();
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }

    /// <summary>A parcel of a kind of its own, stored in the table of all parcels.</summary>
    private sealed class SealedParcel : Carton
    {
        public string Seal { get; set; } = string.Empty;
    }

    private class Carton
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }
    }

    private abstract class CartonsContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Carton>(carton =>
            {
                carton.ToTable("Cartons");
                carton.Property(row => row.Id).ValueGeneratedNever();
                carton.ScopeToTenant(row => row.TenantId);
                KeepRoot(carton);
            });
            modelBuilder.Entity<SealedParcel>(KeepDerived);
        }

        protected virtual void KeepRoot(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Carton> carton)
        {
        }

        protected virtual void KeepDerived(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SealedParcel> parcel)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
        }
    }

    /// <summary>Who changed a row, asked of a derived type alone, which shares its parent's table: a mistake the first save names.</summary>
    private sealed class DerivedContext(DbContextOptions<DerivedContext> options) : CartonsContext(options)
    {
        protected override void KeepDerived(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<SealedParcel> parcel)
            => parcel.RecordsWhoChanged<SealedParcel, SeatId>();
    }

    /// <summary>Who changed a row, asked of the root of the hierarchy.</summary>
    private sealed class RootedContext(DbContextOptions<RootedContext> options) : CartonsContext(options)
    {
        protected override void KeepRoot(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Carton> carton)
            => carton.RecordsWhoChanged<Carton, SeatId>();
    }

    /// <summary>Parcels whose columns were mapped for an id that is not the seat's.</summary>
    private sealed class MistypedContext(DbContextOptions<MistypedContext> options) : ParcelsContext(options)
    {
        protected override void Keep(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Parcel> parcel)
        {
            parcel.ScopeToTenant(row => row.TenantId);
            parcel.RecordsWhoChanged<Parcel, RoleId>();
        }
    }
}

/// <summary>Who changed a row, on SQLite in memory.</summary>
public sealed class AttributionTestsOnSqlite() : AttributionTests(TestDatabases.Sqlite);

/// <summary>Who changed a row, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql.</summary>
public sealed class AttributionTestsOnPostgres(PostgresDatabases postgres) : AttributionTests(postgres);
