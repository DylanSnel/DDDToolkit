using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.Managers;

/// <summary>
/// A tenant's units and the closure of its tree, read once, to name units by their path from the root and
/// to find every unit below a unit. The units are the organization's own, which carry the names; the rows the
/// access questions read have none.
/// </summary>
internal sealed class TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TTenantId : struct, ICreatableEntityId<TTenantId>, IEquatable<TTenantId>
    where TUnitId : struct, ICreatableEntityId<TUnitId>, IEquatable<TUnitId>
    where TSeatId : struct, ICreatableEntityId<TSeatId>, IEquatable<TSeatId>
    where TRoleId : struct, ICreatableEntityId<TRoleId>, IEquatable<TRoleId>
{
    private readonly Dictionary<TUnitId, TUnit> _units;
    private readonly Dictionary<TUnitId, List<OrganizationUnitPath<TTenantId, TUnitId>>> _above = [];
    private readonly Dictionary<TUnitId, List<TUnitId>> _below = [];

    private TenancyUnitMap(IReadOnlyList<TUnit> units, IReadOnlyList<OrganizationUnitPath<TTenantId, TUnitId>> paths)
    {
        _units = units.ToDictionary(unit => unit.Id);
        Root = units.FirstOrDefault(unit => unit.IsRoot)?.Id;

        foreach (var path in paths)
        {
            if (!_above.TryGetValue(path.DescendantId, out var above))
            {
                _above[path.DescendantId] = above = [];
            }

            above.Add(path);

            if (!_below.TryGetValue(path.AncestorId, out var below))
            {
                _below[path.AncestorId] = below = [];
            }

            below.Add(path.DescendantId);
        }
    }

    /// <summary>The root, or <see langword="null"/> when the organization has no units.</summary>
    public TUnitId? Root { get; }

    /// <summary>Every unit's id.</summary>
    public IEnumerable<TUnitId> Ids => _units.Keys;

    /// <summary>The map of <paramref name="organization"/>, which is read already: one read, of the closure.</summary>
    public static async Task<TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>> ReadAsync(
        ITenancyStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId> store,
        TOrganization organization,
        CancellationToken cancellationToken)
    {
        var tenant = organization.Id;
        var paths = await store.Queries.ListAsync(store.Reads.UnitPaths.Where(path => path.TenantId.Equals(tenant)), cancellationToken).ConfigureAwait(false);
        return new TenancyUnitMap<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>(organization.Units, paths);
    }

    /// <summary>The unit, or <see langword="null"/> when the organization has none with the id.</summary>
    public TUnit? Unit(TUnitId id) => _units.GetValueOrDefault(id);

    /// <summary>The unit and every unit below it.</summary>
    public IEnumerable<TUnitId> Subtree(TUnitId id) => _below.TryGetValue(id, out var below) ? below : [id];

    /// <summary>The names of the unit and the units above it, the root first.</summary>
    public string PathOf(TUnitId id)
        => _above.TryGetValue(id, out var above)
            ? string.Join(" / ", above.OrderByDescending(path => path.Distance).Select(path => _units.TryGetValue(path.AncestorId, out var unit) ? unit.Name : path.AncestorId.ToString()))
            : _units.TryGetValue(id, out var own) ? own.Name : id.ToString() ?? string.Empty;

    /// <summary>How deep the unit is, the root being 1.</summary>
    public int DepthOf(TUnitId id) => _above.TryGetValue(id, out var above) ? above.Count : 1;

    /// <summary>The unit by its path.</summary>
    public SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.UnitRef Ref(TUnitId id) => new(id, PathOf(id));

    /// <summary>The units, each once, by path.</summary>
    public IReadOnlyList<SeatManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.UnitRef> Refs(IEnumerable<TUnitId> ids)
        => ids.Distinct().Select(Ref).OrderBy(unit => unit.Path, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// The units among <paramref name="ids"/> that the organization has, each once and whole, with its path and
    /// depth, by path.
    /// </summary>
    public IReadOnlyList<OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.UnitInTree> InTree(IEnumerable<TUnitId> ids)
        => ids
            .Distinct()
            .Select(Unit)
            .OfType<TUnit>()
            .Select(unit => new OrganizationManager<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.UnitInTree(unit, PathOf(unit.Id), DepthOf(unit.Id)))
            .OrderBy(unit => unit.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
