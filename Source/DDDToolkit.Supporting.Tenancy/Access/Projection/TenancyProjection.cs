using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Works out the rows the access questions read from the aggregates they come from: the closure of an
/// organization's tree, and the keys a seat holds where. Pure functions, with no storage: the storage writes
/// what they return in the same save as the change it follows from, and the tests call them directly.
/// </summary>
public static class TenancyProjection
{
    /// <summary>
    /// Every pair of a unit and a unit at or below it, with the levels between them; every unit paired with
    /// itself at 0. Archived units are included: rights held at an archived unit keep working.
    /// </summary>
    /// <param name="organization">The organization.</param>
    /// <exception cref="InvalidOperationException">
    /// The tree is broken: a unit is above itself, is deeper than <c>MaxDepth</c>, or hangs under a unit the
    /// organization does not have. Its invariants refuse to save such a tree, so this is never reached
    /// through the package's methods, and it stops rather than loops when it is.
    /// </exception>
    public static IReadOnlyList<OrganizationUnitPath<TTenantId, TUnitId>> ClosureOf<TTenantId, TUnit, TUnitId>(
        OrganizationAggregate<TTenantId, TUnit, TUnitId> organization)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    {
        ArgumentNullException.ThrowIfNull(organization);

        var byId = new Dictionary<TUnitId, TUnit>();
        foreach (var unit in organization.Units)
        {
            byId.TryAdd(unit.Id, unit);
        }

        var paths = new List<OrganizationUnitPath<TTenantId, TUnitId>>();
        foreach (var descendant in organization.Units)
        {
            var visited = new HashSet<TUnitId> { descendant.Id };
            var current = descendant;
            var distance = 0;

            while (true)
            {
                paths.Add(new OrganizationUnitPath<TTenantId, TUnitId>
                {
                    TenantId = organization.Id,
                    AncestorId = current.Id,
                    DescendantId = descendant.Id,
                    Distance = distance,
                });

                if (current.ParentId is not { } parentId)
                {
                    break;
                }

                distance++;
                if (distance >= OrganizationAggregate<TTenantId, TUnit, TUnitId>.MaxDepth)
                {
                    throw Unprojectable(organization.Id, "the unit " + descendant.Id + " is deeper than " + OrganizationAggregate<TTenantId, TUnit, TUnitId>.MaxDepth + " levels");
                }

                if (!visited.Add(parentId))
                {
                    throw Unprojectable(organization.Id, "the unit " + parentId + " is above itself");
                }

                current = byId.TryGetValue(parentId, out var parent)
                    ? parent
                    : throw Unprojectable(organization.Id, "the unit " + current.Id + " hangs under " + parentId + ", which it does not have");
            }
        }

        return paths;
    }

    /// <summary>
    /// The keys a seat holds, one row for each unit, role and key, with the period of the grant it comes
    /// from. A seat that is not active holds nothing. A grant of a role that is archived, or that
    /// <paramref name="roles"/> does not know, holds nothing. A key the catalogue has retired, or no longer
    /// knows, holds nowhere. Whether the period applies is left to whoever reads the rows.
    /// </summary>
    /// <param name="tenant">The seat's tenant.</param>
    /// <param name="seat">The seat.</param>
    /// <param name="status">The seat's status.</param>
    /// <param name="grants">The seat's grants, from <see cref="GrantsOf{TSeatId, TTenantId, TUnitId, TRoleId}"/> or from a query.</param>
    /// <param name="roles">What is known about each role, or <see langword="null"/> for a role that is not found.</param>
    /// <param name="catalogue">The catalogue that says which keys are live.</param>
    public static IReadOnlyList<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> RightsOf<TTenantId, TSeatId, TUnitId, TRoleId>(
        TTenantId tenant,
        TSeatId seat,
        SeatStatus status,
        IEnumerable<GrantFact<TUnitId, TRoleId>> grants,
        Func<TRoleId, RoleFacts?> roles,
        TenancyCatalogue catalogue)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(catalogue);

        if (status != SeatStatus.Active)
        {
            return [];
        }

        var rights = new List<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>();
        var written = new HashSet<(TUnitId Unit, TRoleId Role, string Key)>();
        foreach (var grant in grants)
        {
            if (roles(grant.RoleId) is not { IsActive: true } facts)
            {
                continue;
            }

            foreach (var key in facts.Keys)
            {
                if (catalogue.IsLive(key) && written.Add((grant.UnitId, grant.RoleId, key)))
                {
                    rights.Add(new SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>
                    {
                        TenantId = tenant,
                        SeatId = seat,
                        UnitId = grant.UnitId,
                        RoleId = grant.RoleId,
                        Key = key,
                        StartsAt = grant.StartsAt,
                        EndsAt = grant.EndsAt,
                    });
                }
            }
        }

        return rights;
    }

    /// <summary>Every grant of a seat in memory, with the unit of the placement it belongs to.</summary>
    /// <param name="seat">The seat.</param>
    public static IEnumerable<GrantFact<TUnitId, TRoleId>> GrantsOf<TSeatId, TTenantId, TUnitId, TRoleId>(
        SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId> seat)
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(seat);

        return seat.Placements
            .SelectMany(placement => placement.Grants.Select(grant => new GrantFact<TUnitId, TRoleId>(placement.UnitId, grant.RoleId, grant.StartsAt, grant.EndsAt)))
            .ToArray();
    }

    private static InvalidOperationException Unprojectable(object organization, string what)
        => new("The organization " + organization + " cannot be projected: " + what + ". Its invariants refuse such a tree; it was changed behind its methods.");
}
