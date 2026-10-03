using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy's SQL functions answer as its C# questions do, for every seeded person in every tenant they might pick and
/// for every key of the catalogue: the policies and the use cases decide on the same facts. The C# side resolves the
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
    public async Task Every_function_answers_as_the_csharp_question_for_every_seat_key_and_unit(string who)
    {
        var (identity, tenant, slug) = Callers[who];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var now = await TenancySeed.DatabaseNowAsync(database.ConnectionString, Cancellation);
        var keys = TenancyPostgres.Catalogue.LiveKeys;

        var csharp = await CSharpAnswersAsync(database, identity, slug, now, keys);
        var sql = await SqlAnswersAsync(database, identity, tenant, keys);

        sql.Seat.Should().Be(csharp.Seat, "caller_seat() is the seat the application selects");
        sql.Tenant.Should().Be(csharp.Tenant, "caller_tenant() is that seat's tenant");
        sql.Readable.Should().BeEquivalentTo(csharp.Readable, "readable_units() is ReadableUnits()");
        foreach (var key in keys)
        {
            sql.Held[key].Should().BeEquivalentTo(csharp.Held[key], "units_where_i_hold('{0}') is UnitsWhereIHold", key);
            sql.Roles[key].Should().BeEquivalentTo(csharp.Roles[key], "roles_with_key('{0}') is RolesWithKey", key);
            sql.HoldsKey[key].Should().Be(csharp.Held[key].Count > 0, "holds_key('{0}') is a unit where the key is held", key);
            sql.AtTheRoot[key].Should().Be(csharp.AtTheRoot[key], "holds_tenant_wide('{0}') is HoldsTenantWideAsync", key);
        }
    }

    [Fact]
    public async Task A_suspended_seat_or_an_inactive_tenant_answers_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Sue holds an Operator grant at South, and Quin every key of Quay; neither answers for anything now.
        foreach (var (identity, tenant, placed) in new[] { (Sue.Identity, Harbor, South), (Quin.Identity, Quay, QuayRoot) })
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
            (await caller.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation)).Should().BeNull();
            (await caller.ScalarAsync<long?>("SELECT tenancy.caller_tenant()", Cancellation)).Should().BeNull();
            (await caller.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation)).Should().BeEmpty("the seat is still placed at {0}, and answers for no unit", placed);
            (await caller.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('widget.read')", Cancellation)).Should().BeEmpty();
            (await caller.ListAsync<Guid>("SELECT tenancy.roles_with_key('widget.read')", Cancellation)).Should().BeEmpty();
            (await caller.ScalarAsync<bool>("SELECT tenancy.holds_key('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
            (await caller.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
            (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"OrganizationUnits\"", Cancellation)).Should().Be(0, "and reads no row of the tenant");
        }

        // The rows are there: the seat and the tenant are what stops them.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Quin.Seat.Value)).Should().BePositive();
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatPlacements\" WHERE \"SeatId\" = $1", Cancellation, Sue.Seat.Value)).Should().Be(1);
    }

    [Fact]
    public async Task An_expired_or_future_grant_answers_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Eve's Watcher grant at South ended yesterday, and her Operator grant there starts the day after tomorrow.
        await using var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation);
        (await eve.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation)).Should().Be(Eve.Seat.Value, "Eve is seated");
        (await eve.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation)).Should().Equal([South.Value], "and placed at South");
        foreach (var key in new[] { "widget.read", "widget.change", "widget.create" })
        {
            (await eve.ListAsync<Guid>("SELECT tenancy.units_where_i_hold($1)", Cancellation, key)).Should().BeEmpty("no grant of hers applies now");
            (await eve.ScalarAsync<bool>("SELECT tenancy.holds_key($1)", Cancellation, key)).Should().BeFalse();
        }

        // The rights rows are there, with the grants' periods.
        (await eve.ScalarAsync<long>(
                "SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1 AND (\"EndsAt\" <= now() OR \"StartsAt\" > now())",
                Cancellation,
                Eve.Seat.Value))
            .Should().Be(4, "one for the Watcher's key, three for the Operator's");
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
            await using var caller = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
            (await caller.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation))
                .Should().Be(seat?.Value, "the seat is found from the verified identity and the tenant picked, never from a seat id");

            if (seat is null)
            {
                (await caller.ScalarAsync<bool>("SELECT tenancy.holds_key('tenancy.roles.manage')", Cancellation)).Should().BeFalse();
                (await caller.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\"", Cancellation)).Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task Readable_units_stay_in_the_callers_tenant_whatever_a_placement_row_says()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // A placement row no use case writes, past every policy: Oli's seat in Orchard, at Harbor's North, of Harbor.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync(
                    "INSERT INTO tenancy.\"SeatPlacements\" (\"UnitId\", \"SeatId\", \"IsPrimary\", \"PlacedAt\", \"TenantId\") VALUES ($1, $2, false, now(), 1)",
                    Cancellation,
                    North.Value,
                    OliInOrchard.Value))
                .Should().Be(1);
            await owner.CommitAsync(Cancellation);
        }

        await using var inOrchard = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation);
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
                await using var caller = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
                (await caller.ListAsync<long>("SELECT tenancy.identity_tenants()", Cancellation)).Should().BeEquivalentTo(expected.Select(id => id.Value));
            }

            // The seat directory the application picks a tenant from lists the same.
            var directory = await services.InScopeAsync(scoped => scoped.GetRequiredService<ISeatDirectory<TenantId, SeatId>>().AllOfAsync(identity, Cancellation));
            directory.Select(seat => seat.Tenant).Should().BeEquivalentTo(expected);
        }
    }

    /// <summary>What the C# questions answer for the person, who picked <paramref name="slug"/>.</summary>
    private static async Task<Answers> CSharpAnswersAsync(TestDatabase database, Guid identity, string slug, DateTimeOffset now, IReadOnlyList<string> keys)
    {
        await using var services = new TenancyServices(
            database,
            configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)),
            rowLevelSecurity: false);

        await using var scope = services.Scope();
        var caller = await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>()
            .ResolveAsync(Caller.User(identity), slug, Cancellation);

        using (TenancyCallers.Begin(caller))
        {
            var questions = scope.ServiceProvider.Answers().Over(scope.ServiceProvider.Tenancy());
            var answers = new Answers(
                caller.Seat?.Value,
                caller.Kind == TenancyCallerKind.Seat ? caller.Tenant?.Value : null,
                [.. (await questions.ReadableUnits().ToListAsync(Cancellation)).Select(unit => unit.Value)]);

            foreach (var key in keys)
            {
                answers.Held[key] = [.. (await questions.UnitsWhereIHold(key).ToListAsync(Cancellation)).Select(unit => unit.Value)];
                answers.Roles[key] = [.. (await questions.RolesWithKey(key).ToListAsync(Cancellation)).Select(role => role.Value)];
                answers.AtTheRoot[key] = await questions.HoldsTenantWideAsync(key, Cancellation);
            }

            return answers;
        }
    }

    /// <summary>What the SQL functions answer for the person, with <paramref name="tenant"/> in the setting.</summary>
    private static async Task<Answers> SqlAnswersAsync(TestDatabase database, Guid identity, TenantId tenant, IReadOnlyList<string> keys)
    {
        await using var caller = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
        var answers = new Answers(
            await caller.ScalarAsync<Guid?>("SELECT tenancy.caller_seat()", Cancellation),
            await caller.ScalarAsync<long?>("SELECT tenancy.caller_tenant()", Cancellation),
            await caller.ListAsync<Guid>("SELECT tenancy.readable_units()", Cancellation));

        foreach (var key in keys)
        {
            answers.Held[key] = await caller.ListAsync<Guid>("SELECT tenancy.units_where_i_hold($1)", Cancellation, key);
            answers.Roles[key] = await caller.ListAsync<Guid>("SELECT tenancy.roles_with_key($1)", Cancellation, key);
            answers.HoldsKey[key] = await caller.ScalarAsync<bool>("SELECT tenancy.holds_key($1)", Cancellation, key);
            answers.AtTheRoot[key] = await caller.ScalarAsync<bool>("SELECT tenancy.holds_tenant_wide($1)", Cancellation, key);
        }

        return answers;
    }

    /// <summary>What one side answered, by key where a question takes one.</summary>
    private sealed record Answers(Guid? Seat, long? Tenant, List<Guid> Readable)
    {
        public Dictionary<string, List<Guid>> Held { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<Guid>> Roles { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> HoldsKey { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, bool> AtTheRoot { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Tenancy's functions answer as its C# questions do, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyFunctionTestsOnDefaultNames(TenancyPostgres postgres) : TenancyFunctionTests(postgres, TenancyNaming.Default);

/// <summary>Tenancy's functions answer as its C# questions do, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyFunctionTestsOnSnakeCase(TenancyPostgres postgres) : TenancyFunctionTests(postgres, TenancyNaming.SnakeCase);
