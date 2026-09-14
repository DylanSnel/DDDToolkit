using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Everything a <c>SaveChanges</c> has to ask to prove itself: every tracked entity the save adds or
/// modifies, child entities included, and the aggregate root of each changed child.
/// <para>
/// Everything on this list is asked <c>EnsureOwnInvariants()</c> and nothing wider. A child is asked
/// for its own rules and its root separately for the root's, because the two are different questions:
/// the child's rule is about the child alone, while the root's may span children this save never
/// touched. Asking each object for itself is also what keeps the count honest, since a changed child
/// and its root are both here, and a root asked <c>EnsureInvariants()</c> would walk straight back
/// into the child and report it a second time.
/// </para>
/// <para>
/// The list is built from change tracker entries and never from a navigation property, which is the
/// whole reason this lives in the persistence layer rather than in the entity. The tracker knows by
/// construction only what was loaded and what changed, so reading it cannot trigger a lazy load,
/// cannot query, and cannot fail on a graph whose children were never loaded. The aggregate's own
/// walk over <c>_lines</c> could do all three, which is why that walk is the domain's answer to a
/// question about an aggregate in memory, and this list is the save's answer to a question about a
/// unit of work.
/// </para>
/// </summary>
internal static class EntitiesToCheck
{
    /// <summary>
    /// Each object the save must ask, once, roots before the children they own so that a broken
    /// aggregate is reported in the aggregate's own words. Entities being deleted are left out, for
    /// the same reason <see cref="ChangedAggregateRoots"/> leaves out a deleted root: something on
    /// its way out of the database has no state left to be consistent about.
    /// </summary>
    public static IReadOnlyList<IHasInvariants> Find(DbContext context)
    {
        var found = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var targets = new List<IHasInvariants>();

        // Find detects changes first, so the loop below sees the same save this one did.
        foreach (var root in ChangedAggregateRoots.Find(context))
        {
            Add(root.Entity, found, targets);
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Add(entry.Entity, found, targets);
            }
        }

        return targets;
    }

    private static void Add(object entity, HashSet<object> found, List<IHasInvariants> targets)
    {
        if (entity is IHasInvariants hasInvariants && found.Add(entity))
        {
            targets.Add(hasInvariants);
        }
    }
}
