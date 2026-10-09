using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests.Support;

/// <summary>Short ways to make the host's instances, through the package's own <see cref="TenancyInstances"/>.</summary>
public static class New
{
    /// <summary>The host's catalogue, with any contributions a test adds.</summary>
    public static TenancyCatalogue Catalogue(params Permission[] contributed)
        => TenancyCatalogue.Build(HostCatalogue.Application, contributed);

    /// <summary>Hand widgets to people: the one key of the application that <see cref="ListingCatalogue"/> marks as managing access.</summary>
    public const string WidgetAssign = "widget.assign";

    /// <summary>The pack of the role that hands widgets to people, in <see cref="ListingCatalogue"/>.</summary>
    public const string KeeperPack = "keeper";

    /// <summary>
    /// The host's catalogue with an administrators' pack that lists its keys: Tenancy's own and
    /// <see cref="WidgetAssign"/>, which is every key that manages access, and none of the keys to work with
    /// widgets. <see cref="KeeperPack"/> is the pack that holds the marked key, so there is a role besides the
    /// supervisor's and the administrators' own that only a holder of its key gives.
    /// </summary>
    public static TenancyCatalogue ListingCatalogue()
        => TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs =
                [
                    .. HostCatalogue.Application.Packs.Select(pack => pack.Administers
                        ? pack with { Keys = [.. TenancyKeys.Permissions.Select(permission => permission.Key), WidgetAssign] }
                        : pack),
                    new RolePack(KeeperPack, "Keeper", "Hands widgets to people", [WidgetAssign, HostCatalogue.WidgetRead], Order: 50),
                ],
                Permissions = [.. HostCatalogue.Permissions, new Permission(WidgetAssign, "Widgets", "Hand widgets to people", Order: 40, ManagesAccess: true)],
            },
            []);

    /// <summary>A tenant being provisioned.</summary>
    public static HostTenant Tenant(TenantShape shape = TenantShape.Hierarchical, string slug = "harbor", long id = 1)
        => TenancyInstances.NewTenant<HostTenant, TenantId, SeatId>(new TenantId(id), TenantSlug.Create(slug).ToValid(), shape);

    /// <summary>An organization with its root.</summary>
    public static HostOrganization Organization(long tenant = 1, string name = "Harbor Works")
        => TenancyInstances.NewOrganization<HostOrganization, TenantId, HostUnit, OrganizationUnitId, SeatId>(
            new TenantId(tenant), name, OrganizationUnitId.CreateSequential(), name);

    /// <summary>An active seat, placed nowhere yet. It has no name: a seat has none in Tenancy.</summary>
    public static HostSeat Seat(long tenant = 1, Guid? identity = null)
        => TenancyInstances.NewSeat<HostSeat, SeatId, TenantId, OrganizationUnitId, RoleId>(
            SeatId.CreateSequential(), new TenantId(tenant), identity ?? Guid.NewGuid());

    /// <summary>An active role made by hand.</summary>
    public static HostRole Role(TenancyCatalogue catalogue, string name, params string[] keys)
        => TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(RoleId.CreateSequential(), new TenantId(1), new RoleDraft(name, string.Empty, keys), catalogue);

    /// <summary>What a seat is told about an active role.</summary>
    public static RoleFacts ActiveRole(params string[] keys) => new(true, keys);
}

/// <summary>
/// A hierarchical tenant, "Harbor Works", as provisioning leaves it: active, its organization with North,
/// North Coast below it and South, a role copied from every pack seeded for its shape, and a first administrator
/// placed at the root with the administrators' role, open-ended. Every event is drained, so a test sees only
/// what it does itself.
/// </summary>
public sealed class HarborBuilder
{
    private DateTimeOffset _now = FixedClock.Start;
    private TenancyCatalogue? _catalogue;
    private long _tenant = 1;
    private string _slug = "harbor";

    /// <summary>When the first administrator is granted the administrators' role.</summary>
    public HarborBuilder At(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    /// <summary>A catalogue other than the host's plain one.</summary>
    public HarborBuilder With(TenancyCatalogue catalogue)
    {
        _catalogue = catalogue;
        return this;
    }

    /// <summary>Another tenant than the first, with its own id and slug.</summary>
    public HarborBuilder Tenant(long id, string slug)
    {
        _tenant = id;
        _slug = slug;
        return this;
    }

    /// <summary>Builds it.</summary>
    public Harbor Build()
    {
        var catalogue = _catalogue ?? New.Catalogue();
        var tenant = New.Tenant(TenantShape.Hierarchical, _slug, _tenant);
        var organization = New.Organization(tenant.Id.Value);

        var root = organization.Root.Id;
        var north = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), root, "North", tenant.Shape).Id;
        var northCoast = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), north, "North Coast", tenant.Shape).Id;
        var south = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), root, "South", tenant.Shape).Id;

        var roles = catalogue.PacksFor(tenant.Shape).ToDictionary(
            pack => pack.Key,
            pack => TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
                RoleId.CreateSequential(), tenant.Id, new RoleDraft(pack.Name, pack.Description, pack.Keys, pack.Key), catalogue));

        var administratorRole = roles[catalogue.AdministratorPackFor(tenant.Shape).Key];
        var administrator = New.Seat(tenant.Id.Value);
        administrator.Rename("Ada");
        administrator.Place(root, primary: true, _now, placedBy: null);
        administrator.Grant(root, administratorRole.Id, administratorRole.Facts, GrantPeriod.Open(_now), grantedBy: null, "set up with the tenant");
        tenant.Activate<SeatId>();

        var harbor = new Harbor(catalogue, tenant, organization, root, north, northCoast, south, roles, administrator);
        harbor.DrainEvents();
        return harbor;
    }
}

/// <summary>What <see cref="HarborBuilder"/> built.</summary>
public sealed record Harbor(
    TenancyCatalogue Catalogue,
    HostTenant Tenant,
    HostOrganization Organization,
    OrganizationUnitId Root,
    OrganizationUnitId North,
    OrganizationUnitId NorthCoast,
    OrganizationUnitId South,
    IReadOnlyDictionary<string, HostRole> RolesByPack,
    HostSeat Administrator)
{
    /// <summary>The role copied from the administrators' pack.</summary>
    public HostRole AdministratorRole => RolesByPack[HostCatalogue.AdministratorPack];

    /// <summary>What the use case would tell a seat about each of the tenant's roles.</summary>
    public RoleFacts? FactsOf(RoleId role) => RolesByPack.Values.FirstOrDefault(candidate => candidate.Id == role)?.Facts;

    /// <summary>Another active seat of the tenant, placed nowhere yet.</summary>
    public HostSeat NewSeat() => New.Seat(Tenant.Id.Value);

    /// <summary>Every aggregate, to seed a store with.</summary>
    public object[] Aggregates => [Tenant, Organization, Administrator, .. RolesByPack.Values];

    /// <summary>Drops every pending event, so a test sees only what it does itself.</summary>
    public void DrainEvents()
    {
        Tenant.DrainEvents();
        Organization.DrainEvents();
        Administrator.DrainEvents();
        foreach (var role in RolesByPack.Values)
        {
            role.DrainEvents();
        }
    }
}
