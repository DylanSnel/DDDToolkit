using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// The entries a save writes: those the context tracks as added, modified or deleted.
/// <para>
/// They are read from the state manager, which keeps the entries of each state apart. So the list costs what the
/// save writes, not what the context tracks. <c>ChangeTracker.Entries()</c> made an <see cref="EntityEntry"/> for
/// every tracked entity, the unchanged ones included, and detected changes again before it did. This is Entity
/// Framework's internal API (EF1001), the same the toolkit's own interceptors use.
/// </para>
/// </summary>
internal static class ChangedEntries
{
    /// <summary>
    /// Detects changes once, where the context detects them on its own, and lists the entries the save writes. A
    /// list, not a live view, so the caller may change the state of an entry while it walks them.
    /// </summary>
    public static List<EntityEntry> Detect(DbContext context)
    {
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

#pragma warning disable EF1001 // Internal EF Core API usage.
        // Added, modified, deleted, not unchanged, and a deleted entry that shares its row with an added one too, as
        // ChangeTracker.Entries() lists it. By position: the name of the last parameter differs between releases.
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
