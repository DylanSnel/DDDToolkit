using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Lists the entries that a save writes: the added, modified and deleted entries of the context.
/// <para>
/// The list comes from the state manager, which keeps a separate set of entries for each state. The unchanged entries
/// are not visited. <c>ChangeTracker.Entries()</c> detects the changes again and creates an <see cref="EntityEntry"/>
/// for every tracked entity, unchanged ones included. The state manager is Entity Framework's internal API (EF1001).
/// <see cref="ChangedAggregateRoots"/> also uses it to find a principal.
/// </para>
/// </summary>
internal static class ChangedEntries
{
    /// <summary>
    /// Calls <c>DetectChanges</c> when <c>AutoDetectChangesEnabled</c> is on, and lists the added, modified and deleted
    /// entries. The list is a copy, so the caller can change the state of an entry while it iterates.
    /// </summary>
    public static List<EntityEntry> Detect(DbContext context)
    {
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

#pragma warning disable EF1001 // Internal EF Core API usage.
        // The flags are added, modified, deleted, unchanged and shared identity. ChangeTracker.Entries() also lists a
        // deleted entry that shares its row with an added one, so the last flag is on. The flags are passed by position
        // because the name of the last parameter differs between Entity Framework releases.
        var stateManager = context.GetService<IStateManager>();
        var entries = new List<EntityEntry>(stateManager.GetCountForState(true, true, true, false, true));
        foreach (var entry in stateManager.GetEntriesForState(true, true, true, false, true))
        {
            entries.Add(new EntityEntry(entry));
        }
#pragma warning restore EF1001

        return entries;
    }
}
