using Examples.Hosting;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Persistence;

/// <summary>
/// How Projects reads Tenancy: through Tenancy's read functions, so the module reads no table of Tenancy's. No
/// test here opens a database: a model is built from the provider alone.
/// </summary>
public sealed class ProjectsModelTests
{
    /// <summary>The rows the access questions read, each with the function of Tenancy's it comes from.</summary>
    private static readonly (Type Row, string Function)[] ReadModel =
    [
        (typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>), TenancyFunctionNames.CallerRights),
        (typeof(OrganizationUnitPath<TenantId, OrganizationUnitId>), TenancyFunctionNames.TenantUnitPaths),
        (typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>), TenancyFunctionNames.TenantUnits),
        (typeof(RoleRow<TenantId, RoleId>), TenancyFunctionNames.TenantRoles),
        (typeof(PlacementRow<SeatId, OrganizationUnitId>), TenancyFunctionNames.TenantPlacements),
        (typeof(SeatRow<TenantId, SeatId>), TenancyFunctionNames.TenantSeats),
    ];

    [Fact]
    public void The_projects_model_on_postgres_asks_tenancy_through_its_functions()
    {
        // The context as a host on Postgres configures it. Nothing connects: the address leads nowhere.
        var options = new DbContextOptionsBuilder<ProjectsContext>();
        ModuleDatabase.UsePostgres(options, "Host=model-only", ProjectsContext.Schema);
        using var projects = new ProjectsContext(options.Options);
        var model = projects.Model;

        TenancyModel.ReadsThroughFunctions(model).Should().BeTrue();
        foreach (var (row, function) in ReadModel)
        {
            var entityType = model.FindEntityType(row)!;
            entityType.GetFunctionName().Should().Be(function, "{0} is read from Tenancy's function", row.Name);
            model.FindDbFunction(function)!.Schema.Should().Be(ProjectsContext.TenancyTablesSchema);
            entityType.GetTableName().Should().BeNull();
            entityType.GetViewName().Should().BeNull();
        }

        // Its own tables are all the tables it has, and it maps no view at all: nothing of Tenancy's but functions.
        model.GetEntityTypes().Where(entityType => entityType.GetTableName() is not null).Select(entityType => entityType.GetSchema() + "." + entityType.GetTableName())
            .Should().BeEquivalentTo(
                ProjectsContext.Schema + "." + ProjectsContext.ProjectsTable,
                ProjectsContext.Schema + "." + ProjectsContext.CrewTable,
                ProjectsContext.Schema + "." + ProjectsContext.CrewRolesTable,
                ProjectsContext.Schema + "." + ProjectsContext.ProjectRolesTable,
                ProjectsContext.Schema + "." + ProjectsContext.OutboxTable);
        model.GetEntityTypes().Should().NotContain(entityType => entityType.GetViewName() != null);

        // A question is asked of the functions, inside the module's own query. Nobody is calling here, so the tenant
        // filter, which would answer nobody nothing, is left out of the statement.
        var mine = projects.Projects.IgnoreQueryFilters()
            .Where(project => projects.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().Any(right => right.UnitId == project.UnitId));
        mine.ToQueryString().Should().Contain("FROM tenancy.caller_rights() AS").And.Contain("FROM projects.\"Projects\" AS").And.NotContain("\"SeatRights\"");
    }

    [Fact]
    public void The_projects_context_is_taken_from_a_pool_with_the_model_of_its_database()
    {
        // A host that runs the parts of a request side by side hands each a context from a pool. The model, with
        // the functions it reads Tenancy through, is built once for the database, not per instance.
        var services = new ServiceCollection();
        services.AddPooledDbContextFactory<ProjectsContext>(options => ModuleDatabase.UsePostgres(options, "Host=model-only", ProjectsContext.Schema));
        using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<IDbContextFactory<ProjectsContext>>();

        ProjectsContext first;
        using (first = pool.CreateDbContext())
        {
            TenancyModel.ReadsThroughFunctions(first.Model).Should().BeTrue();
        }

        using var again = pool.CreateDbContext();
        again.Should().BeSameAs(first, "the pool hands the context out again");
        TenancyModel.ReadsThroughFunctions(again.Model).Should().BeTrue();
        using var beside = pool.CreateDbContext();
        beside.Should().NotBeSameAs(again);
        beside.Model.Should().BeSameAs(again.Model, "every context of the pool has the one model");
    }
}
