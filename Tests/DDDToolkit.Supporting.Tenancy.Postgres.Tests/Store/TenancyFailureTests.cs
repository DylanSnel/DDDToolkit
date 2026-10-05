using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// What Postgres refuses in a save of Tenancy's store reaches the caller as the refusal the use case gives for the
/// same rule, where the database sees what the use case could not ask about first: each unique index answers with
/// the refusal it declares, the trigger that keeps an administrator is translated, and a row a policy denies is
/// <c>access.refused</c>. Anything else goes on as the failure it is.
/// </summary>
public abstract class TenancyFailureTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_taken_slug_under_system_in_is_slug_taken()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Provisioning acts in the tenant it makes, so the policies hide Harbor's slug from its own check: the unique
        // index on the slug refuses the save instead.
        RefusalException refusal;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            refusal = (await FluentActions.Awaiting(() => services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                    new HostTenancy.TenantToProvision("harbor", "Second Harbor", TenantShape.Flat, "Second", Guid.NewGuid(), "Someone", TenantId: new TenantId(40)),
                    Cancellation)))
                .Should().ThrowAsync<RefusalException>()).Which;
        }

        refusal.Code.Should().Be(TenancyRefusals.SlugTaken);
        refusal.Arguments.Should().Contain(new KeyValuePair<string, object?>("Slug", "harbor"));
        refusal.InnerException.Should().NotBeNull("the refusal keeps the failure it stands for");
    }

    [Fact]
    public async Task A_second_root_is_refused_by_the_index()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Past the aggregate, which never makes one: a second root of Harbor, written by Tenancy's own system work,
        // which the policies let write the tree.
        PostgresException refused;
        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            refused = (await FluentActions.Awaiting(() => system.ExecuteAsync(
                    "INSERT INTO tenancy.\"OrganizationUnits\" (\"Id\", \"TenantId\", \"ParentId\", \"Name\", \"Status\") VALUES (gen_random_uuid(), 1, NULL, 'Second', 'Active')",
                    Cancellation))
                .Should().ThrowAsync<PostgresException>()).Which;
        }

        refused.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        refused.ConstraintName.Should().Be(names.Of("IX_OrganizationUnits_TenantId_WhereRoot"));

        // Met by a save, it is the refusal the aggregate gives for the same rule. The aggregate's own check would
        // refuse first, so the save is made by a context that has only the interceptor that answers the database,
        // named as the database's tables are.
        await using var services = new TenancyServices(database);
        await using var scope = services.Scope();
        var options = new DbContextOptionsBuilder<TestTenancyContext>().UseNpgsql(database.ConnectionString).AddInterceptors(new DatabaseRefusalInterceptor());
        database.Names.Configure(options);
        options.UsePostgresRowLevelSecurity(scope.ServiceProvider);

        RefusalException refusal;
        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor))
        await using (var past = new TestTenancyContext(options.Options))
        {
            var organization = await past.Set<HostOrganization>().SingleAsync(row => row.Id == Harbor, Cancellation);
            past.Entry(organization.Units.Single(unit => unit.Id == North)).Property(unit => unit.ParentId).CurrentValue = null;

            refusal = (await FluentActions.Awaiting(() => past.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        }

        refusal.Code.Should().Be(TenancyRefusals.OneRoot);
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        UniqueViolationOf(refusal).Should().Be(names.Of("IX_OrganizationUnits_TenantId_WhereRoot"));
    }

    [Fact]
    public async Task A_save_the_policies_deny_is_access_refused()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // System work of another module's scope is system work to the use cases, which let it through; the
        // policies let it read Tenancy's tables and write none of them. A new row is refused in so many words.
        var adding = await FluentActions.Awaiting(() => services.BySystemIn(
                Harbor,
                scoped => scoped.Seats().AddSeatAsync(Guid.NewGuid(), "Zed", Cancellation),
                scope: "widgets"))
            .Should().ThrowAsync<RefusalException>();

        adding.Which.Code.Should().Be(ToolkitRefusals.Refused);
        adding.Which.Kind.Should().Be(RefusalKind.NotPermitted);
        var denied = adding.Which.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        denied.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // A change of access it makes through a use case raises an event the history keeps, and the save adds a row
        // to the access history for it: only Tenancy's own scope adds to a tenant's history, so that row is refused
        // in so many words too.
        var placing = await FluentActions.Awaiting(() => services.BySystemIn(
                Harbor,
                scoped => scoped.Seats().MakePrimaryAsync(Seth.Seat, NorthPier, Cancellation),
                scope: "widgets"))
            .Should().ThrowAsync<RefusalException>();

        placing.Which.Code.Should().Be(ToolkitRefusals.Refused);
        placing.Which.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // A row it reads and may not change is hidden from its statement, which finds no row, as a lost race
        // does. Nobody else wrote, so it is the same refusal, and no conflict to retry. A change that adds nothing
        // to the history shows it: that save writes the one row and nothing it may add.
        var retitling = await FluentActions.Awaiting(() => services.BySystemIn(
                Harbor,
                async scoped =>
                {
                    var tenancy = scoped.Tenancy();
                    var seth = await tenancy.Set<HostSeat>().SingleAsync(seat => seat.Id == Seth.Seat, Cancellation);
                    seth.ChangeJobTitle("Foreman");
                    await tenancy.SaveChangesAsync(Cancellation);
                },
                scope: "widgets"))
            .Should().ThrowAsync<RefusalException>();

        retitling.Which.Code.Should().Be(ToolkitRefusals.Refused);
        retitling.Which.InnerException.Should().BeOfType<ConcurrencyConflictException>();

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\" WHERE \"DisplayName\" = 'Zed' OR \"JobTitle\" = 'Foreman'", Cancellation))
            .Should().Be(0, "nothing was written");
        (await owner.ScalarAsync<long>(
            "SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1 AND \"UnitId\" = $2 AND \"IsPrimary\"",
            Cancellation,
            Seth.Seat.Value,
            NorthPier.Value)).Should().Be(0, "the seat's primary placement is where it was");

        // In Tenancy's own scope the change goes through.
        await services.BySystemIn(Harbor, scoped => scoped.Seats().MakePrimaryAsync(Seth.Seat, NorthPier, Cancellation));
    }

    [Fact]
    public async Task A_second_primary_is_second_primary()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Seth's seat is loaded, with North primary; meanwhile a writer past the aggregate places him at South and makes
        // that his primary placement. The aggregate, which cannot see it, makes North Pier primary.
        var refusal = await FluentActions.Awaiting(() => services.BySystemIn(Harbor, async scoped =>
            {
                var store = scoped.GetRequiredService<HostTenancy.IStore>();
                var seth = (await store.FindSeatAsync(Seth.Seat, Cancellation))!;

                await using (var other = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
                {
                    (await other.ExecuteAsync("UPDATE tenancy.\"SeatPlacements\" SET \"IsPrimary\" = false WHERE \"SeatId\" = $1 AND \"UnitId\" = $2", Cancellation, Seth.Seat.Value, North.Value))
                        .Should().Be(1);
                    (await other.ExecuteAsync(
                            "INSERT INTO tenancy.\"SeatPlacements\" (\"UnitId\", \"SeatId\", \"IsPrimary\", \"PlacedAt\", \"TenantId\") VALUES ($1, $2, true, now(), 1)",
                            Cancellation,
                            South.Value,
                            Seth.Seat.Value))
                        .Should().Be(1);
                    await other.CommitAsync(Cancellation);
                }

                seth.MakePrimary(NorthPier);
                await store.SaveAsync(Cancellation);
            }))
            .Should().ThrowAsync<RefusalException>();

        refusal.Which.Code.Should().Be(TenancyRefusals.SecondPrimary);
        UniqueViolationOf(refusal.Which).Should().Be(names.Of("IX_SeatPlacements_SeatId_WherePrimary"));
    }

    [Fact]
    public async Task The_administrator_trigger_is_last_admin()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        // Past the use case, which would have refused it first: Harbor's one administrator gives up the role.
        var refusal = await FluentActions.Awaiting(() => services.BySystemIn(Harbor, async scoped =>
            {
                var store = scoped.GetRequiredService<HostTenancy.IStore>();
                var ada = (await store.FindSeatAsync(Ada.Seat, Cancellation))!;
                ada.Revoke(HarborRoot, HarborRoles.Administrator);
                await store.SaveAsync(Cancellation);
            }))
            .Should().ThrowAsync<RefusalException>();

        refusal.Which.Code.Should().Be(TenancyRefusals.LastAdmin);
        refusal.Which.InnerException.Should().BeAssignableTo<Exception>().Which.ToString().Should().Contain("tenancy_administrator_remains");
    }

    [Fact]
    public async Task A_rights_trigger_failure_is_rethrown()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // No caller writes a right, so the store's context runs as the application here, which owns the tables.
        await using var services = new TenancyServices(database, rowLevelSecurity: false);

        // A right no grant backs, written through the store's own context: not a refusal of any use case.
        var failure = await FluentActions.Awaiting(() => services.BySystemIn(Harbor, async scoped =>
            {
                scoped.Tenancy().Add(new SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>
                {
                    TenantId = Harbor,
                    SeatId = Oli.Seat,
                    UnitId = NorthPier,
                    RoleId = HarborRoles.Watcher,
                    Key = TestHost.HostCatalogue.WidgetRead,
                    StartsAt = DateTimeOffset.UtcNow,
                });
                await scoped.GetRequiredService<HostTenancy.IStore>().SaveAsync(Cancellation);
            }))
            .Should().ThrowAsync<Exception>();

        failure.Which.Should().NotBeAssignableTo<RefusalException>();
        failure.Which.ToString().Should().Contain("tenancy_rights_backed_by_grants");
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND \"RoleId\" = $2", Cancellation, Oli.Seat.Value, HarborRoles.Watcher.Value))
            .Should().Be(0, "the transaction was rolled back");
    }

    /// <summary>The unique index Postgres named in the failure <paramref name="refusal"/> stands for.</summary>
    private static string? UniqueViolationOf(RefusalException refusal)
    {
        var failure = refusal.InnerException;
        while (failure is not null and not PostgresException)
        {
            failure = failure.InnerException;
        }

        var postgres = failure.Should().BeOfType<PostgresException>("the refusal keeps the failure it stands for").Subject;
        postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        return postgres.ConstraintName;
    }
}

/// <summary>What Postgres refuses, as the refusal it stands for, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyFailureTestsOnDefaultNames(TenancyPostgres postgres) : TenancyFailureTests(postgres, TenancyNaming.Default);

/// <summary>What Postgres refuses, as the refusal it stands for, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyFailureTestsOnSnakeCase(TenancyPostgres postgres) : TenancyFailureTests(postgres, TenancyNaming.SnakeCase);
