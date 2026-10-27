using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Access;

/// <summary>
/// Reads the owner of an aggregate whose id is <typeparamref name="TId"/>, as the caller, for a request about the owner's
/// own (<see cref="AccessRequirement.OwnerOf{TId}"/>): the value of the property the aggregate marks <c>[Owner]</c>, in the
/// one row the id names. The core's <see cref="CurrentUserAccessCheck"/> asks it, and compares what it read with what the
/// <see cref="ICallersOwn{TValue}"/> of the owner's type says is the caller's.
/// </summary>
/// <remarks>
/// The Entity Framework package registers one for every id, with <c>AddDDDToolkitEntityFramework</c>, which a host on Entity
/// Framework calls already: it finds the context that maps the aggregate among those the host registered, when the host
/// starts (the start-up check <c>entity-framework.owners-mapped</c>) or at the first request about it, and reads the
/// column the <c>[Owner]</c> property maps, in one statement, so a host writes nothing for it. Where the host runs its
/// queries as the caller, that statement runs as the caller too, and row level security decides whether the row is there.
/// A storage of another kind registers one of its own for the id, per scope, which takes the place of the package's for
/// that id:
/// <code>
/// services.AddScoped&lt;IAggregateOwners&lt;BoardId&gt;, BoardOwners&gt;();
/// </code>
/// <para>
/// It refuses nobody. A row there is none of for the caller is answered <see langword="null"/>, and the check refuses the
/// request as one about an aggregate that is not found; a row whose owner is null is answered as nobody's, and the check
/// refuses it as one about somebody else's.
/// </para>
/// </remarks>
/// <typeparam name="TId">The aggregate's id.</typeparam>
public interface IAggregateOwners<TId>
    where TId : struct, IEntityId, IEquatable<TId>
{
    /// <summary>
    /// The type of the owner, as the aggregate's <c>[Owner]</c> property is declared, without its <c>?</c>: the type the
    /// caller's own is asked of, before anything is read.
    /// </summary>
    /// <exception cref="InvalidOperationException">The storage keeps no aggregate of the id with an owner it can read.</exception>
    Type OwnerType { get; }

    /// <summary>
    /// The owner of the aggregate whose id is <paramref name="aggregate"/>, read as the caller, in one statement; or
    /// <see langword="null"/> where there is no such row for the caller: none was made, it is gone, or the caller may not
    /// see it.
    /// </summary>
    /// <param name="aggregate">The id of the aggregate the request is about.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    ValueTask<AggregateOwner?> ReadAsync(TId aggregate, CancellationToken cancellationToken);
}

/// <summary>
/// The owner an <see cref="IAggregateOwners{TId}"/> read of one aggregate: the value of the property it marks <c>[Owner]</c>,
/// as its row holds it, boxed, or <see langword="null"/> for a row that is nobody's.
/// </summary>
/// <param name="Owner">The owner, of <see cref="IAggregateOwners{TId}.OwnerType"/>; <see langword="null"/> for nobody.</param>
public sealed record AggregateOwner(object? Owner)
{
    /// <summary>The owner of a row whose owner is null: nobody, whom no caller is.</summary>
    public static AggregateOwner Nobody { get; } = new(Owner: null);
}
