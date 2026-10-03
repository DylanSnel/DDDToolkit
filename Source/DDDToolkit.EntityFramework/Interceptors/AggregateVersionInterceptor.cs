using System.Runtime.ExceptionServices;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Interceptors;

/// <summary>
/// Optimistic concurrency for aggregates. On every <c>SaveChanges</c> it increments
/// <see cref="IAggregateRoot.Version"/> of each root that is being written: a modified root, a newly
/// added root (its version becomes 1) and the owning root of any child that changed, whether the
/// child is an owned entity, an element of an owned collection, or an entity related through a
/// foreign key whose principal is an aggregate root. Each root is bumped at most once per save.
/// <para>
/// Together with the <c>Version</c> concurrency token registered by
/// <c>AddDDDToolkitConventions</c> this turns "two users saved the same aggregate" into a
/// <see cref="ConcurrencyConflictException"/> (the original <see cref="DbUpdateConcurrencyException"/>
/// is the inner exception) instead of a silent last-write-wins.
/// </para>
/// <para>
/// The interceptor calls <c>DetectChanges</c> first (unless auto-detection is disabled on the context)
/// so that changes made by domain event handlers in the same save are versioned too. Register it after
/// <see cref="PublishDomainEventsInterceptor"/>; <c>UseDDDToolkit</c> does this for you.
/// </para>
/// <para>
/// <b>A statement that found no row is not always a lost race.</b> Under row level security, an update or a
/// delete of a row a policy hides from the statement finds no row either, and no retry will change that. So
/// before the conflict is thrown, each row the failed statement was for is read again by its key, as the same
/// caller and through the context's filters: its own concurrency tokens, or its aggregate root's
/// <c>Version</c> for a child that has none. When every one is still there, unchanged, nobody else wrote, and
/// the save is refused with <see cref="ToolkitRefusals.Refused"/> instead, a <see cref="RefusalException"/> of
/// kind <see cref="RefusalKind.NotPermitted"/> with the conflict as its inner exception, and a warning is logged
/// through the context's logger factory. A row that is gone, hidden or changed is the conflict it always was.
/// That costs one query for each row the failed statement was for, and none for a save that succeeds.
/// </para>
/// </summary>
public sealed class AggregateVersionInterceptor : SaveChangesInterceptor
{
    private const int MaxOwnershipDepth = 32;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            BumpVersions(context);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            BumpVersions(context);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
        => throw Translate(eventData);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        => throw Translate(eventData);

    /// <inheritdoc />
    /// <remarks>
    /// The update pipeline wraps whatever <see cref="ThrowingConcurrencyException"/> throws in a
    /// <see cref="DbUpdateException"/>. Throwing from here replaces the outgoing exception, so callers
    /// can <c>catch (ConcurrencyConflictException)</c> directly, and <c>catch (RefusalException)</c> for a
    /// save a policy denied.
    /// </remarks>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (Unwrap(eventData.Exception, out var entries) is not { } conflict)
        {
            return;
        }

        if (eventData.Context is { } context && DatabaseRefusals.DeniedNotLate(context, RowsToReadAgain(context, entries), conflict) is { } refusal)
        {
            throw refusal;
        }

        ExceptionDispatchInfo.Throw(conflict);
    }

    /// <inheritdoc />
    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Unwrap(eventData.Exception, out var entries) is not { } conflict)
        {
            return;
        }

        if (eventData.Context is { } context
            && await DatabaseRefusals.DeniedNotLateAsync(context, RowsToReadAgain(context, entries), conflict, cancellationToken).ConfigureAwait(false) is { } refusal)
        {
            throw refusal;
        }

        ExceptionDispatchInfo.Throw(conflict);
    }

    /// <summary>The conflict <paramref name="exception"/> is or carries, with the entries of the statement that found no row.</summary>
    private static ConcurrencyConflictException? Unwrap(Exception exception, out IReadOnlyList<EntityEntry> entries)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ConcurrencyConflictException conflict:
                    entries = (conflict.InnerException as DbUpdateConcurrencyException)?.Entries ?? [];
                    return conflict;
                case DbUpdateConcurrencyException concurrency when concurrency.InnerException is not ConcurrencyConflictException:
                    // A provider raised the conflict without passing through ThrowingConcurrencyException.
                    entries = concurrency.Entries;
                    return Translate(concurrency.Entries, concurrency);
            }
        }

        entries = [];
        return null;
    }

    /// <summary>
    /// The rows to read again to tell a denied save from a lost race: each entry of the failed statement that has
    /// concurrency tokens of its own, and for one that has none, or is owned and so read only through its owner,
    /// the aggregate root it belongs to, whose <c>Version</c> stands for the whole aggregate. Each once. Empty
    /// when one of them has neither, since nothing can then be compared.
    /// </summary>
    private static IReadOnlyList<EntityEntry> RowsToReadAgain(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var rows = new List<EntityEntry>();

        foreach (var entry in entries)
        {
            if (!entry.Metadata.IsOwned() && entry.Metadata.GetProperties().Any(static property => property.IsConcurrencyToken))
            {
                if (seen.Add(entry.Entity))
                {
                    rows.Add(entry);
                }

                continue;
            }

            var roots = FindOwningRoots(context, entry).ToList();
            if (roots.Count == 0)
            {
                return [];
            }

            rows.AddRange(roots.Where(root => seen.Add(root.Entity)));
        }

        return rows;
    }

    /// <summary>Increments the version of every aggregate root that this save touches, once per root.</summary>
    public static void BumpVersions(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        var bumped = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IAggregateRoot)
            {
                if (entry.State != EntityState.Deleted)
                {
                    Bump(entry, bumped);
                }

                continue;
            }

            foreach (var root in FindOwningRoots(context, entry))
            {
                if (root.State != EntityState.Deleted)
                {
                    Bump(root, bumped);
                }
            }
        }
    }

    private static void Bump(EntityEntry root, HashSet<object> bumped)
    {
        if (!bumped.Add(root.Entity))
        {
            return;
        }

        var version = root.Property(nameof(IAggregateRoot.Version));
        version.CurrentValue = root.State == EntityState.Added ? 1L : ((long)(version.CurrentValue ?? 0L)) + 1;
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

    private static ConcurrencyConflictException Translate(ConcurrencyExceptionEventData eventData)
        => Translate(eventData.Entries, eventData.Exception);

    private static ConcurrencyConflictException Translate(IReadOnlyList<EntityEntry> entries, Exception inner)
    {
        var entry = entries.FirstOrDefault(static e => e.Entity is IAggregateRoot) ?? entries.FirstOrDefault();
        if (entry is null)
        {
            return new ConcurrencyConflictException(aggregateType: null, aggregateId: null, inner);
        }

        var key = entry.Metadata.FindPrimaryKey();
        object? id = key is null
            ? null
            : key.Properties.Count == 1
                ? entry.Property(key.Properties[0].Name).CurrentValue
                : string.Join("|", key.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString()));

        return new ConcurrencyConflictException(entry.Metadata.ClrType, id, inner);
    }
}
