using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;

namespace DDDToolkit.EntityFramework.Owners;

/// <summary>
/// The owners of the aggregates whose id is <typeparamref name="TId"/>, read with Entity Framework:
/// <see cref="IAggregateOwners{TId}"/> for an aggregate a context of the host maps. <c>AddDDDToolkitEntityFramework</c>
/// registers it for every id, per scope, so the core's check of <c>[Own]</c> and <c>[OrOwn]</c> on an aggregate's id reads
/// the owner with nothing more written.
/// </summary>
/// <remarks>
/// Which context maps the aggregate, and which column its owner is, is found once for the host, the first time a request
/// about it is checked (<see cref="OwnerColumns"/>), and the owner is then read in one statement as the caller. A host
/// that registers an <see cref="IAggregateOwners{TId}"/> of its own for an id has that one asked instead.
/// </remarks>
/// <typeparam name="TId">The aggregate's id.</typeparam>
/// <param name="services">The services of the scope the check runs in, where the contexts are.</param>
/// <param name="columns">Where the owner of each aggregate is found, kept for the host.</param>
internal sealed class EfAggregateOwners<TId>(IServiceProvider services, OwnerColumns columns) : IAggregateOwners<TId>
    where TId : struct, IEntityId, IEquatable<TId>
{
    private OwnerColumn<TId>? _column;

    /// <inheritdoc />
    public Type OwnerType => Column.OwnerType;

    /// <inheritdoc />
    public ValueTask<AggregateOwner?> ReadAsync(TId aggregate, CancellationToken cancellationToken)
        => Column.ReadAsync(services, aggregate, cancellationToken);

    /// <summary>The column the owner is read from, found the first time it is asked for.</summary>
    private OwnerColumn<TId> Column => _column ??= columns.For<TId>(services);
}
