using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.Supporting.Together.Tests;

/// <summary>
/// The starter roles of a resource work as Tenancy's do. Provisioning a college makes the roles of its courses, and the
/// sync of the starter roles makes a missing one and makes the rest follow. The host writes nothing for it but the
/// registration the generator writes. Each claim is asked on SQLite and on Postgres, under row level security, so system
/// work in the college passes the policies of the role table.
/// </summary>
public abstract class StarterRolesOfACollegeTests(CampusDatabases databases)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Task<List<CourseRole>> RolesOfAsync(CampusApp campus, TenantId college)
        => campus.Services.BySystemInAsync(college, provider => provider.Campus().CourseRoles.AsNoTracking().OrderBy(role => role.FromStarterRole).ToListAsync(Cancellation));

    private static async Task<TenantId> ProvisionAsync(CampusApp campus, string slug)
    {
        var tenant = await campus.Services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(
            new TenantToProvision(slug, slug, TenantShape.Flat, slug, Guid.CreateVersion7()),
            Cancellation));
        return tenant.Id;
    }

    /// <summary>Puts a role back as an older version of the application left it: its permissions and what it remembers of its starter role.</summary>
    private static Task AsAnOlderVersionLeftItAsync(CampusApp campus, TenantId college, string starterRole, string[] permissions, string[]? fromStarterRole)
        => campus.Services.ChangeAsync(college, async context =>
        {
            var role = await context.CourseRoles.AsTracking().SingleAsync(candidate => candidate.FromStarterRole == starterRole, Cancellation);
            context.Entry(role).Property(row => row.Permissions).CurrentValue = permissions;
            context.Entry(role).Property(row => row.PermissionsFromStarterRole).CurrentValue = fromStarterRole;
        });

    private static IStarterRoleSync Sync(CampusApp campus) => campus.Services.Provider.GetRequiredService<IStarterRoleSync>();

    [Fact]
    public async Task A_new_college_has_the_roles_of_its_courses_right_after_it_is_provisioned()
    {
        using var campus = await databases.CreateAsync();

        var cedar = await ProvisionAsync(campus, "cedar");

        var roles = await RolesOfAsync(campus, cedar);
        roles.Select(role => (role.FromStarterRole, role.Name, role.TenantId, role.Status)).Should().Equal(
            (CourseMembership.Convenor, CourseMembership.Convenor, cedar, CustomRoleStatus.Active),
            (CourseMembership.Tutor, CourseMembership.Tutor, cedar, CustomRoleStatus.Active));
        roles[1].Permissions.Should().Equal(CoursePermissions.Grade, CoursePermissions.See);
        roles[1].PermissionsFromStarterRole.Should().Equal(CoursePermissions.Grade, CoursePermissions.See);

        // Each college's roles are its own, and a request in another college does not read them.
        (await RolesOfAsync(campus, campus.Scenario.Birch)).Select(role => role.TenantId).Should().OnlyContain(tenant => tenant == campus.Scenario.Birch);
    }

    [Fact]
    public async Task A_sync_of_the_starter_roles_makes_a_changed_one_follow_and_a_second_run_changes_nothing()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        await AsAnOlderVersionLeftItAsync(campus, data.Birch, CourseMembership.Tutor, [CoursePermissions.See], [CoursePermissions.See]);

        var report = await Sync(campus).SyncAsync(Cancellation);

        report.Failed.Should().BeEmpty();
        report.RolesChanged.Should().Be(1);
        report.RolesMade.Should().Be(0);
        var tutor = (await RolesOfAsync(campus, data.Birch)).Single(role => role.FromStarterRole == CourseMembership.Tutor);
        tutor.Permissions.Should().Equal(CoursePermissions.Grade, CoursePermissions.See);
        tutor.PermissionsFromStarterRole.Should().Equal(CoursePermissions.Grade, CoursePermissions.See);

        // A second run, or the next start of the host, reads and changes nothing.
        var again = await Sync(campus).SyncAsync(Cancellation);
        (again.RolesMade, again.RolesChanged, again.RolesWithoutTheirStarterRole, again.Succeeded).Should().Be((0, 0, 0, true));
    }

    [Fact]
    public async Task A_college_that_lacks_a_starter_role_gets_it_at_the_next_sync()
    {
        using var campus = await databases.CreateAsync();

        // A college whose roles of courses were lost after it was made: what a provisioning that failed halfway leaves.
        var cedar = await ProvisionAsync(campus, "cedar");
        await campus.Services.ChangeAsync(cedar, async context => context.CourseRoles.RemoveRange(await context.CourseRoles.AsTracking().ToListAsync(Cancellation)));
        (await RolesOfAsync(campus, cedar)).Should().BeEmpty();

        var report = await Sync(campus).SyncAsync(Cancellation);

        report.RolesMade.Should().Be(2);
        report.Succeeded.Should().BeTrue();
        (await RolesOfAsync(campus, cedar)).Select(role => role.FromStarterRole).Should().Equal(CourseMembership.Convenor, CourseMembership.Tutor);
    }

    [Fact]
    public async Task What_the_college_changed_stays_a_renamed_role_follows_and_an_archived_role_is_not_made_again()
    {
        using var campus = await databases.CreateAsync();
        var cedar = await ProvisionAsync(campus, "cedar");
        await campus.Services.ChangeAsync(cedar, async context =>
        {
            var roles = await context.CourseRoles.AsTracking().ToListAsync(Cancellation);
            var tutor = roles.Single(role => role.FromStarterRole == CourseMembership.Tutor);
            tutor.Rename("Lecturer", null, CourseMembership.Rules);
            tutor.HaveItGive(CoursePermissions.See);
            roles.Single(role => role.FromStarterRole == CourseMembership.Convenor).PutAway();
        });

        var report = await Sync(campus).SyncAsync(Cancellation);

        report.Succeeded.Should().BeTrue();
        var kept = await RolesOfAsync(campus, cedar);
        kept.Should().HaveCount(2, "a role told by what it was made from is not made again, whatever became of it");
        kept.Single(role => role.FromStarterRole == CourseMembership.Tutor).Should().Match<CourseRole>(role => role.Name == "Lecturer");
        kept.Single(role => role.FromStarterRole == CourseMembership.Tutor).Permissions.Should().Equal([CoursePermissions.See], "the college took grading out, and the starter role still holds it");
        kept.Single(role => role.FromStarterRole == CourseMembership.Convenor).Status.Should().Be(CustomRoleStatus.Archived);
    }

    [Fact]
    public async Task A_role_stored_before_roles_remembered_their_starter_role_gets_what_it_lacks_and_loses_nothing()
    {
        using var campus = await databases.CreateAsync();
        var cedar = await ProvisionAsync(campus, "cedar");
        await AsAnOlderVersionLeftItAsync(campus, cedar, CourseMembership.Tutor, [CoursePermissions.Outline, CoursePermissions.See], null);

        var report = await Sync(campus).SyncAsync(Cancellation);

        report.Succeeded.Should().BeTrue();
        (await RolesOfAsync(campus, cedar)).Single(role => role.FromStarterRole == CourseMembership.Tutor).Permissions
            .Should().Equal(CoursePermissions.Grade, CoursePermissions.Outline, CoursePermissions.See);
    }

    [Fact]
    public async Task A_seat_is_refused_following_the_starter_roles()
    {
        using var campus = await databases.CreateAsync();
        var data = campus.Scenario;

        var refused = await FluentActions.Awaiting(() => campus.Services.AsAsync(data.Ada, CampusScenario.InAlder, provider => provider.GetRequiredService<CourseRoleManager>()
                .FollowStarterRolesAsync(draft => new CourseRole(CourseRoleId.Create(), data.Alder, draft), Cancellation)))
            .Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>();

        refused.Which.Code.Should().Be(DDDToolkit.Exceptions.ToolkitRefusals.SystemOnly, "Ada administers the college, and what a starter role holds is the application's");
    }
}

/// <summary>The claims on SQLite, in memory.</summary>
public sealed class StarterRolesOfACollegeTestsOnSqlite() : StarterRolesOfACollegeTests(CampusDatabases.Sqlite);

/// <summary>The claims on Postgres, under row level security: system work in the college passes the policies of the role table.</summary>
public sealed class StarterRolesOfACollegeTestsOnPostgres(CampusPostgres postgres) : StarterRolesOfACollegeTests(postgres);

/// <summary>
/// What goes wrong in another module's starter roles stays there. The tenant is saved first, so provisioning returns it and
/// the failure is logged; a sync names the tenant and goes on. Any database shows it, so these run on SQLite alone.
/// </summary>
public sealed class StarterRolesThatFailTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failure_after_the_save_is_logged_and_leaves_the_tenant()
    {
        using var database = new SqliteConnection("DataSource=:memory:");
        database.Open();
        var log = new RecordingLoggers();
        using var services = Services(database, log, services => services.AddScoped<ITenantStarterRoles, Failing>());

        var tenant = await services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(
            new TenantToProvision("cedar", "Cedar", TenantShape.Flat, "Cedar", Guid.CreateVersion7()),
            Cancellation));

        tenant.Slug.Value.Should().Be("cedar", "the tenant was saved first");
        (await services.BySystemInAsync(tenant.Id, provider => provider.Tenants().SuspendAsync("unpaid", Cancellation))).Should().NotBeNull("the tenant exists");
        log.Errors.Should().ContainSingle().Which.Should().Match<LoggedError>(error => error.Failure is InvalidOperationException && error.Message.Contains(tenant.Id.ToString()!, StringComparison.Ordinal));

        // The roles of courses ran all the same: one failure belongs to the module it happened in.
        (await services.BySystemInAsync(tenant.Id, provider => provider.Campus().CourseRoles.CountAsync(Cancellation))).Should().Be(2);
    }

    [Fact]
    public async Task A_sync_names_the_tenant_that_failed_and_goes_on_with_the_others()
    {
        using var database = new SqliteConnection("DataSource=:memory:");
        database.Open();
        var failIn = new FailingTenant();
        using var services = Services(database, new RecordingLoggers(), services => services.AddSingleton(failIn).AddScoped<ITenantStarterRoles, FailingInOneTenant>());

        var first = await services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(new TenantToProvision("cedar", "Cedar", TenantShape.Flat, "Cedar", Guid.CreateVersion7()), Cancellation));
        var second = await services.BySystemAsync(provider => provider.Tenants().ProvisionAsync(new TenantToProvision("elm", "Elm", TenantShape.Flat, "Elm", Guid.CreateVersion7()), Cancellation));
        failIn.Tenant = first.Id;
        await services.ChangeAsync(second.Id, async context => context.CourseRoles.RemoveRange(await context.CourseRoles.AsTracking().ToListAsync(Cancellation)));

        var report = await services.Provider.GetRequiredService<IStarterRoleSync>().SyncAsync(Cancellation);

        report.Failed.Should().ContainSingle().Which.Tenant.Should().Be(first.Id.ToString());
        report.Tenants.Should().Be(2);
        report.RolesMade.Should().Be(2, "the other tenant went on");
        report.Succeeded.Should().BeFalse();
    }

    private static CampusServices Services(SqliteConnection database, RecordingLoggers log, Action<IServiceCollection> host)
    {
        var services = new CampusServices(
            options => options.UseSqlite(database),
            new FixedClock(),
            host: registered =>
            {
                registered.AddSingleton<ILoggerFactory>(log);
                registered.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
                host(registered);
            });
        using var scope = services.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenancyContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<CampusContext>().GetService<IRelationalDatabaseCreator>().CreateTables();
        return services;
    }

    private sealed record LoggedError(string Message, Exception? Failure);

    private sealed class Failing : ITenantStarterRoles
    {
        public Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("the module is away");
    }

    private sealed class FailingTenant
    {
        public TenantId? Tenant { get; set; }
    }

    private sealed class FailingInOneTenant(FailingTenant failIn, TenancyAccessManager access) : ITenantStarterRoles
    {
        public Task<TenantStarterRolesFollowed> FollowAsync(CancellationToken cancellationToken)
            => access.CurrentTenant.Require().Id == failIn.Tenant
                ? throw new InvalidOperationException("the module is away")
                : Task.FromResult(new TenantStarterRolesFollowed(0, 0, 0));
    }

    private sealed class RecordingLoggers : ILoggerFactory, ILogger
    {
        public List<LoggedError> Errors { get; } = [];

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(new LoggedError(formatter(state, exception), exception));
            }
        }
    }
}
