using System.Collections.Concurrent;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Membership.TestHost.Crates;
using DDDToolkit.Supporting.Membership.TestHost.Pallets;
using DDDToolkit.Supporting.Membership.TestHost.Persistence;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Membership.TestHost.Depot;

// What the depot answers for the resources that stand beside it, written by hand: the class an application
// hands to a resource's registration. Everything the package knows about the depot, it knows through these.

/// <summary>
/// The logical names of the depot's functions in a database that answers the questions itself: what the
/// resources' rules name, and what the depot defines them under. Texts, so the rules refer to nothing.
/// </summary>
public static class DepotFunctions
{
    /// <summary>The owner the depot's functions are named relative to.</summary>
    public const string Owner = "depot";

    /// <summary>Answers the calling porter, or <c>NULL</c> for a caller the depot does not count.</summary>
    public const string CallerPorter = Owner + "/caller_porter";

    /// <summary>Answers, for a key, the depot's roles in use that give it.</summary>
    public const string RolesWithKey = Owner + "/roles_with_key";

    /// <summary>Answers, for a key, the bays the calling porter's hold of it reaches: where it is held, and every bay below.</summary>
    public const string BaysWhereIHold = Owner + "/bays_where_i_hold";
}

/// <summary>
/// Who the depot knows its callers as, looked up before any question is asked: an application reads this when
/// a request begins, so that answering who the caller is reads nothing. Here it is filled when the depot's
/// porters are saved.
/// </summary>
public sealed class DepotDesk
{
    private readonly ConcurrentDictionary<Guid, (PorterId Porter, bool Active)> _porters = new();

    /// <summary>Takes a porter on, or says again whether one counts.</summary>
    public void TakeOn(Porter porter) => _porters[porter.UserId] = (porter.Id, porter.Active);

    /// <summary>The porter <paramref name="caller"/> is, when the depot counts it.</summary>
    public PorterId? ActivePorterOf(Caller caller)
        => caller.UserId is { } user && _porters.TryGetValue(user, out var known) && known.Active ? known.Porter : null;

    /// <summary>Whether the depot knows <paramref name="caller"/> at all, counting or not.</summary>
    public bool HasTakenOn(Caller caller) => caller.UserId is { } user && _porters.ContainsKey(user);
}

/// <summary>
/// What the depot answers for its pallets: who the caller is as a member, and who can be made one. A pallet's
/// roles are its own and nothing above reaches it, so that is all.
/// </summary>
/// <param name="desk">Who the depot knows its callers as.</param>
/// <param name="context">The depot's rows.</param>
public sealed class PalletsInTheDepot(DepotDesk desk, DepotContext context) : ICallerMember<PalletId, PorterId>, IMemberDirectory<PalletId, PorterId>
{
    /// <inheritdoc />
    public PorterId? Find(Caller caller) => desk.ActivePorterOf(caller);

    /// <inheritdoc />
    public async ValueTask<bool> IsActiveAsync(PorterId member, CancellationToken cancellationToken)
        => await context.Porters.AnyAsync(porter => porter.Id == member && porter.Active, cancellationToken);
}

/// <summary>
/// What the depot answers for its crates, in one class: who the caller is as a member and who can be made
/// one, which of its roles there are and which give a key, and where the caller holds a key among its bays.
/// </summary>
/// <param name="desk">Who the depot knows its callers as.</param>
/// <param name="context">The depot's rows, for what is asked outside a statement.</param>
public sealed class CratesInTheDepot(DepotDesk desk, DepotContext context)
    : ICallerMember<CrateId, PorterId>,
      IMemberDirectory<CrateId, PorterId>,
      IMemberRoles<CrateId, DepotRoleId>,
      IRolesWithKey<CrateId, DepotRoleId>,
      IPlacesReached<CrateId, BayId>
{
    /// <summary>What a caller the depot does not know is refused with, where a question refuses.</summary>
    public const string NotOfTheDepot = "depot.not-a-porter";

    /// <inheritdoc />
    public PorterId? Find(Caller caller) => desk.ActivePorterOf(caller);

    /// <inheritdoc />
    /// <remarks>Somebody the depot never took on is nobody its crates can be asked about, and is told so.</remarks>
    public void Require(Caller caller)
    {
        if (!desk.HasTakenOn(caller))
        {
            throw new RefusalException(NotOfTheDepot, RefusalKind.NotPermitted, "The depot does not know this caller.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsActiveAsync(PorterId member, CancellationToken cancellationToken)
        => await context.Porters.AnyAsync(porter => porter.Id == member && porter.Active, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(DepotRoleId role, CancellationToken cancellationToken)
        => await context.Roles.AnyAsync(kept => kept.Id == role && kept.InUse, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<DepotRoleId?> FindOwnerRoleAsync(CancellationToken cancellationToken)
        => await context.Roles
            .Where(kept => kept.Name == CrateMembership.Rules.OwnerRole && kept.InUse)
            .Select(kept => (DepotRoleId?)kept.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>Over the context the statement runs on, which is not always this request's own.</remarks>
    public IQueryable<DepotRoleId> RolesWith(DbContext context, Caller caller, string key)
        => from given in context.Set<DepotRoleKey>()
           where given.Key == key
           join kept in context.Set<DepotRole>() on given.RoleId equals kept.Id
           where kept.InUse
           select given.RoleId;

    /// <inheritdoc />
    public IQueryable<BayId> PlacesReached(DbContext context, Caller caller, string key)
    {
        var user = caller.UserId;
        return from porter in context.Set<Porter>()
               where porter.UserId == user && porter.Active
               join hold in context.Set<BayHold>() on porter.Id equals hold.PorterId
               where hold.Key == key
               join path in context.Set<BayPath>() on hold.BayId equals path.AboveId
               select path.BayId;
    }
}
