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
/// This is the same walk <see cref="AggregateVersionInterceptor"/> does to decide which versions to
/// bump, kept separately so <see cref="InvariantInterceptor"/> can ask the same question without the
/// two interceptors sharing state.
/// </para>
/// </summary>
internal static class ChangedAggregateRoots
{
    private const int MaxOwnershipDepth = 32;

    /// <summary>
    /// Every aggregate root this save adds or modifies, each returned once. Roots that are being
    /// deleted are left out: an aggregate on its way out of the database has no state left to be
    /// consistent about.
    /// </summary>
    public static IReadOnlyList<EntityEntry> Find(DbContext context)
    {
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        var found = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var roots = new List<EntityEntry>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IAggregateRoot)
            {
                Add(entry, found, roots);
                continue;
            }

            foreach (var root in FindOwningRoots(context, entry))
            {
                Add(root, found, roots);
            }
        }

        return roots;
    }

    private static void Add(EntityEntry root, HashSet<object> found, List<EntityEntry> roots)
    {
        if (root.State != EntityState.Deleted && found.Add(root.Entity))
        {
            roots.Add(root);
        }
    }

    /// <summary>
    /// Walks from a child entry to the aggregate root(s) it belongs to: through the ownership when
    /// the entity is owned, otherwise through foreign keys whose principal is an aggregate root that
    /// has a navigation back to this entity (the shape of a child table inside an aggregate).
    /// </summary>
    private static IEnumerable<EntityEntry> FindOwningRoots(DbContext context, EntityEntry child)
    {
        var results = new List<EntityEntry>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { child.Entity };
        Walk(context, child, results, visited, depth: 0);
        return results;
    }

    private static void Walk(DbContext context, EntityEntry current, List<EntityEntry> results, HashSet<object> visited, int depth)
    {
        if (depth >= MaxOwnershipDepth)
        {
            return;
        }

        foreach (var foreignKey in OwnerForeignKeys(current.Metadata))
        {
            var principal = FindPrincipal(context, current, foreignKey);
            if (principal is null || !visited.Add(principal.Entity))
            {
                continue;
            }

            if (principal.Entity is IAggregateRoot)
            {
                results.Add(principal);
            }
            else
            {
                Walk(context, principal, results, visited, depth + 1);
            }
        }
    }

    private static IEnumerable<IForeignKey> OwnerForeignKeys(IEntityType entityType)
    {
        var ownership = entityType.FindOwnership();
        if (ownership is not null)
        {
            return [ownership];
        }

        return entityType.GetForeignKeys().Where(static fk =>
            fk.PrincipalToDependent is not null
            && typeof(IAggregateRoot).IsAssignableFrom(fk.PrincipalEntityType.ClrType));
    }

    private static EntityEntry? FindPrincipal(DbContext context, EntityEntry dependent, IForeignKey foreignKey)
    {
        if (foreignKey.DependentToPrincipal is { } navigation)
        {
            var target = dependent.Navigation(navigation.Name).CurrentValue;
            if (target is not null)
            {
                return context.Entry(target);
            }
        }

        // Owned entities usually have no navigation back to their owner, only the (often shadow) FK.
        // The state manager resolves the tracked principal from the FK values through its identity map
        // without touching the database. This is EF Core internal API (EF1001); it has been stable
        // since EF Core 3 and is the documented escape hatch for exactly this lookup.
#pragma warning disable EF1001 // Internal EF Core API usage.
        var stateManager = context.GetService<IStateManager>();
        var principalEntry = stateManager.FindPrincipal(dependent.GetInfrastructure(), foreignKey);
        return principalEntry is null ? null : new EntityEntry(principalEntry);
#pragma warning restore EF1001
    }
}
