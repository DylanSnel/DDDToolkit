using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy's SQL functions answer as its C# questions do, for every seeded person in every tenant they might pick and
/// for every permission of the catalogue: the policies and the use cases decide on the same facts. The C# side resolves the
/// caller as the application does, from the verified identity and the tenant picked, and asks over Entity Framework as
/// the tables' owner, at the moment the database's <c>now()</c> said just before; the SQL side asks as that person,
/// with that tenant in the setting.
/// </summary>
public abstract class TenancyFunctionTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static readonly Dictionary<string, (Guid Identity, TenantId Tenant, string Slug)> Callers = new(StringComparer.Ordinal)
    {
        ["Ada in harbor"] = (Ada.Identity, Harbor, "harbor"),
        ["Hiro in harbor"] = (Hiro.Identity, Harbor, "harbor"),
        ["Seth in harbor"] = (Seth.Identity, Harbor, "harbor"),
        ["Oli in harbor"] = (Oli.Identity, Harbor, "harbor"),
        ["Oli in orchard"] = (Oli.Identity, Orchard, "orchard"),
        ["Sue in harbor, suspended"] = (Sue.Identity, Harbor, "harbor"),
        ["Eve in harbor, with an ended and a future grant"] = (Eve.Identity, Harbor, "harbor"),
        ["Odette in orchard"] = (Odette.Identity, Orchard, "orchard"),
        ["Quin in quay, a suspended tenant"] = (Quin.Identity, Quay, "quay"),
        ["Ada in orchard, where she has no seat"] = (Ada.Identity, Orchard, "orchard"),
        ["A stranger in harbor"] = (Stranger, Harbor, "harbor"),
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Seats => [.. Callers.Keys];

    [Theory]
    [MemberData(nameof(Seats))]
    public async Task Every_function_answers_as_the_csharp_question_for_every_seat_permission_and_unit(string who)
    {
        var (identity, tenant, slug) = Callers[who];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var now = await TenancySeed.DatabaseNowAsync(database.ConnectionString, Cancellation);
        var permissions = TenancyPostgres.Catalogue.LivePermissions;

        var csharp = await CSharpAnswersAsync(database, identity, slug, now, permissions);
        var sql = await SqlAnswersAsync(database, identity, tenant, permissions);

        sql.Seat.Should().Be(csharp.Seat, "current_seat() is the seat the application selects");
        sql.Tenant.Should().Be(csharp.Tenant, "current_tenant() is that seat's tenant");
        sql.Readable.Should().BeEquivalentTo(csharp.Readable, "readable_units() is ReadableUnits()");
        foreach (var permission in permissions)
        {
            sql.Held[permission].Should().BeEquivalentTo(csharp.Held[permission], "units_where_i_hold_permission('{0}') is UnitsWhereIHold", permission);
            sql.Roles[permission].Should().BeEquivalentTo(csharp.Roles[permission], "roles_with_permission('{0}') is RolesWithPermission", permission);
            sql.HoldsPermission[permission].Should().Be(csharp.Held[permission].Count > 0, "holds_permission('{0}') is a unit where the permission is held", permission);
            sql.AtTheRoot[permission].Should().Be(csharp.AtTheRoot[permission], "holds_at_tenant('{0}') is HoldsAtTenantAsync", permission);
        }
    }

    [Fact]
    public async Task A_suspended_seat_or_an_inactive_tenant_answers_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Sue holds an Operator grant at South, and Quin every permission of Quay; neither answers for anything now.
        foreach (var (identity, tenant, placed) in new[] { (Sue.Identity, Harbor, South), (Quin.Identity, Quay, QuayRoot) })
        {
            await using var caller = await AsUser.PersonAsync(database, identity, tenant, Cancellation);
            (await caller.ScalarAsync<Guid?>("SELECT tenancy.current_seat()", Cancellation)).Should().BeNull();
            (await caller.ScalarAsync<long?>("SELECT tenancy.current_tenant()", Cancellation)).Should().BeNull();
            (await caller.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation)).Should().BeEmpty("the seat is still placed at {0}, and answers for no unit", placed);
            (await caller.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_permission('widget.read')", Cancellation)).Should().BeEmpty();
            (await caller.ListAsync<Guid>("SELECT tenancy.roles_with_permission('widget.read')", Cancellation)).Should().BeEmpty();
            (await caller.ScalarAsync<bool>("SELECT tenancy.holds_permission('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
            (await caller.ScalarAsync<bool>("SELECT tenancy.holds_at_tenant('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
            (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"OrganizationUnits\"", Cancellation)).Should().Be(0, "and reads no row of the tenant");
        }

        // The rows are there: the seat and the tenant are what stops them.
        await using var owner = await AsUser.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Quin.Seat.Value)).Should().BePositive();
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(1);
    }

    [Fact]
    public async Task An_expired_or_future_grant_answers_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Eve's Watcher grant at South ended yesterday, and her Operator grant there starts the day after tomorrow.
        await using var eve = await AsUser.PersonAsync(database, Eve.Identity, Harbor, Cancellation);
        (await eve.ScalarAsync<Guid?>("SELECT tenancy.current_seat()", Cancellation)).Should().Be(Eve.Seat.Value, "Eve is seated");
        (await eve.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation)).Should().Equal([South.Value], "and placed at South");
        foreach (var permission in new[] { "widget.read", "widget.change", "widget.create" })
        {
            (await eve.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_permission($1)", Cancellation, permission)).Should().BeEmpty("no grant of hers applies now");
            (await eve.ScalarAsync<bool>("SELECT tenancy.holds_permission($1)", Cancellation, permission)).Should().BeFalse();
        }

        // The rights rows are there, with the grants' periods.
        (await eve.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND (\"EndsAt\" <= now() OR \"StartsAt\" > now())",
                Cancellation,
                Eve.Seat.Value))
            .Should().Be(4, "one for the Watcher's permission, three for the Operator's");
    }

    [Fact]
    public async Task A_wrong_tenant_setting_reaches_no_seat()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Ada has a seat in Harbor only; Oli has one in Harbor and one in Orchard.
        foreach (var (identity, tenant, seat) in new (Guid, TenantId?, SeatId?)[]
                 {
                     (Ada.Identity, Harbor, Ada.Seat),
                     (Ada.Identity, Orchard, null),
                     (Ada.Identity, null, null),
                     (Ada.Identity, new TenantId(404), null),
                     (Oli.Identity, Harbor, Oli.Seat),
                     (Oli.Identity, Orchard, OliInOrchard),
                     (Stranger, Harbor, null),
                 })
        {
            await using var caller = await AsUser.PersonAsync(database, identity, tenant, Cancellation);
            (await caller.ScalarAsync<Guid?>("SELECT tenancy.current_seat()", Cancellation))
                .Should().Be(seat?.Value, "the seat is found from the verified identity and the tenant picked, never from a seat id");

            if (seat is null)
            {
                (await caller.ScalarAsync<bool>("SELECT tenancy.holds_permission('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
                (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task Readable_units_stay_in_the_callers_tenant_whatever_a_placement_row_says()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // A placement row no use case writes, past every policy: Oli's seat in Orchard, at Harbor's North, of Harbor.
        await using (var owner = await AsUser.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync(
                    "INSERT INTO tenancy.\"SeatPlacements\" (\"UnitId\", \"SeatId\", \"IsPrimary\", \"PlacedAt\", \"TenantId\") VALUES ($1, $2, false, now(), 1)",
                    Cancellation,
                    North.Value,
                    OliInOrchard.Value))
                .Should().Be(1);
            await owner.CommitAsync(Cancellation);
        }

        await using var inOrchard = await AsUser.PersonAsync(database, Oli.Identity, Orchard, Cancellation);
        (await inOrchard.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation)).Should().Equal(OrchardRoot.Value);
    }

    [Fact]
    public async Task Identity_tenants_lists_every_tenant_of_the_person_whatever_the_status()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database, rowLevelSecurity: false);

        foreach (var (identity, expected) in new[]
                 {
                     (Oli.Identity, new[] { Harbor, Orchard }),
                     (Sue.Identity, [Harbor]),
                     (Quin.Identity, [Quay]),
                     (Stranger, []),
                 })
        {
            // Whatever tenant the setting names, or none.
            foreach (TenantId? tenant in new TenantId?[] { null, Harbor, Orchard })
            {
                await using var caller = await AsUser.PersonAsync(database, identity, tenant, Cancellation);
                (await caller.ListAsync<long>("SELECT tenancy.identity_tenants()", Cancellation)).Should().BeEquivalentTo(expected.Select(id => id.Value));
            }

            // The seat directory the application picks a tenant from lists the same.
            var directory = await services.InScopeAsync(scoped => scoped.GetRequiredService<ISeatDirectory<TenantId, SeatId>>().AllOfAsync<HostSeat>(identity, Cancellation));
            directory.Select(seat => seat.Seat.TenantId).Should().BeEquivalentTo(expected);
        }
    }

    /// <summary>What the C# questions answer for the person, who picked <paramref name="slug"/>.</summary>
    private static async Task<Answers> CSharpAnswersAsync(TestDatabase database, Guid identity, string slug, DateTimeOffset now, IReadOnlyList<string> permissions)
    {
        await using var services = new TenancyServices(
            database,
            configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)),
            rowLevelSecurity: false);

        await using var scope = services.Scope();
        var caller = await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>()
            .ResolveAsync(CurrentUserSnapshot.User(identity), slug, Cancellation);

        using (TenancyUsers.Begin(caller))
        {
            var queries = scope.ServiceProvider.TenancyContext().Tenancy();
            var answers = new Answers(
                caller.Seat?.Value,
                caller.Kind == TenancyUserKind.Seat ? caller.Tenant?.Value : null,
                [.. (await queries.ReadableUnits().ToListAsync(Cancellation)).Select(unit => unit.Value)]);

            foreach (var permission in permissions)
            {
                answers.Held[permission] = [.. (await queries.UnitsWhereIHold(permission).ToListAsync(Cancellation)).Select(unit => unit.Value)];
                answers.Roles[permission] = [.. (await queries.RolesWithPermission(permission).ToListAsync(Cancellation)).Select(role => role.Value)];
                answers.AtTheRoot[permission] = await queries.HoldsAtTenantAsync(permission, Cancellation);
            }

            return answers;
        }
    }

    /// <summary>What the SQL functions answer for the person, with <paramref name="tenant"/> in the setting.</summary>
    private static async Task<Answers> SqlAnswersAsync(TestDatabase database, Guid identity, TenantId tenant, IReadOnlyList<string> permissions)
    {
        await using var caller = await AsUser.PersonAsync(database, identity, tenant, Cancellation);
        var answers = new Answers(
            await caller.ScalarAsync<Guid?>("SELECT tenancy.current_seat()", Cancellation),
            await caller.ScalarAsync<long?>("SELECT tenancy.current_tenant()", Cancellation),
            await caller.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation));

        foreach (var permission in permissions)
        {
            answers.Held[permission] = await caller.ListAsync<Guid>("SELECT tenancy.units_where_i_hold_permission($1)", Cancellation, permission);
            answers.Roles[permission] = await caller.ListAsync<Guid>("SELECT tenancy.roles_with_permission($1)", Cancellation, permission);
            answers.HoldsPermission[permission] = await caller.ScalarAsync<bool>("SELECT tenancy.holds_permission($1)", Cancellation, permission);
            answers.AtTheRoot[permission] = await caller.ScalarAsync<bool>("SELECT tenancy.holds_at_tenant($1)", Cancellation, permission);
        }

        return answers;
    }

    /// <summary>What one side answered, by permission where a question takes one.</summary>
    private sealed record Answers(Guid? Seat, long? Tenant, List<Guid> Readable)
    {
        public Dictionary<string, List<Guid>> Held { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<Guid>> Roles { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> HoldsPermission { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> AtTheRoot { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Tenancy's functions answer as its C# questions do, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyFunctionTestsOnDefaultNames(TenancyPostgres postgres) : TenancyFunctionTests(postgres, TenancyNaming.Default);

/// <summary>Tenancy's functions answer as its C# questions do, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyFunctionTestsOnSnakeCase(TenancyPostgres postgres) : TenancyFunctionTests(postgres, TenancyNaming.SnakeCase);
