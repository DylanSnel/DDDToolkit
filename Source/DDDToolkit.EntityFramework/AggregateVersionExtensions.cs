using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Optimistic concurrency that reaches back to the reader: the version a client last saw decides whether its
/// change is made, not the version the server happened to load a moment ago.
/// </summary>
public static class AggregateVersionExtensions
{
    /// <summary>
    /// Says that the client last saw <paramref name="aggregate"/> at <paramref name="version"/>. If it is
    /// loaded at another version, this throws <see cref="ConcurrencyConflictException"/> now, before anything
    /// changes; otherwise the save compares against that version, so a change made by somebody else after the
    /// load is the same conflict.
    /// <code>
    /// var project = await context.Projects.SingleAsync(project => project.Id == id, cancellationToken);
    /// context.ExpectVersion(project, expectedVersion);   // what the client read, from If-Match or the request
    /// project.Rename(name);
    /// await context.SaveChangesAsync(cancellationToken);
    /// </code>
    /// <para>
    /// <see cref="AggregateVersionInterceptor"/> alone protects the moment between a load and its save. A
    /// client that read a project a minute ago, and saves a change it decided on then, loads the project again
    /// as it is now, and its save succeeds over whatever happened in that minute. Handing the version the
    /// client read to this method closes that gap. Both failures are one exception, because the client does the
    /// same thing for both: read again, and decide again.
    /// </para>
    /// <para>
    /// Call it after the load and before the change. It reads nothing from the database and changes nothing in
    /// the context.
    /// </para>
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root's type.</typeparam>
    /// <param name="context">The context that loaded the aggregate and will save it.</param>
    /// <param name="aggregate">The aggregate, as this context loaded it.</param>
    /// <param name="version">The <see cref="IAggregateRoot.Version"/> the client last saw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="aggregate"/> is null.</exception>
    /// <exception cref="ConcurrencyConflictException">The aggregate is loaded at another version than the client saw.</exception>
    /// <exception cref="InvalidOperationException">
    /// This context did not load the aggregate, the aggregate is new and has no stored version yet, or the model
    /// does not map <see cref="IAggregateRoot.Version"/> as a concurrency token, which
    /// <c>AddDDDToolkitConventions()</c> does: nothing would then compare the version at the save.
    /// </exception>
    public static void ExpectVersion<TAggregate>(this DbContext context, TAggregate aggregate, long version)
        where TAggregate : class, IAggregateRoot
        => Expect(context, aggregate, version);

    /// <summary>
    /// Says that the client last saw <paramref name="aggregate"/> at <paramref name="version"/>, when it says
    /// which version it saw: the one line a handler writes after its load for a request that may name one, such
    /// as a command whose <c>ExpectedVersion</c> came from <c>If-Match</c>.
    /// <code>
    /// var project = await context.Projects.SingleAsync(project => project.Id == command.Id, cancellationToken);
    /// context.ExpectVersion(project, command.ExpectedVersion);   // a stale one is a conflict, none compares nothing
    /// </code>
    /// A version is held as <see cref="ExpectVersion{TAggregate}(DbContext, TAggregate, long)"/> holds it. With
    /// none, <see langword="null"/>, the client named nothing to compare, and the save compares against the version
    /// the aggregate was loaded at, as it always does: a change made after the load is still a conflict, and one
    /// made before it is the last write that wins, which is what a client that sends no version asks for. What
    /// this refuses either way, an aggregate this context did not load, a new one, a model that would not compare
    /// the version at the save, it refuses with none as well, so the mistake shows on the first request and not on
    /// the first one that names a version.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root's type.</typeparam>
    /// <param name="context">The context that loaded the aggregate and will save it.</param>
    /// <param name="aggregate">The aggregate, as this context loaded it.</param>
    /// <param name="version">The <see cref="IAggregateRoot.Version"/> the client last saw, or <see langword="null"/> when it named none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="aggregate"/> is null.</exception>
    /// <exception cref="ConcurrencyConflictException">The aggregate is loaded at another version than the client saw.</exception>
    /// <exception cref="InvalidOperationException">
    /// This context did not load the aggregate, the aggregate is new and has no stored version yet, or the model
    /// does not map <see cref="IAggregateRoot.Version"/> as a concurrency token.
    /// </exception>
    public static void ExpectVersion<TAggregate>(this DbContext context, TAggregate aggregate, long? version)
        where TAggregate : class, IAggregateRoot
        => Expect(context, aggregate, version);

    /// <summary>Holds <paramref name="aggregate"/> to <paramref name="version"/>, or only to being loaded and compared at the save when there is none.</summary>
    private static void Expect<TAggregate>(DbContext context, TAggregate aggregate, long? version)
        where TAggregate : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(aggregate);

        var entry = context.Entry(aggregate);
        var name = entry.Metadata.ClrType.Name;

        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"The {name} is not tracked by this {context.GetType().Name}. ExpectVersion compares the version an aggregate was loaded with, "
                + "so load the aggregate through the context that will save it first.");
        }

        if (entry.State == EntityState.Added)
        {
            throw new InvalidOperationException(
                $"The {name} is new and has no stored version yet, so no client can have seen one. ExpectVersion is for an aggregate that was loaded.");
        }

        if (entry.Metadata.FindProperty(nameof(IAggregateRoot.Version)) is not { IsConcurrencyToken: true })
        {
            throw new InvalidOperationException(
                $"The model of {context.GetType().Name} does not map {name}.{nameof(IAggregateRoot.Version)} as a concurrency token, "
                + "so a save would not compare it and a change made after the load would go unnoticed. "
                + "Call configurationBuilder.AddDDDToolkitConventions() in ConfigureConventions.");
        }

        // The original value is what the row had when it was loaded, or after this context's last save, and it
        // is what the save puts in its WHERE clause. When it is the version the client saw, there is nothing to
        // set: the save already compares against it. A client that named none has nothing to compare.
        if (version is not { } expected)
        {
            return;
        }

        var loaded = entry.Property(nameof(IAggregateRoot.Version)).OriginalValue;
        if (loaded is not long stored || stored != expected)
        {
            throw new ConcurrencyConflictException(entry.Metadata.ClrType, IdOf(entry));
        }
    }

    /// <summary>The aggregate's id as the conflict of a failed save names it: the key's value, or its values joined when it has several.</summary>
    private static object? IdOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? null
            : key.Properties.Count == 1
                ? entry.Property(key.Properties[0].Name).CurrentValue
                : string.Join("|", key.Properties.Select(property => entry.Property(property.Name).CurrentValue?.ToString()));
    }
}
