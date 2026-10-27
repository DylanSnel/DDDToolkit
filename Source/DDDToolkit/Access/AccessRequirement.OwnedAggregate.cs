using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Access;

public abstract partial record AccessRequirement
{
    /// <summary>
    /// The request is about the owner's own aggregate: the caller is the owner of the aggregate <paramref name="aggregate"/>
    /// names, as the property the aggregate marks <c>[Owner]</c> says it. Anyone else is refused with
    /// <see cref="ToolkitRefusals.NotOwn"/>, and an aggregate there is no row of for the caller with
    /// <see cref="ToolkitRefusals.NotFound"/>. The request names the aggregate, and the check reads its owner at the door,
    /// so the request never says whose it is.
    /// <code>
    /// AccessRequirement IRequireAccess.RequiredAccess =&gt; AccessRequirement.Any(
    ///     AccessRequirement.OwnerOf(Board),                              // the board's owner,
    ///     TenancyAccess.AtTenant(BoardPermissions.Manage));           // or the permission for every board
    /// </code>
    /// A request says it with the attributes it says the caller's own with as well: <see cref="OwnAttribute"/> on the
    /// aggregate's id with <see cref="OwnOnlyAttribute"/> for the owner alone, <see cref="OrOwnAttribute"/> beside
    /// <see cref="RequiresPermissionAttribute"/> for the owner or the permission.
    /// </summary>
    /// <remarks>
    /// The fact is the domain's, and the policy the request's: the aggregate marks the property that names its owner,
    /// <c>[Owner] public UserId Owner { get; private set; }</c>, and says nothing of who may do what. The owner is of a type
    /// the caller has an own of, as the value of <see cref="Own{TValue}"/> is, and the <see cref="ICallersOwn{TValue}"/> of
    /// that type says what the caller's own is. A row whose owner is null is nobody's, and nobody is its owner.
    /// <para>
    /// What reads the owner is the storage's, an <see cref="IAggregateOwners{TId}"/>: the Entity Framework package's reads
    /// the one column of the one row, in one statement, as the caller, so row level security still decides whether the row
    /// is there for the caller at all. A caller who has no own of the owner's type, one who did not sign in or the
    /// application's own work, is refused before anything is read. Several owners of one aggregate are no case of this
    /// one: that is a resource with members, which Membership decides.
    /// </para>
    /// </remarks>
    /// <param name="aggregate">The id of the aggregate the request is about.</param>
    /// <typeparam name="TId">The aggregate's id.</typeparam>
    public static OwnedAggregate<TId> OwnerOf<TId>(TId aggregate)
        where TId : struct, IEntityId, IEquatable<TId>
        => new(aggregate);

    /// <summary>
    /// What <see cref="OwnerOf"/> answers: the request is about the aggregate <see cref="Aggregate"/> names, and the caller
    /// is its owner. Decided by the core's <see cref="CurrentUserAccessCheck"/>, which has the owner read by the
    /// <see cref="IAggregateOwners{TId}"/> registered for <typeparamref name="TId"/>, and compares it with what the
    /// <see cref="ICallersOwn{TValue}"/> of the owner's type says is the caller's.
    /// </summary>
    /// <typeparam name="TId">The aggregate's id.</typeparam>
    [AccessCheckRegistration("services.AddDDDToolkitEntityFramework(), which reads the owner of an aggregate from the context that maps it; a storage of another kind registers an IAggregateOwners<TId> that reads it")]
    public sealed record OwnedAggregate<TId> : AccessRequirement, IOwnedAggregateRequirement
        where TId : struct, IEntityId, IEquatable<TId>
    {
        /// <summary>Made by <see cref="OwnerOf"/>, the one spelling a request writes.</summary>
        /// <param name="aggregate">The id of the aggregate the request is about.</param>
        internal OwnedAggregate(TId aggregate) => Aggregate = aggregate;

        /// <summary>The id of the aggregate the request is about, whose owner the caller is compared with.</summary>
        public TId Aggregate { get; }

        /// <inheritdoc />
        bool IOwnedAggregateRequirement.CanBeAnswered(IServiceProvider? services) => services?.GetService<IAggregateOwners<TId>>() is not null;

        /// <inheritdoc />
        async ValueTask IOwnedAggregateRequirement.RequireAsync(ICurrentUser user, IServiceProvider services, CancellationToken cancellationToken)
        {
            var owners = services.GetService<IAggregateOwners<TId>>()
                ?? throw new InvalidOperationException(
                    $"Nothing reads the owner of an aggregate whose id is {WrittenTypeNames.Of(typeof(TId))}. Register an IAggregateOwners<{WrittenTypeNames.Of(typeof(TId))}>, as services.AddDDDToolkitEntityFramework() does; a requirement nothing checks lets nobody through.");

            var type = owners.OwnerType;
            var handle = OwnHandle.Of(type) is { } known && known.CanBeAnswered(services)
                ? known
                : throw new InvalidOperationException(
                    $"The owner of an aggregate whose id is {WrittenTypeNames.Of(typeof(TId))} is a {WrittenTypeNames.Of(type)}, and nothing says what the caller's own {WrittenTypeNames.Of(type)} is. "
                    + $"Register an ICallersOwn<{WrittenTypeNames.Of(type)}> with services.AddCallersOwn<{WrittenTypeNames.Of(type)}, TCallersOwn>(); a requirement nothing checks lets nobody through.");

            // Asked before anything is read: a caller with no own of the type owns nothing, and a database that checks
            // every row may refuse such a caller the table outright, rather than answer that it sees no row.
            if (handle.Find(user, services) is not { } own)
            {
                throw ToolkitRefusals.Refuse(ToolkitRefusals.NotOwn);
            }

            // One column of one row, read as the caller: a row the caller does not see is as missing as one never made.
            var read = await owners.ReadAsync(Aggregate, cancellationToken).ConfigureAwait(false)
                ?? throw ToolkitRefusals.Refuse(ToolkitRefusals.NotFound);

            // A row whose owner is null is nobody's: whoever it is about, it is not the caller.
            if (read.Owner is not { } owner || !own.Equals(owner))
            {
                throw ToolkitRefusals.Refuse(ToolkitRefusals.NotOwn);
            }
        }
    }
}

/// <summary>The core's view of <see cref="AccessRequirement.OwnedAggregate{TId}"/>, whatever the id's type, so its check reads no type argument.</summary>
internal interface IOwnedAggregateRequirement
{
    /// <summary>Whether something reads the owner of an aggregate of the requirement's id: nothing is read to tell.</summary>
    /// <param name="services">The scope's services, where a reader is registered; <see langword="null"/> for none.</param>
    bool CanBeAnswered(IServiceProvider? services);

    /// <summary>Returns when <paramref name="user"/> is the owner of the requirement's aggregate, and throws otherwise.</summary>
    /// <param name="user">Who is calling.</param>
    /// <param name="services">The scope's services, where the reader and the caller's own of the owner's type are registered.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <exception cref="RefusalException"><see cref="ToolkitRefusals.NotOwn"/> or <see cref="ToolkitRefusals.NotFound"/>.</exception>
    /// <exception cref="InvalidOperationException">Nothing reads the owner, or nothing says what the caller's own of its type is.</exception>
    ValueTask RequireAsync(ICurrentUser user, IServiceProvider services, CancellationToken cancellationToken);
}
