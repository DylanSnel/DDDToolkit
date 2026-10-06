using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// What <c>AddTenancy</c> and <c>AddTenancyReadModel</c> put in a model: Tenancy's tables with their keys,
/// indexes and columns, the application's own fields and entities next to them, and a consumer's views that
/// describe every column as the table they read does.
/// </summary>
public abstract class ModelTests(TestDatabases databases) : IAsyncLifetime
{
    private static readonly string[] TenancyTables =
    [
        "Tenants", "Organizations", "OrganizationUnits", "OrganizationUnitPaths", "Seats", "SeatPlacements",
        "SeatRoleGrants", "SeatRights", "Roles", "TenancyAccessRevisions",
    ];

    private TestServices _services = null!;
    private AsyncServiceScope _scope;

    private TestTenancyContext Tenancy => _scope.ServiceProvider.GetRequiredService<TestTenancyContext>();

    private TestWidgetContext Widgets => _scope.ServiceProvider.GetRequiredService<TestWidgetContext>();

    public async ValueTask InitializeAsync()
    {
        _services = await databases.ServicesAsync();
        _scope = _services.Scope();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _scope.DisposeAsync();
            _services.Dispose();
        }
    }

    [Fact]
    public void AddTenancy_maps_every_table_key_and_index_as_documented()
    {
        var model = Tenancy.Model;

        var tenant = Table<HostTenant>(model, "Tenants", key: ["Id"], indexes: ["Slug unique"]);
        Column(tenant, "Slug").GetMaxLength().Should().Be(63);
        Column(tenant, "Slug").GetTypeMapping().Converter!.ProviderClrType.Should().Be<string>();
        Named(Column(tenant, "Status"));
        Named(Column(tenant, "Shape"));
        Column(tenant, "StatusReason").GetMaxLength().Should().Be(500);
        Column(tenant, "Version").IsConcurrencyToken.Should().BeTrue();
        Filtered(tenant, "Id");

        var organization = Table<HostOrganization>(model, "Organizations", key: ["Id"], indexes: []);
        Column(organization, "Name").GetMaxLength().Should().Be(200);
        Column(organization, "Version").IsConcurrencyToken.Should().BeTrue();
        Filtered(organization, "Id");

        var unit = Table<HostUnit>(model, "OrganizationUnits", key: ["Id"], indexes: ["ParentId", "TenantId", "TenantId unique where \"ParentId\" IS NULL"]);
        Owner(unit).Should().Be("HostOrganization Id <- TenantId");
        Column(unit, "TenantId").IsShadowProperty().Should().BeTrue();
        Column(unit, "Name").GetMaxLength().Should().Be(200);
        unit.FindProperty("Kind").Should().BeNull("the package keeps no kind of unit: an application that tells them apart adds a field of its own");
        Column(unit, "CostCentre").IsNullable.Should().BeTrue("a field of the application's own class is mapped with the package's");
        Named(Column(unit, "Status"));
        FieldAccess(organization, "Units", "_units");

        var path = Table<OrganizationUnitPath<TenantId, OrganizationUnitId>>(model, "OrganizationUnitPaths", key: ["AncestorId", "DescendantId"], indexes: ["DescendantId", "TenantId"]);
        Filtered(path, "TenantId");

        var seat = Table<HostSeat>(model, "Seats", key: ["Id"], indexes: ["Identity,TenantId unique", "TenantId"]);
        Column(seat, "DisplayName").GetMaxLength().Should().Be(200);
        Named(Column(seat, "Status"));
        Column(seat, "Version").IsConcurrencyToken.Should().BeTrue();
        Filtered(seat, "TenantId");

        var placement = Table<Placement<SeatId, OrganizationUnitId, RoleId>>(model, "SeatPlacements", key: ["SeatId", "UnitId"], indexes: ["SeatId unique where " + _services.Database.PrimaryFilter]);
        Owner(placement).Should().Be("HostSeat Id <- SeatId");
        Column(placement, "TenantId").IsShadowProperty().Should().BeTrue("the seat's tenant, for the placements' view to be filtered on");
        Utc(Column(placement, "PlacedAt"), nullable: false);
        FieldAccess(seat, "Placements", "_placements");

        var grant = Table<RoleGrant<SeatId, RoleId>>(model, "SeatRoleGrants", key: ["SeatId", "UnitId", "RoleId"], indexes: ["RoleId"]);
        Owner(grant).Should().Be("Placement<SeatId, OrganizationUnitId, RoleId> SeatId,UnitId <- SeatId,UnitId");
        Utc(Column(grant, "StartsAt"), nullable: false);
        Utc(Column(grant, "EndsAt"), nullable: true);
        Column(grant, "Reason").GetMaxLength().Should().Be(500);
        FieldAccess(placement, "Grants", "_grants");

        var right = Table<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>(model, "SeatRights", key: ["SeatId", "UnitId", "RoleId", "Key"], indexes: ["RoleId", "SeatId,Key", "TenantId"]);
        Column(right, "Key").GetMaxLength().Should().Be(128);
        Utc(Column(right, "StartsAt"), nullable: false);
        Utc(Column(right, "EndsAt"), nullable: true);
        Filtered(right, "TenantId");

        var role = Table<HostRole>(model, "Roles", key: ["Id"], indexes: ["TenantId,NormalizedName unique"]);
        Column(role, "Name").GetMaxLength().Should().Be(120);
        Column(role, "NormalizedName").IsShadowProperty().Should().BeTrue();
        Column(role, "NormalizedName").GetMaxLength().Should().Be(120);
        Column(role, "NormalizedName").IsNullable.Should().BeFalse("a role saved without it fails rather than escapes the unique index");
        Column(role, "Description").GetMaxLength().Should().Be(1000);
        Column(role, "FromPack").GetMaxLength().Should().Be(64);
        Named(Column(role, "Status"));
        Column(role, "Keys").IsPrimitiveCollection.Should().BeTrue("a role's keys are one column: JSON on SQLite, a text array on Postgres");
        Column(role, "Version").IsConcurrencyToken.Should().BeTrue();
        Filtered(role, "TenantId");

        var revision = Table<TenancyAccessRevision<TenantId>>(model, "TenancyAccessRevisions", key: ["TenantId"], indexes: []);
        Column(revision, "Revision").IsConcurrencyToken.Should().BeTrue();
        Filtered(revision, "TenantId");

        foreach (var keyed in new[] { tenant, organization, unit, seat, role, revision })
        {
            keyed.FindPrimaryKey()!.Properties.Should().OnlyContain(
                property => property.ValueGenerated == ValueGenerated.Never,
                "every Tenancy id is the application's, and never made by the database");
        }
    }

    [Fact]
    public void AddTenancyInvitations_maps_the_invitations_and_their_digests_apart()
    {
        var model = Tenancy.Model;

        var invitation = Table<HostInvitation>(model, "Invitations", key: ["Id"], indexes: ["TenantId,State,ExpiresAt"]);
        Column(invitation, "Address").GetMaxLength().Should().Be(254);
        Column(invitation, "DisplayName").GetMaxLength().Should().Be(200);
        Named(Column(invitation, "State"));
        Utc(Column(invitation, "IssuedAt"), nullable: false);
        Utc(Column(invitation, "ExpiresAt"), nullable: false);
        Utc(Column(invitation, "GrantUntil"), nullable: true);
        Utc(Column(invitation, "AcceptedAt"), nullable: true);
        Utc(Column(invitation, "ClosedAt"), nullable: true);
        Column(invitation, "Version").IsConcurrencyToken.Should().BeTrue();
        Column(invitation, "Id").ValueGenerated.Should().Be(ValueGenerated.Never);
        Filtered(invitation, "TenantId");
        ColumnOf<HostInvitation>(model, "Note").Should().Be("Invitations.Note", "the application's own field is a column of its table");

        // What an invitation offers does not change once the row is there: a save that changed it is refused.
        string[] offered = ["TenantId", "UnitId", "RoleId", "GrantUntil", "IssuedAt", "ExpiresAt", "IssuedBy", "IssuedAsSystem"];
        invitation.GetProperties().Where(property => property.GetAfterSaveBehavior() == PropertySaveBehavior.Throw && !property.IsKey())
            .Select(property => property.Name).Should().BeEquivalentTo(offered);

        // The digest of its token is no column of the invitation: it is a row of a table of its own, found by a
        // unique index, written after the invitation and gone with it.
        invitation.GetProperties().Should().NotContain(property => property.ClrType == typeof(byte[]));
        var digest = Table<TenancyInvitationDigest<InvitationId, TenantId>>(model, "InvitationDigests", key: ["InvitationId"], indexes: ["Digest unique", "TenantId"]);
        Column(digest, "Digest").GetMaxLength().Should().Be(32);
        Column(digest, "InvitationId").ValueGenerated.Should().Be(ValueGenerated.Never);
        Filtered(digest, "TenantId");
        var owner = digest.GetForeignKeys().Should().ContainSingle().Which;
        owner.PrincipalEntityType.Should().BeSameAs(invitation);
        owner.Properties.Select(property => property.Name).Should().Equal("InvitationId");
        owner.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
        digest.GetNavigations().Should().BeEmpty("nothing loads a digest with its invitation");
        invitation.GetNavigations().Should().BeEmpty();
    }

    [Fact]
    public async Task Host_fields_are_columns_of_the_hosts_tables()
    {
        var model = Tenancy.Model;
        ColumnOf<HostTenant>(model, "IsDemo").Should().Be("Tenants.IsDemo");
        ColumnOf<HostSeat>(model, "JobTitle").Should().Be("Seats.JobTitle");
        ColumnOf<HostUnit>(model, "CostCentre").Should().Be("OrganizationUnits.CostCentre");

        var harbor = await _services.ProvisionAsync("harbor");
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var tenant = await Tenancy.Set<HostTenant>().SingleAsync(TestContext.Current.CancellationToken);
            var organization = await Tenancy.Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken);
            var seat = await Tenancy.Set<HostSeat>().SingleAsync(TestContext.Current.CancellationToken);

            tenant.MarkAsDemo();
            organization.Root.SetCostCentre("NL-001");
            seat.ChangeJobTitle("Harbor master");
            await Tenancy.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = _services.Scope();
        var reread = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            (await reread.Set<HostTenant>().SingleAsync(TestContext.Current.CancellationToken)).IsDemo.Should().BeTrue();
            (await reread.Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken)).Root.CostCentre.Should().Be("NL-001");
            (await reread.Set<HostSeat>().SingleAsync(TestContext.Current.CancellationToken)).JobTitle.Should().Be("Harbor master");
        }
    }

    [Fact]
    public async Task A_host_entity_on_a_template_class_is_mapped_and_versioned()
    {
        var note = Tenancy.Model.FindEntityType(typeof(TenantNote))!;
        note.IsOwned().Should().BeTrue();
        note.FindOwnership()!.PrincipalEntityType.ClrType.Should().Be<HostTenant>();
        note.GetTableName().Should().Be("TenantNote");

        var harbor = await _services.ProvisionAsync("harbor");
        long before;
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var tenant = await Tenancy.Set<HostTenant>().SingleAsync(TestContext.Current.CancellationToken);
            before = tenant.Version;

            tenant.AddNote("Moored at the east quay.");
            await Tenancy.SaveChangesAsync(TestContext.Current.CancellationToken);

            tenant.Version.Should().Be(before + 1, "a change to what the tenant owns is a change to the tenant");
        }

        await using var scope = _services.Scope();
        var reread = scope.ServiceProvider.GetRequiredService<TestTenancyContext>();
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
        {
            var tenant = await reread.Set<HostTenant>().SingleAsync(TestContext.Current.CancellationToken);
            tenant.Version.Should().Be(before + 1);
            tenant.Notes.Select(saved => saved.Text).Should().Equal("Moored at the east quay.");
        }
    }

    [Fact]
    public void Read_model_types_are_views_and_absent_from_the_create_script()
    {
        // In Tenancy's own context, the rows that are parts of an aggregate are views over its tables.
        View<OrganizationUnitRow<TenantId, OrganizationUnitId>>(Tenancy.Model, "OrganizationUnits");
        View<RoleRow<TenantId, RoleId>>(Tenancy.Model, "Roles");
        View<PlacementRow<SeatId, OrganizationUnitId>>(Tenancy.Model, "SeatPlacements");
        View<SeatRow<TenantId, SeatId>>(Tenancy.Model, "Seats");

        var script = Tenancy.Database.GenerateCreateScript();
        script.Should().NotContain("CREATE VIEW");
        foreach (var table in TenancyTables)
        {
            Regex.Matches(script, "CREATE TABLE " + Regex.Escape(_services.Database.TenancyTable(table))).Should().ContainSingle(table + " is created once, by its table");
        }

        // In a consumer's, all six are.
        View<OrganizationUnitRow<TenantId, OrganizationUnitId>>(Widgets.Model, "OrganizationUnits");
        View<RoleRow<TenantId, RoleId>>(Widgets.Model, "Roles");
        View<PlacementRow<SeatId, OrganizationUnitId>>(Widgets.Model, "SeatPlacements");
        View<SeatRow<TenantId, SeatId>>(Widgets.Model, "Seats");
        View<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>(Widgets.Model, "SeatRights");
        View<OrganizationUnitPath<TenantId, OrganizationUnitId>>(Widgets.Model, "OrganizationUnitPaths");
        Widgets.Database.GenerateCreateScript().Should().NotContain("CREATE VIEW");

        // Every view is kept to the caller's tenant, the placements' too, through the tenant its table keeps.
        foreach (var view in Tenancy.Model.GetEntityTypes().Concat(Widgets.Model.GetEntityTypes()).Where(entityType => entityType.GetViewName() is not null))
        {
            view.FindDeclaredQueryFilter(TenancyQueryFilter.Name).Should().NotBeNull(view.DisplayName() + " is kept to the caller's tenant");
        }
    }

    [Fact]
    public void The_consumer_read_model_matches_AddTenancy_column_for_column()
    {
        var views = Widgets.Model.GetEntityTypes().Where(entityType => entityType.GetViewName() is not null).ToList();
        views.Should().HaveCount(6);

        foreach (var view in views)
        {
            var viewStore = StoreObjectIdentifier.View(view.GetViewName()!, view.GetViewSchema());
            var tables = Tenancy.Model.GetEntityTypes().Where(entityType => entityType.GetTableName() == viewStore.Name).ToList();
            tables.Should().NotBeEmpty(view.DisplayName() + " reads a table of Tenancy's");
            var tableStore = StoreObjectIdentifier.Table(viewStore.Name, tables[0].GetSchema());
            tableStore.Schema.Should().Be(viewStore.Schema, "the view reads the schema the table is in");

            foreach (var column in view.GetProperties())
            {
                var name = column.GetColumnName(viewStore)!;
                var source = tables.SelectMany(table => table.GetProperties()).Where(property => property.GetColumnName(tableStore) == name).ToList();
                source.Should().ContainSingle(view.DisplayName() + "." + name + " is a column of " + tableStore.Name);

                Describe(column, viewStore).Should().Be(Describe(source[0], tableStore), view.DisplayName() + "." + name + " is described as the table describes it");
            }
        }
    }

    [Fact]
    public void The_consumer_context_creates_no_tenancy_tables()
    {
        var script = Widgets.Database.GenerateCreateScript();

        Regex.Matches(script, "CREATE TABLE (?:\\w+\\.)?\"(\\w+)\"").Select(match => match.Groups[1].Value).Should().Equal("Widgets");
        Widgets.Model.GetEntityTypes().Where(entityType => entityType.GetTableName() is not null).Select(entityType => entityType.ClrType)
            .Should().Equal([typeof(Widget)], "the read model is views only, which migrations leave alone");
    }

    [Fact]
    public void The_host_calls_AddTenancy_without_type_arguments()
    {
        // TestTenancyContext calls modelBuilder.AddTenancy() and TestHostTenancy calls
        // services.AddTenancy<TestTenancyContext>(...): both compile only because the generator wrote them into
        // the TestHost, closed over its classes, internal to it and in the package's namespace.
        var host = typeof(TestTenancyContext).Assembly;

        var model = host.GetType(typeof(TenancyModelBuilderExtensions).Namespace + ".GeneratedTenancyModelBuilderExtensions")!;
        model.Should().NotBeNull();
        model.IsPublic.Should().BeFalse("each application gets its own, and none sees another's");
        var addToModel = model.GetMethods().Should().ContainSingle(method => method.Name == "AddTenancy").Subject;
        addToModel.IsGenericMethod.Should().BeFalse();
        addToModel.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(typeof(ModelBuilder), typeof(TenancyTableNames), typeof(DatabaseFacade));
        addToModel.GetParameters()[1].HasDefaultValue.Should().BeTrue("the table names keep their default");
        addToModel.GetParameters()[2].HasDefaultValue.Should().BeTrue("and so does the database, without which there are no filtered indexes");

        var services = host.GetType(typeof(TenancyEntityFrameworkServiceCollectionExtensions).Namespace + ".GeneratedTenancyEntityFrameworkServiceCollectionExtensions")!;
        services.Should().NotBeNull();
        services.IsPublic.Should().BeFalse();
        var addToServices = services.GetMethods().Should().ContainSingle(method => method.Name == "AddTenancy").Subject;
        addToServices.GetGenericArguments().Select(argument => argument.Name).Should().Equal(["TContext"], "the context is the application's to name");
        addToServices.GetParameters()[1].ParameterType.Should().Be<Action<TenancyOptions<TenantId, SeatId, OrganizationUnitId, RoleId>>>();
    }

    [Fact]
    public void The_generated_AddTenancy_matches_the_explicit_one()
    {
        using var explicitContext = new ExplicitTenancyContext(_services.Database.Options<ExplicitTenancyContext>().Options);

        // Tables, columns, keys and indexes, as a migration would create them.
        Tenancy.Database.GenerateCreateScript().Should().Be(explicitContext.Database.GenerateCreateScript());

        // And what the create script does not show: the views, the owned types and the named filters.
        Shape(Tenancy.Model).Should().Equal(Shape(explicitContext.Model));
    }

    [Fact]
    public void The_generated_services_AddTenancy_registers_what_the_explicit_one_does()
    {
        var generated = TestHostTenancy.Add(new ServiceCollection());
        var explicitServices = new ServiceCollection().AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId, TestTenancyContext>(
            options => options.Catalogue = HostCatalogue.Application);
        explicitServices.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, DDDToolkit.Supporting.Tenancy.TestHost.Requests.IHostRequest, TestTenancyContext>();
        explicitServices.AddTenancyInvitations<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId, HostInvitation, InvitationId, TestTenancyContext>();

        Registrations(generated).Should().Equal(Registrations(explicitServices));
    }

    private static IEnumerable<string> Shape(IModel model)
        => model.GetEntityTypes()
            .OrderBy(entityType => entityType.Name, StringComparer.Ordinal)
            .Select(entityType => string.Join(
                " ",
                entityType.Name,
                "table=" + entityType.GetSchema() + "." + entityType.GetTableName(),
                "view=" + entityType.GetViewSchema() + "." + entityType.GetViewName(),
                "owner=" + entityType.FindOwnership()?.PrincipalEntityType.Name,
                "key=" + string.Join(",", entityType.FindPrimaryKey()?.Properties.Select(property => property.Name) ?? []),
                "filters=" + string.Join(";", entityType.GetDeclaredQueryFilters().Select(filter => filter.Key + ":" + filter.Expression)),
                "properties=" + string.Join(",", entityType.GetProperties().Select(property => property.Name + ":" + property.GetTypeMapping().Converter?.GetType().Name))));

    private static IEnumerable<string> Registrations(IServiceCollection services)
        => services.Select(descriptor => descriptor.Lifetime + " " + descriptor.ServiceType + " " + (descriptor.ImplementationType?.ToString() ?? descriptor.ImplementationInstance?.GetType().ToString() ?? "factory"));

    private static IEntityType Table<T>(IModel model, string table, string[] key, string[] indexes)
    {
        var entityType = model.FindEntityType(typeof(T))!;
        entityType.Should().NotBeNull(typeof(T).Name + " is mapped");
        entityType.GetTableName().Should().Be(table);
        entityType.GetSchema().Should().Be(TestTenancyContext.Schema, "Tenancy's tables are in the context's default schema");
        entityType.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(key);
        entityType.GetIndexes()
            .Select(index => string.Join(",", index.Properties.Select(property => property.Name)) + (index.IsUnique ? " unique" : string.Empty)
                             + (index.GetFilter() is { } filter ? " where " + filter : string.Empty))
            .Order(StringComparer.Ordinal)
            .Should().Equal(indexes);
        return entityType;
    }

    private static IProperty Column(IEntityType entityType, string name)
        => entityType.FindProperty(name) ?? throw new InvalidOperationException(entityType.DisplayName() + " has no property " + name);

    private static void Named(IProperty property)
    {
        property.GetTypeMapping().Converter!.ProviderClrType.Should().Be<string>(property.Name + " is stored as its name");
        property.GetMaxLength().Should().Be(32);
    }

    private static void Utc(IProperty property, bool nullable)
        => property.GetTypeMapping().Converter.Should().BeOfType(
            nullable ? typeof(NullableUtcDateTimeOffsetConverter) : typeof(UtcDateTimeOffsetConverter),
            property.Name + " is stored as its UTC instant");

    private static void Filtered(IEntityType entityType, string tenantProperty)
    {
        var filter = entityType.FindDeclaredQueryFilter(TenancyQueryFilter.Name);
        filter.Should().NotBeNull(entityType.DisplayName() + " is kept to the caller's tenant");
        filter!.Expression!.ToString().Should().MatchRegex(@"\." + tenantProperty + @"\b.*Current", "the filter compares " + tenantProperty + " with the caller's tenant");
    }

    private static string Owner(IEntityType owned)
    {
        var ownership = owned.FindOwnership()!;
        return ownership.PrincipalEntityType.DisplayName() + " "
            + string.Join(",", ownership.PrincipalKey.Properties.Select(property => property.Name)) + " <- "
            + string.Join(",", ownership.Properties.Select(property => property.Name));
    }

    private static void FieldAccess(IEntityType owner, string navigation, string field)
    {
        var found = owner.FindNavigation(navigation)!;
        found.FieldInfo!.Name.Should().Be(field);
        found.GetPropertyAccessMode().Should().Be(PropertyAccessMode.Field);
    }

    private static string ColumnOf<T>(IModel model, string property)
    {
        var entityType = model.FindEntityType(typeof(T))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        return table.Name + "." + entityType.FindProperty(property)!.GetColumnName(table);
    }

    private static void View<T>(IModel model, string name)
    {
        var entityType = model.FindEntityType(typeof(T))!;
        entityType.FindPrimaryKey().Should().BeNull(typeof(T).Name + " is keyless");
        entityType.GetTableName().Should().BeNull(typeof(T).Name + " is no table");
        entityType.GetViewName().Should().Be(name);
        entityType.GetViewSchema().Should().Be(TestTenancyContext.Schema);
    }

    /// <summary>What a column is, as far as reading it goes.</summary>
    private static string Describe(IProperty property, StoreObjectIdentifier store)
    {
        var mapping = property.GetTypeMapping();
        return string.Join(
            " ",
            property.GetColumnName(store),
            property.GetColumnType(store),
            "max=" + property.GetMaxLength(store),
            "null=" + property.IsColumnNullable(store),
            "clr=" + Nullable.GetUnderlyingType(property.ClrType)?.Name + property.ClrType.Name,
            "converter=" + mapping.Converter?.GetType().Name,
            "provider=" + (mapping.Converter?.ProviderClrType ?? mapping.ClrType).Name,
            "element=" + property.GetElementType()?.GetTypeMapping().Converter?.GetType().Name);
    }
}

/// <summary>What AddTenancy and AddTenancyReadModel put in a model, on SQLite in memory.</summary>
public sealed class ModelTestsOnSqlite() : ModelTests(TestDatabases.Sqlite);

/// <summary>What AddTenancy and AddTenancyReadModel put in a model, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class ModelTestsOnPostgres(PostgresDatabases postgres) : ModelTests(postgres);
