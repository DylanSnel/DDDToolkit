using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Finds the aggregate roots a <c>SaveChanges</c> is about to write: a root that is added or
/// modified itself, and the owning root of any entry that changed but is not a root, whether that
/// entry is an owned entity, an element of an owned collection, or an entity related through a
/// foreign key whose principal is an aggregate root.
/// <para>
/// <see cref="InvariantInterceptor"/> asks it which roots to check, and <see cref="AggregateVersionInterceptor"/>
/// which versions to bump. Each asks on its own, at its own point of the save, and the two share no state.
/// </para>
/// </summary>
internal static class ChangedAggregateRoots
{
    private const int MaxOwnershipDepth = 32;

    /// <summary>The foreign keys that lead from an entity type to its owner, read once per entity type of a model.</summary>
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> OwnerKeys = new();

    /// <summary>
    /// Every aggregate root this save adds or modifies, each returned once. Roots that are being
    /// deleted are left out: an aggregate on its way out of the database has no state left to be
    /// consistent about. Detects changes first, where the context detects them on its own.
    /// </summary>
    public static List<EntityEntry> Find(DbContext context) => Find(context, ChangedEntries.Detect(context));

    /// <summary>The aggregate roots of <paramref name="changed"/>, the entries a save writes, as <see cref="Find(DbContext)"/> describes.</summary>
    public static List<EntityEntry> Find(DbContext context, List<EntityEntry> changed)
    {
        var found = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var roots = new List<EntityEntry>();
        Walker? walker = null;

        foreach (var entry in changed)
        {
            if (entry.Entity is IAggregateRoot)
            {
                Add(entry, found, roots);
                continue;
            }

            walker ??= new Walker(context);
            foreach (var root in walker.RootsOf(entry))
            {
                Add(root, found, roots);
            }
        }

        return roots;
    }

    /// <summary>
    /// The aggregate roots <paramref name="child"/> belongs to, deleted ones included: through the ownership when the
    /// entity is owned, otherwise through foreign keys whose principal is an aggregate root that has a navigation back
    /// to this entity (the shape of a child table inside an aggregate).
    /// </summary>
    public static List<EntityEntry> RootsOf(DbContext context, EntityEntry child) => [.. new Walker(context).RootsOf(child)];

    private static void Add(EntityEntry root, HashSet<object> found, List<EntityEntry> roots)
    {
        if (root.State != EntityState.Deleted && found.Add(root.Entity))
        {
            roots.Add(root);
        }
    }

    /// <summary>
    /// The walk from a child to its roots, with the state manager and the two collections it needs held once for
    /// every child of a save, rather than made again for each.
    /// </summary>
    private sealed class Walker(DbContext context)
    {
        private readonly List<EntityEntry> _results = [];
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

#pragma warning disable EF1001 // Internal EF Core API usage.
        private IStateManager? _stateManager;

        private IStateManager StateManager => _stateManager ??= context.GetService<IStateManager>();
#pragma warning restore EF1001

        /// <summary>The roots of <paramref name="child"/>. The list is reused by the next call.</summary>
        public List<EntityEntry> RootsOf(EntityEntry child)
        {
            _results.Clear();
            _visited.Clear();
            _visited.Add(child.Entity);
            Walk(child, depth: 0);
            return _results;
        }

        private void Walk(EntityEntry current, int depth)
        {
            if (depth >= MaxOwnershipDepth)
            {
                return;
            }

            foreach (var foreignKey in OwnerForeignKeys(current.Metadata))
            {
                var principal = FindPrincipal(current, foreignKey);
                if (principal is null || !_visited.Add(principal.Entity))
                {
                    continue;
                }

                if (principal.Entity is IAggregateRoot)
                {
                    _results.Add(principal);
                }
                else
                {
                    Walk(principal, depth + 1);
                }
            }
        }

        private EntityEntry? FindPrincipal(EntityEntry dependent, IForeignKey foreignKey)
        {
#pragma warning disable EF1001 // Internal EF Core API usage.
            if (foreignKey.DependentToPrincipal is { } navigation)
            {
                var target = dependent.Navigation(navigation.Name).CurrentValue;
                if (target is not null)
                {
                    // The entry the state manager already holds. Only a target it does not track goes through
                    // context.Entry, which detects the changes of that one entity and starts an entry for it.
                    return StateManager.TryGetEntry(target) is { } tracked ? new EntityEntry(tracked) : context.Entry(target);
                }
            }

            // Owned entities usually have no navigation back to their owner, only the (often shadow) FK.
            // The state manager resolves the tracked principal from the FK values through its identity map
            // without touching the database. This is EF Core internal API (EF1001); it has been stable
            // since EF Core 3 and is the documented escape hatch for exactly this lookup.
            var principalEntry = StateManager.FindPrincipal(dependent.GetInfrastructure(), foreignKey);
            return principalEntry is null ? null : new EntityEntry(principalEntry);
#pragma warning restore EF1001
        }
    }

    private static IForeignKey[] OwnerForeignKeys(IEntityType entityType)
        => OwnerKeys.GetValue(entityType, static type =>
        {
            if (type.FindOwnership() is { } ownership)
            {
                return [ownership];
            }

            return [.. type.GetForeignKeys().Where(static fk =>
                fk.PrincipalToDependent is not null
                && typeof(IAggregateRoot).IsAssignableFrom(fk.PrincipalEntityType.ClrType))];
        });
}
