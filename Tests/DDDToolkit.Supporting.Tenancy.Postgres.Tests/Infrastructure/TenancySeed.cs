using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// The data the secured template is seeded with, through the use cases and under the policies, as system work in
/// each tenant; every id fixed, so a test names what it asks about. Every period is relative to the database's
/// clock and at least a day away from it, so a test never has to move a clock.
/// <list type="table">
/// <item><term>Harbor (1)</term><description>
/// A tree: the root, North and South below it, and North Pier below North. Ada administers it. Hiro holds the
/// Grants desk role at North (only <c>tenancy.grants.manage</c>, a key that manages access). Seth is a Supervisor at
/// North (units, seats and grants managed there, and widgets), placed at North and North Pier. Oli is an Operator at
/// North Pier. Sue is an Operator at South, and suspended. Eve is placed at South with a Watcher grant that ended
/// yesterday and an Operator grant that starts the day after tomorrow. Widgets at North, North Pier (with a part) and
/// South.
/// </description></item>
/// <item><term>Orchard (2)</term><description>Odette administers it. Oli has a seat there too, a Watcher at the root. A widget at the root.</description></item>
/// <item><term>Quay (3)</term><description>Quin administers it, and it is suspended.</description></item>
/// </list>
/// </summary>
public static class TenancySeed
{
    public static readonly TenantId Harbor = new(1);
    public static readonly TenantId Orchard = new(2);
    public static readonly TenantId Quay = new(3);

    public static readonly OrganizationUnitId HarborRoot = Unit(1);
    public static readonly OrganizationUnitId North = Unit(2);
    public static readonly OrganizationUnitId South = Unit(3);
    public static readonly OrganizationUnitId NorthPier = Unit(4);
    public static readonly OrganizationUnitId OrchardRoot = Unit(5);
    public static readonly OrganizationUnitId QuayRoot = Unit(6);

    public static readonly Person Ada = new(1, "Ada");
    public static readonly Person Hiro = new(2, "Hiro");
    public static readonly Person Seth = new(3, "Seth");
    public static readonly Person Oli = new(4, "Oli");
    public static readonly Person Sue = new(5, "Sue");
    public static readonly Person Eve = new(6, "Eve");
    public static readonly Person Odette = new(7, "Odette");
    public static readonly Person Quin = new(8, "Quin");

    /// <summary>Someone signed in who has no seat anywhere.</summary>
    public static readonly Guid Stranger = Guid.Parse("d0000000-0000-4000-8000-000000000099");

    /// <summary>Oli's seat in Orchard: the same person, another seat.</summary>
    public static readonly SeatId OliInOrchard = Seat(204);

    public static readonly Roles HarborRoles = new(1);
    public static readonly Roles OrchardRoles = new(2);
    public static readonly Roles QuayRoles = new(3);

    /// <summary>Harbor's Grants desk: <c>tenancy.grants.manage</c> alone.</summary>
    public static readonly RoleId GrantsDesk = Role(1, 9);

    public static readonly WidgetId Pump = Widget(1);
    public static readonly WidgetId Valve = Widget(2);
    public static readonly WidgetId Gauge = Widget(3);
    public static readonly WidgetId Crate = Widget(4);

    /// <summary>
    /// Seeds <paramref name="database"/>, whose access files are applied, through the use cases, with every period
    /// relative to the database's clock.
    /// </summary>
    public static async Task SeedAsync(TestDatabase database, CancellationToken cancellationToken)
    {
        var now = await DatabaseNowAsync(database.ConnectionString, cancellationToken);
        await using var services = new TenancyServices(database);
        await SeedAsync(services, now, cancellationToken);
    }

    /// <summary>
    /// Seeds the database of <paramref name="services"/>, whose access files are applied, through the use cases of
    /// those services, with every period relative to <paramref name="now"/>, the database's clock.
    /// </summary>
    public static async Task SeedAsync(TenancyServices services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await ProvisionAsync(services, Harbor, "harbor", HarborRoot, Ada, HarborRoles, cancellationToken);
        await ProvisionAsync(services, Orchard, "orchard", OrchardRoot, Odette, OrchardRoles, cancellationToken);
        await ProvisionAsync(services, Quay, "quay", QuayRoot, Quin, QuayRoles, cancellationToken);

        await services.BySystemIn(Harbor, async scoped =>
        {
            await scoped.Organization().AddUnitAsync(HarborRoot, "North", "region", cancellationToken, North);
        });
        await services.BySystemIn(Harbor, scoped => scoped.Organization().AddUnitAsync(HarborRoot, "South", "region", cancellationToken, South));
        await services.BySystemIn(Harbor, scoped => scoped.Organization().AddUnitAsync(North, "North Pier", "site", cancellationToken, NorthPier));
        await services.BySystemIn(Harbor, scoped => scoped.Roles().CreateAsync("Grants desk", "Gives roles", [TenancyKeys.GrantsManage], cancellationToken, GrantsDesk));

        await SeatPersonAsync(services, Harbor, Hiro, North, [(North, GrantsDesk, null, null)], cancellationToken);
        await SeatPersonAsync(services, Harbor, Seth, North, [(North, HarborRoles.Supervisor, null, null)], cancellationToken);
        await services.BySystemIn(Harbor, scoped => scoped.Seats().PlaceAsync(Seth.Seat, NorthPier, primary: false, cancellationToken));
        await SeatPersonAsync(services, Harbor, Oli, NorthPier, [(NorthPier, HarborRoles.Operator, null, null)], cancellationToken);
        await SeatPersonAsync(services, Harbor, Sue, South, [(South, HarborRoles.Operator, null, null)], cancellationToken);
        await services.BySystemIn(Harbor, scoped => scoped.Seats().SuspendAsync(Sue.Seat, cancellationToken));
        await SeatPersonAsync(
            services,
            Harbor,
            Eve,
            South,
            [(South, HarborRoles.Watcher, now.AddDays(-3), now.AddDays(-1)), (South, HarborRoles.Operator, now.AddDays(2), null)],
            cancellationToken);

        await services.BySystemIn(Orchard, async scoped =>
        {
            await scoped.Seats().AddSeatAsync(Oli.Identity, Oli.Name, cancellationToken, OliInOrchard);
        });
        await services.BySystemIn(Orchard, scoped => scoped.Seats().PlaceAsync(OliInOrchard, OrchardRoot, primary: true, cancellationToken));
        await services.BySystemIn(Orchard, scoped => scoped.Seats().GrantAsync(OliInOrchard, OrchardRoot, OrchardRoles.Watcher, until: null, reason: null, cancellationToken));

        await services.BySystemIn(Quay, scoped => scoped.Tenants().SuspendAsync("Paused for the tests", cancellationToken));

        // The widgets are another module's rows: its own system work writes them, in its own scope.
        await services.BySystemIn(
            Harbor,
            async scoped =>
            {
                var widgets = scoped.Widgets();
                widgets.Widgets.Add(new Widget(Pump, Harbor, North, "Pump"));
                var valve = new Widget(Valve, Harbor, NorthPier, "Valve");
                valve.AddPart("Gasket");
                widgets.Widgets.Add(valve);
                widgets.Widgets.Add(new Widget(Gauge, Harbor, South, "Gauge"));
                await widgets.SaveChangesAsync(cancellationToken);
            },
            scope: "widgets");
        await services.BySystemIn(
            Orchard,
            async scoped =>
            {
                scoped.Widgets().Widgets.Add(new Widget(Crate, Orchard, OrchardRoot, "Crate"));
                await scoped.Widgets().SaveChangesAsync(cancellationToken);
            },
            scope: "widgets");
    }

    /// <summary>
    /// Gives <paramref name="person"/> a role in Harbor at <paramref name="unit"/>, placing them there first where they are
    /// not: Harbor's Administrator, every key there and below, or with <paramref name="keys"/> a role of its own holding
    /// those alone. System work through the use cases, committed.
    /// </summary>
    public static async Task HoldAtAsync(TestDatabase database, Person person, OrganizationUnitId unit, IReadOnlyCollection<string>? keys, CancellationToken cancellationToken)
    {
        await using var services = new TenancyServices(database);
        var role = keys is null
            ? HarborRoles.Administrator
            : await services.BySystemIn(Harbor, scoped => scoped.Roles().CreateAsync("Desk for " + string.Join(" and ", keys), "Holds what a test needs", keys, cancellationToken));

        await services.BySystemIn(Harbor, async scoped =>
        {
            var seat = (await scoped.GetRequiredService<HostTenancy.IStore>().FindSeatAsync(person.Seat, cancellationToken))!;
            if (seat.Placements.All(placement => placement.UnitId != unit))
            {
                await scoped.Seats().PlaceAsync(person.Seat, unit, primary: false, cancellationToken);
            }
        });
        await services.BySystemIn(Harbor, scoped => scoped.Seats().GrantAsync(person.Seat, unit, role, until: null, reason: null, cancellationToken));
    }

    /// <summary>What the database's clock says now: what the policies call <c>now()</c>.</summary>
    public static async Task<DateTimeOffset> DatabaseNowAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT now()", connection);
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync(cancellationToken))!, TimeSpan.Zero);
    }

    private static async Task ProvisionAsync(TenancyServices services, TenantId tenant, string slug, OrganizationUnitId root, Person administrator, Roles roles, CancellationToken cancellationToken)
    {
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await services.InScopeAsync(scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision(
                    slug,
                    char.ToUpperInvariant(slug[0]) + slug[1..] + " Works",
                    TenantShape.Hierarchical,
                    char.ToUpperInvariant(slug[0]) + slug[1..],
                    "company",
                    administrator.Identity,
                    administrator.Name,
                    TenantId: tenant,
                    RootId: root,
                    AdminSeatId: administrator.Seat,
                    RoleIds: roles.ByPack),
                cancellationToken));
        }
    }

    private static async Task SeatPersonAsync(
        TenancyServices services,
        TenantId tenant,
        Person person,
        OrganizationUnitId primary,
        IReadOnlyList<(OrganizationUnitId Unit, RoleId Role, DateTimeOffset? From, DateTimeOffset? Until)> grants,
        CancellationToken cancellationToken)
    {
        await services.BySystemIn(tenant, scoped => scoped.Seats().AddSeatAsync(person.Identity, person.Name, cancellationToken, person.Seat));
        await services.BySystemIn(tenant, scoped => scoped.Seats().PlaceAsync(person.Seat, primary, primary: true, cancellationToken));
        foreach (var (unit, role, from, until) in grants)
        {
            await services.BySystemIn(tenant, scoped => scoped.Seats().GrantAsync(person.Seat, unit, role, until, reason: null, cancellationToken, from));
        }
    }

    private static OrganizationUnitId Unit(int number) => new(Fixed(0xa, number));

    private static SeatId Seat(int number) => new(Fixed(0xc, number));

    private static RoleId Role(int tenant, int number) => new(Fixed(0xe, tenant * 100 + number));

    private static WidgetId Widget(int number) => new(Fixed(0xf, number));

    /// <summary>A fixed id: <c>a0000000-0000-4000-8000-000000000001</c> for kind <c>a</c> and number 1.</summary>
    private static Guid Fixed(int kind, int number) => Guid.Parse($"{kind:x}0000000-0000-4000-8000-{number:D12}");

    /// <summary>A person of the seed: their verified identity, their name and their seat in the tenant they are seeded in.</summary>
    public sealed record Person(int Number, string Name)
    {
        public Guid Identity { get; } = Fixed(0xd, Number);

        public SeatId Seat { get; } = TenancySeed.Seat(100 + Number);
    }

    /// <summary>The roles a tenant is provisioned with, one per pack of the TestHost, with fixed ids.</summary>
    public sealed record Roles(int Tenant)
    {
        public RoleId Administrator { get; } = Role(Tenant, 1);

        public RoleId Supervisor { get; } = Role(Tenant, 2);

        public RoleId Operator { get; } = Role(Tenant, 3);

        public RoleId Watcher { get; } = Role(Tenant, 4);

        public IReadOnlyDictionary<string, RoleId> ByPack => new Dictionary<string, RoleId>(StringComparer.Ordinal)
        {
            [HostCatalogue.AdministratorPack] = Administrator,
            [HostCatalogue.SupervisorPack] = Supervisor,
            [HostCatalogue.OperatorPack] = Operator,
            [HostCatalogue.WatcherPack] = Watcher,
        };
    }
}
