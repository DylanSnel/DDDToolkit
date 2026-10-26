using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// What a module's model maps of Tenancy's: the rows of the read model, which carry access facts and no name, and
/// nothing else. So a module cannot read what a seat, a unit or a role is called in a query of its own: it answers
/// ids, and the names are asked of Tenancy's directory. And what a model says of its own rows: each one that has
/// a tenant is kept to the caller's.
/// </summary>
/// <remarks>
/// No test here opens a database: a model is built from the provider alone, for Postgres, which the sample runs
/// on. The contexts are the ones the host registers, read from the host, so a module that is added is held to
/// this without being added here.
/// </remarks>
public sealed class ModuleModelTests
{
    /// <summary>Every context the host registers, by the options it registers for each.</summary>
    private static IReadOnlyList<Type> ContextsOfTheHost { get; } =
    [
        .. HostRegistrations.All
            .Select(descriptor => descriptor.ServiceType)
            .Where(service => service.IsGenericType && service.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(service => service.GetGenericArguments()[0])
            .Distinct(),
    ];

    [Fact]
    public void No_module_model_reads_more_of_tenancy_than_access_facts()
    {
        ContextsOfTheHost.Should().Contain([typeof(TenantsContext), typeof(ProjectsContext), typeof(InspectionsContext)], "the host registers every module's context");

        foreach (var contextType in ContextsOfTheHost.Where(context => context != typeof(TenantsContext)))
        {
            using var context = Make(contextType);
            var model = context.Model;

            // A model that mapped one of Tenancy's own tables would be taken for Tenancy's, and not judged: no
            // module's does. The rights and the closure are read rows here, each mapped as a function.
            model.GetEntityTypes().Where(entityType => TenancyModel.TableOf(entityType) is not null).Select(entityType => entityType.DisplayName())
                .Should().BeEmpty("{0} maps no table of Tenancy's", contextType.Name);

            // Tenancy's tables are in Tenancy's schema and nowhere else, so that is the schema a model is asked under.
            TenancyModel.ReadsBeyondAccessFacts(model, TenantsContext.Schema)
                .Should().BeEmpty("{0} reads Tenancy through the rows of its read model alone", contextType.Name);
        }

        // The check has something to judge: Projects maps the six rows, each without a name.
        using var projects = Make(typeof(ProjectsContext));
        projects.Model.FindEntityType(typeof(SeatRow<TenantId, SeatId>))!.GetProperties().Select(property => property.Name).Should().BeEquivalentTo("Id", "TenantId", "Status");
        projects.Model.FindEntityType(typeof(RoleRow<TenantId, RoleId>))!.GetProperties().Select(property => property.Name).Should().BeEquivalentTo("Id", "TenantId", "FromPack", "Status", "Keys");
        projects.Model.FindEntityType(typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>))!.GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo("Id", "TenantId", "ParentId", "Status");

        // And Tenancy's own model, which maps the tables the names are in, is Tenancy's: it is not judged.
        using var tenancy = Make(typeof(TenantsContext));
        tenancy.Model.GetEntityTypes().Select(TenancyModel.TableOf).Should().Contain([TenancyTable.Seat, TenancyTable.Role, TenancyTable.Unit]);
        TenancyModel.ReadsBeyondAccessFacts(tenancy.Model, TenantsContext.Schema).Should().BeEmpty();
    }

    /// <summary>
    /// Every entity of a model that has a tenant is read through the tenant filter and marked for the save check.
    /// On Postgres the exported policies keep another tenant's rows back whatever the application asks, so no
    /// answer would show an entity that lost <c>ScopeToTenant</c>. One that records who changed its rows stops
    /// the host at its start without it; for any other, the application's own lock would be gone and every
    /// request would be answered as before. So it is held for each of them here, on the model, where it shows.
    /// </summary>
    [Fact]
    public void Every_entity_with_a_tenant_is_kept_to_the_callers_tenant()
    {
        var judged = new List<Type>();
        foreach (var contextType in ContextsOfTheHost)
        {
            using var context = Make(contextType);

            // What has a tenant: an entity with a property of the tenant's id, its key included, as a tenant's own
            // row has it, that a query of the context reads from a table, a view or a function. An owned type is
            // kept through its owner. A row mapped to nothing is the answer to one statement the package writes
            // out, which no query reads as a set. And Tenancy's access history has no filter by design: the
            // toolkit's retention reads the table whole, so whoever reads a tenant's history names the tenant.
            var history = TenancyModel.EventLogOf(context.Model)?.Log;
            var withATenant = context.Model.GetEntityTypes()
                .Where(entityType => !entityType.IsOwned()
                    && entityType != history
                    && (entityType.GetTableName() ?? entityType.GetViewName() ?? entityType.GetFunctionName()) is not null
                    && entityType.GetProperties().Any(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(TenantId)))
                .ToList();

            foreach (var entityType in withATenant)
            {
                entityType.GetRootType().GetDeclaredQueryFilters().Select(filter => filter.Key)
                    .Should().Contain(TenancyQueryFilter.Name, "{0} of {1} has a tenant, so it is read through the tenant filter", entityType.DisplayName(), contextType.Name);
                TenancyModel.TenantPropertyOf(entityType)
                    .Should().NotBeNull("{0} of {1} has a tenant, so the save check is told which property holds it", entityType.DisplayName(), contextType.Name);
            }

            judged.AddRange(withATenant.Select(entityType => entityType.ClrType));
        }

        // The check has something to judge: each module's own rows, Tenancy's, and the rows a module reads of Tenancy's.
        judged.Should().Contain([typeof(Project), typeof(Inspection), typeof(Seat), typeof(SeatRow<TenantId, SeatId>)]);
    }

    [Fact]
    public void A_model_that_maps_a_name_of_tenancys_is_found()
    {
        // Projects' own context, with one thing more in its model each time: what a module would map to read a name.
        using var typeOfItsOwn = Make(typeof(ProjectsContext), options => options.ReplaceService<IModelCustomizer, WithASeatOfItsOwn>());
        using var shadowName = Make(typeof(ProjectsContext), options => options.ReplaceService<IModelCustomizer, WithARolesName>());
        using var underNoSchema = Make(typeof(ProjectsContext), options => options.ReplaceService<IModelCustomizer, WithASeatUnderNoSchema>());

        TenancyModel.ReadsBeyondAccessFacts(typeOfItsOwn.Model, TenantsContext.Schema).Should().ContainSingle()
            .Which.Should().StartWith("NamedSeat is mapped onto the table tenancy.Seats, which is Tenancy's.");
        TenancyModel.ReadsBeyondAccessFacts(shadowName.Model, TenantsContext.Schema).Should().ContainSingle()
            .Which.Should().StartWith("RoleRow<TenantId, RoleId> has the property Name, which is no part of Tenancy's read model.");

        // The same type under the seats' name with no schema, which puts it in the module's own. Postgres keeps a
        // table of that name there apart from Tenancy's, so it reads nothing of Tenancy's.
        underNoSchema.Model.FindEntityType(typeof(NamedSeat))!.GetSchema().Should().Be(ProjectsContext.Schema);
        TenancyModel.ReadsBeyondAccessFacts(underNoSchema.Model, TenantsContext.Schema)
            .Should().BeEmpty("projects.Seats is a table of the module's own");

        // The planted models are the context's own in everything else, and the context as it is passes.
        typeOfItsOwn.Model.FindEntityType(typeof(Project)).Should().NotBeNull();
        using var asItIs = Make(typeof(ProjectsContext));
        TenancyModel.ReadsBeyondAccessFacts(asItIs.Model, TenantsContext.Schema).Should().BeEmpty();
    }

    /// <summary>A context of the host's, with the model it has on Postgres. It never connects.</summary>
    private static DbContext Make(Type context, Action<DbContextOptionsBuilder>? configure = null)
    {
        var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(context))!;
        options.UseNpgsql("Host=model-only");
        configure?.Invoke(options);
        return (DbContext)Activator.CreateInstance(context, options.Options)!;
    }

    /// <summary>A seat with its name, as a module might map one for itself.</summary>
    private sealed class NamedSeat
    {
        public Guid Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// The context's own model, and a type of the module's own over Tenancy's seats: on the table itself, which is
    /// where a seat's name is. The read row is what Tenancy's function answers, and that has no name.
    /// </summary>
    private sealed class WithASeatOfItsOwn(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<NamedSeat>().ToTable("Seats", TenantsContext.Schema).HasKey(seat => seat.Id);
        }
    }

    /// <summary>The context's own model, and the same type under the seats' name with no schema named.</summary>
    private sealed class WithASeatUnderNoSchema(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<NamedSeat>().ToTable("Seats").HasKey(seat => seat.Id);
        }
    }

    /// <summary>The context's own model, and the roles' name as a shadow property of the read row.</summary>
    private sealed class WithARolesName(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<RoleRow<TenantId, RoleId>>().Property<string>("Name");
        }
    }
}
