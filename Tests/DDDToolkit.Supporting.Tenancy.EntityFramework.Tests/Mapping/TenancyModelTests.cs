using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// What a model says about tenancy, for a package that works from the model: which entity types are Tenancy's own
/// tables, which property holds an entity type's tenant, and whether a module's model reads more of Tenancy's than
/// the access facts of its read model.
/// </summary>
public sealed class TenancyModelTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly AsyncServiceScope _scope;

    public TenancyModelTests() => _scope = _services.Scope();

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void TableOf_and_TenantPropertyOf_answer_for_every_tenancy_table_and_no_view()
    {
        // Tenancy's own tables: the table each is, and the property its tenant is in, where the table is kept to one
        // itself rather than through its owner.
        var tables = new Dictionary<Type, (TenancyTable Table, string? Tenant)>
        {
            [typeof(HostTenant)] = (TenancyTable.Tenant, "Id"),
            [typeof(HostOrganization)] = (TenancyTable.Organization, "Id"),
            [typeof(HostUnit)] = (TenancyTable.Unit, null),
            [typeof(OrganizationUnitPath<TenantId, OrganizationUnitId>)] = (TenancyTable.UnitPath, "TenantId"),
            [typeof(HostSeat)] = (TenancyTable.Seat, "TenantId"),
            [typeof(Placement<SeatId, OrganizationUnitId, RoleId>)] = (TenancyTable.Placement, null),
            [typeof(RoleGrant<SeatId, RoleId>)] = (TenancyTable.Grant, null),
            [typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>)] = (TenancyTable.Right, "TenantId"),
            [typeof(HostRole)] = (TenancyTable.Role, "TenantId"),
            [typeof(TenancyAccessRevision<TenantId>)] = (TenancyTable.AccessRevision, "TenantId"),
            [typeof(HostInvitation)] = (TenancyTable.Invitation, "TenantId"),
            [typeof(TenancyInvitationDigest<InvitationId, TenantId>)] = (TenancyTable.InvitationDigest, "TenantId"),
        };

        // Everything else in either model: no Tenancy table, and a tenant only where it is kept to one.
        var others = new Dictionary<Type, string?>
        {
            [typeof(TenantNote)] = null,
            [typeof(OutboxMessage)] = null,
            [typeof(EventLogEntry)] = null,
            [typeof(Widget)] = "TenantId",
            [typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>)] = "TenantId",
            [typeof(RoleRow<TenantId, RoleId>)] = "TenantId",
            [typeof(PlacementRow<SeatId, OrganizationUnitId>)] = "TenantId",
            [typeof(SeatRow<TenantId, SeatId>)] = "TenantId",
        };

        var tenancy = _scope.ServiceProvider.Tenancy().Model;
        var widgets = _scope.ServiceProvider.Widgets().Model;

        // The rows Tenancy's database functions answer are mapped to nothing: no table of Tenancy's, and no tenant
        // of their own, since the function that answers them keeps to the caller's, or, for the tenants to visit,
        // answers across tenants on purpose.
        static bool AnswersAFunction(IEntityType entityType) => entityType.GetTableName() is null && entityType.GetViewName() is null;

        foreach (var entityType in tenancy.GetEntityTypes())
        {
            if (AnswersAFunction(entityType))
            {
                TenancyModel.TableOf(entityType).Should().BeNull(entityType.DisplayName() + " is no table");
                TenantOf(entityType).Should().BeNull(entityType.DisplayName());
            }
            else if (tables.TryGetValue(entityType.ClrType, out var expected))
            {
                TenancyModel.TableOf(entityType).Should().Be(expected.Table, entityType.DisplayName());
                TenantOf(entityType).Should().Be(expected.Tenant, entityType.DisplayName());
            }
            else
            {
                others.Should().ContainKey(entityType.ClrType, "every entity type of Tenancy's context is accounted for");
                TenancyModel.TableOf(entityType).Should().BeNull(entityType.DisplayName() + " is not one of Tenancy's tables");
                TenantOf(entityType).Should().Be(others[entityType.ClrType], entityType.DisplayName());
            }
        }

        tables.Keys.Should().OnlyContain(type => tenancy.FindEntityType(type) != null, "every Tenancy table is in Tenancy's own model");
        tenancy.GetEntityTypes().Where(AnswersAFunction).Should().HaveCount(5, "the administrators, the rights a move changes, the holders of a key, the tenants to visit and the invitation of a token's digest")
            .And.Contain(entityType => entityType.ClrType == typeof(MoveReach<OrganizationUnitId>));

        // A consumer's context: its own entity is kept to a tenant, and every row of the read model is a view.
        foreach (var entityType in widgets.GetEntityTypes())
        {
            TenancyModel.TableOf(entityType).Should().BeNull(entityType.DisplayName() + " is a consumer's, or a view over Tenancy's tables");
            TenantOf(entityType).Should().Be(AnswersAFunction(entityType) ? null : "TenantId", entityType.DisplayName() + " is kept to a tenant, unless a function answers it");
        }

        widgets.GetEntityTypes().Where(AnswersAFunction).Should().ContainSingle("a consumer asks who holds a key, and nothing else of other seats");

        widgets.GetEntityTypes().Select(entityType => entityType.ClrType).Should().Contain(
            [typeof(Widget), typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>), typeof(OrganizationUnitPath<TenantId, OrganizationUnitId>)]);
    }

    public static TheoryData<bool, bool> ReadModels => new()
    {
        { false, false }, { false, true }, { true, false }, { true, true },
    };

    [Theory]
    [MemberData(nameof(ReadModels))]
    public void A_module_model_of_the_read_model_alone_reads_nothing_beyond_it(bool throughFunctions, bool snakeCase)
    {
        using var module = new Module(
            modelBuilder =>
            {
                if (throughFunctions)
                {
                    modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
                }
                else
                {
                    modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema, snakeCase ? TenancyTableNames.SnakeCase : null);
                }

                // The module's own table, kept to a tenant, with a name of its own: nothing of Tenancy's.
                modelBuilder.Entity<Widget>(widget =>
                {
                    widget.ToTable(snakeCase ? "widgets" : "Widgets");
                    widget.HasKey(row => row.Id);
                    widget.ScopeToTenant(row => row.TenantId);
                });
            },
            snakeCase);

        var tables = snakeCase ? TenancyTableNames.SnakeCase : null;
        TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema, tables).Should().BeEmpty();
        TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema, tables).Should().BeSameAs(
            TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema, tables), "a model is walked once for the same question");

        // The widgets' own context of these tests, which maps the views and a table of its own, passes too.
        TenancyModel.ReadsBeyondAccessFacts(_scope.ServiceProvider.Widgets().Model, TestTenancyContext.Schema).Should().BeEmpty();
        FluentActions.Invoking(() => TenancyModel.ReadsBeyondAccessFacts(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_type_of_the_modules_own_on_a_table_of_tenancys_is_reported()
    {
        // The seats' table as a table, the roles' as a view, the units' through Tenancy's function, and the tenants'
        // table as the second table of a type that is split over two: each reads what the read model keeps back.
        using var module = new Module(modelBuilder =>
        {
            modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
            modelBuilder.Entity<NamedSeat>().ToTable("Seats", TestTenancyContext.Schema).HasKey(seat => seat.Id);
            modelBuilder.Entity<NamedRole>().HasNoKey().ToView("roles", "TENANCY");
            modelBuilder.Entity<NamedUnit>().HasNoKey().ToFunction(TenancyFunctionNames.TenantUnits, function => function.HasSchema(TestTenancyContext.Schema));
            modelBuilder.Entity<NamedTenant>(tenant =>
            {
                tenant.ToTable("TenantNames").HasKey(row => row.Id);
                tenant.SplitToTable("Tenants", TestTenancyContext.Schema, split => split.Property(row => row.Name));
            });

            // A table of the module's own under one of Tenancy's names, in the module's schema, is the module's.
            modelBuilder.Entity<Widget>().ToTable("Roles").HasKey(widget => widget.Id);
        });

        var findings = TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema);

        findings.Should().HaveCount(4);
        findings.Should().ContainSingle(finding => finding.StartsWith("NamedSeat is mapped onto the table tenancy.Seats, which is Tenancy's.", StringComparison.Ordinal));
        findings.Should().ContainSingle(finding => finding.StartsWith("NamedRole is mapped onto the view TENANCY.roles, which is Tenancy's.", StringComparison.Ordinal),
            "names are compared ignoring case, as some databases do");
        findings.Should().ContainSingle(finding => finding.StartsWith("NamedUnit is mapped onto the function tenancy.tenant_units, which is Tenancy's.", StringComparison.Ordinal));
        findings.Should().ContainSingle(finding => finding.StartsWith("NamedTenant is mapped onto the table tenancy.Tenants, which is Tenancy's.", StringComparison.Ordinal));
        findings.Should().OnlyContain(finding => finding.EndsWith("and asks the directory for names, by id.", StringComparison.Ordinal));

        // Asked about another schema, or other table names, the same model reads nothing of Tenancy's there.
        TenancyModel.ReadsBeyondAccessFacts(module.Model, "elsewhere").Should().BeEmpty();
        TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema, new TenancyTableNames(Tenants: "Lessees", Seats: "Chairs", Roles: "Hats"))
            .Should().ContainSingle("a function has one name, whatever the tables are called")
            .Which.Should().StartWith("NamedUnit is mapped onto the function tenancy.tenant_units, which is Tenancy's.");

        // With no schema named, Tenancy's is the model's own: there the module's table named Roles is Tenancy's.
        TenancyModel.ReadsBeyondAccessFacts(module.Model, schema: null).Should().ContainSingle()
            .Which.Should().StartWith("Widget is mapped onto the table " + TestWidgetContext.Schema + ".Roles, which is Tenancy's.");
    }

    [Fact]
    public void A_property_added_to_a_read_row_is_reported()
    {
        using var module = new Module(modelBuilder =>
        {
            modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);

            // Shadow properties over columns the views do have: the names the read model keeps back.
            modelBuilder.Entity<RoleRow<TenantId, RoleId>>().Property<string>("Name");
            modelBuilder.Entity<SeatRow<TenantId, SeatId>>().Property<string>("DisplayName");
            modelBuilder.Entity<SeatRow<TenantId, SeatId>>().Property<Guid>("Identity");
            modelBuilder.Entity<OrganizationUnitRow<TenantId, OrganizationUnitId>>().Property<string>("Kind");
        });

        TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema).Select(finding => finding[..finding.IndexOf(", which", StringComparison.Ordinal)]).Should().BeEquivalentTo(
            "RoleRow<TenantId, RoleId> has the property Name",
            "SeatRow<TenantId, SeatId> has the property DisplayName",
            "SeatRow<TenantId, SeatId> has the property Identity",
            "OrganizationUnitRow<TenantId, OrganizationUnitId> has the property Kind");

        // The one shadow property the package gives a row itself is the placements' tenant; any other is reported.
        using var functions = new Module(modelBuilder =>
        {
            modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
            modelBuilder.Entity<PlacementRow<SeatId, OrganizationUnitId>>().Property<DateTime>("PlacedAt");
        });

        TenancyModel.ReadsBeyondAccessFacts(functions.Model, TestTenancyContext.Schema).Should().ContainSingle()
            .Which.Should().StartWith("PlacementRow<SeatId, OrganizationUnitId> has the property PlacedAt, which is no part of Tenancy's read model.");
    }

    [Fact]
    public void Tenancys_own_model_is_not_judged()
    {
        var tenancy = _scope.ServiceProvider.Tenancy().Model;

        // It maps every table of Tenancy's, in the schema asked about, and the host's own note on a tenant as well.
        tenancy.GetEntityTypes().Should().Contain(entityType => entityType.ClrType == typeof(HostSeat) && entityType.GetSchema() == TestTenancyContext.Schema);
        TenancyModel.ReadsBeyondAccessFacts(tenancy, TestTenancyContext.Schema).Should().BeEmpty();
        TenancyModel.ReadsBeyondAccessFacts(tenancy, schema: null).Should().BeEmpty();
    }

    [Fact]
    public void A_module_that_maps_a_read_row_onto_the_table_itself_is_still_judged()
    {
        // The rights and the closure are tables in Tenancy's own model, so TableOf knows them as tables here too. A
        // module that maps them so does not pass for Tenancy by it.
        using var module = new Module(modelBuilder =>
        {
            modelBuilder.Entity<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>()
                .ToTable("SeatRights", TestTenancyContext.Schema)
                .HasKey(right => new { right.SeatId, right.UnitId, right.RoleId, right.Key });
            modelBuilder.Entity<OrganizationUnitPath<TenantId, OrganizationUnitId>>()
                .ToTable("OrganizationUnitPaths", TestTenancyContext.Schema)
                .HasKey(path => new { path.AncestorId, path.DescendantId });
            modelBuilder.Entity<NamedRole>().ToTable("Roles", TestTenancyContext.Schema).HasKey(role => role.Id);
        });

        module.Model.GetEntityTypes().Select(TenancyModel.TableOf).Should().Contain([TenancyTable.Right, TenancyTable.UnitPath]);
        TenancyModel.ReadsBeyondAccessFacts(module.Model, TestTenancyContext.Schema).Should().ContainSingle()
            .Which.Should().StartWith("NamedRole is mapped onto the table tenancy.Roles, which is Tenancy's.");
    }

    private static string? TenantOf(IEntityType entityType) => TenancyModel.TenantPropertyOf(entityType)?.Name;

    /// <summary>A seat with its name, as a module might map one for itself.</summary>
    private sealed class NamedSeat
    {
        public SeatId Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>A role with its name.</summary>
    private sealed class NamedRole
    {
        public RoleId Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A unit with its name.</summary>
    private sealed class NamedUnit
    {
        public OrganizationUnitId Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A tenant with its name, kept in two tables.</summary>
    private sealed class NamedTenant
    {
        public TenantId Id { get; set; }

        public string Note { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// A module's context in the widgets' schema, asked for its model only: it never connects. With
    /// <paramref name="snakeCase"/> it names its tables and columns the way a naming convention would.
    /// </summary>
    private sealed class Module(Action<ModelBuilder> map, bool snakeCase = false)
        : DbContext(new DbContextOptionsBuilder<Module>().UseNpgsql("Host=model-only").ReplaceService<IModelCacheKeyFactory, PerInstance>().Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestWidgetContext.Schema);
            map(modelBuilder);

            if (snakeCase)
            {
                foreach (var property in modelBuilder.Model.GetEntityTypes().Where(entityType => entityType.GetFunctionName() is null).SelectMany(entityType => entityType.GetProperties()))
                {
                    property.SetColumnName(string.Concat(property.Name.Select((letter, index) => index > 0 && char.IsUpper(letter) ? "_" + char.ToLowerInvariant(letter) : char.ToLowerInvariant(letter).ToString())));
                }
            }
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
        }

        /// <summary>Every instance builds its own model: the mapping is the test's, not the class's.</summary>
        private sealed class PerInstance : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context, designTime);
        }
    }
}
