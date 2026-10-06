using DDDToolkit.Abstractions.Access;
using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Application.Seats.Queries;
using GreenDonut.Data;

namespace Examples.Tenancy.Tenants.Application.StoredTenancy;

/// <summary>
/// Where this module's queries read Tenancy. The application declares it; the infrastructure project implements
/// it and registers it, scoped.
/// </summary>
/// <remarks>
/// Every read gets storage of its own, and none uses the unit of work the request's commands save through. A
/// request may send several queries side by side, as the resolvers of one GraphQL request do, and one unit of
/// work runs one query at a time: two queries on it would meet. So nothing here hands out anything that outlives
/// one query.
/// <list type="bullet">
/// <item><see cref="Open"/> is for a query the application composes itself, of Tenancy's rows and the access
/// questions asked over them: both read the reading's one context, so what is composed of them runs as one
/// statement.</item>
/// <item><see cref="AskDirectoryAsync"/> and <see cref="SeatsOfAsync"/> are for what the Tenancy package answers:
/// its directory of the tenant's units, seats and roles, and its lookup of a person's seats. The package makes
/// those over the context of the scope they are resolved in, so each question gets a scope of its own, and with
/// it a context of its own: the same promise, kept by other means, and the one place in the sample where a read
/// does not take its context from the module's factory.</item>
/// <item><see cref="TenantsAsync"/> asks the package's directory of every tenant, for the application's operators,
/// in a scope of its own as well; and <see cref="HistoryAsync"/> reads a page of one tenant's access history, on a
/// context of that read's own.</item>
/// <item><see cref="OpenInvitationsAsync"/> asks the package for the open invitations the caller may read, in a
/// scope of its own too, and <see cref="GrantsOfAsync"/> reads one seat's roles through the package's store, the
/// same way.</item>
/// </list>
/// The package's commands need no port of this module's: they load and save through the package's own store,
/// <see cref="SampleTenancy.IStore"/>, one unit of work per request, and so does the command this module adds.
/// <para>
/// Public, because the infrastructure project implements it and a handler's constructor names it. No route
/// names it: a route only sends a request.
/// </para>
/// </remarks>
public interface ITenancyReads
{
    /// <summary>
    /// Opens a reading: Tenancy's rows on a context of its own, for one query. The caller disposes it,
    /// <c>await using</c>, when the query has run.
    /// </summary>
    ITenancyReading Open();

    /// <summary>
    /// Asks the Tenancy package's directory one question, in a scope of its own. The directory reads through the
    /// package's store, which the container makes over the context of the scope it is resolved in; given its own
    /// scope, a question shares no context with any other read or with the request's unit of work.
    /// </summary>
    /// <remarks>
    /// The directory checks the caller itself, as every use case of the package does, and refuses with the
    /// package's codes. The scope ends with the question: what is answered must be data, not something that still
    /// reads.
    /// </remarks>
    /// <typeparam name="TAnswer">What the directory answers with.</typeparam>
    /// <param name="ask">The question, put to the directory of that scope.</param>
    /// <exception cref="Exceptions.RefusalException">The directory's own refusal.</exception>
    Task<TAnswer> AskDirectoryAsync<TAnswer>(Func<SampleTenancy.TenancyDirectory, Task<TAnswer>> ask);

    /// <summary>
    /// Every seat the caller has, in every tenant and in any status, by the verified identity of their token: the
    /// one read that looks across tenants, and only ever for the caller's own identity; never an e-mail address.
    /// The Tenancy package answers it (<see cref="TenantSelection{TTenantId, TSeatId}.SeatsOfAsync"/>), by the rule
    /// it seats a caller in one tenant by: a caller whose token role holds no seat is answered none, as a person
    /// without a seat is. Read in a scope of its own, as <see cref="AskDirectoryAsync"/> is.
    /// </summary>
    /// <param name="caller">Who is calling, as the host verified it.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<IReadOnlyList<SeatOfCaller<TenantId, SeatId>>> SeatsOfAsync(Caller caller, CancellationToken cancellationToken);

    /// <summary>
    /// A page of every tenant of the application, by slug, as the Tenancy package's directory of tenants answers
    /// it: to an operator and to nobody else, which the directory checks itself. Read in a scope of its own, as
    /// <see cref="AskDirectoryAsync"/> is.
    /// </summary>
    /// <param name="after">The marker the page before answered as its next, or <see langword="null"/> for the first page.</param>
    /// <param name="size">How many tenants the page holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="Exceptions.RefusalException">The directory's own refusal.</exception>
    Task<SampleTenancy.TenantDirectoryPage> TenantsAsync(string? after, int size, CancellationToken cancellationToken);

    /// <summary>
    /// A page of the access history of <paramref name="tenant"/>, newest first: one statement, on a context of its
    /// own. The history is not kept to the caller's tenant by the storage's filter, so this names the tenant, and
    /// whoever calls it has decided first that the caller may read that tenant's history.
    /// </summary>
    /// <param name="tenant">The tenant whose history is read.</param>
    /// <param name="paging">Which page: how many rows, and after which marker.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="Exceptions.RefusalException"><c>tenancy.cursor-invalid</c> for a marker this list did not give.</exception>
    Task<Page<AccessHistoryEntry>> HistoryAsync(TenantId tenant, PagingArguments paging, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's invitations that can still be accepted, the soonest to end first, as the Tenancy package
    /// answers them to the caller: those into the units where it manages seats, which may be none. The package
    /// checks the caller itself. Read in a scope of its own, as <see cref="AskDirectoryAsync"/> is.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="Exceptions.RefusalException">The package's own refusal.</exception>
    Task<IReadOnlyList<SampleTenancy.OpenInvitation<InvitationId>>> OpenInvitationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The roles <paramref name="seat"/> holds, each where and for which period, by the unit's path and then the
    /// role's name: read through the Tenancy package's store, in a scope of its own, as <see cref="AskDirectoryAsync"/>
    /// is. A seat the caller's tenant does not have holds none. Whoever calls it has decided first that the caller
    /// may read another seat's roles; the storage asks again, as the caller.
    /// </summary>
    /// <param name="seat">The seat whose roles are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<SeatGrant>> GrantsOfAsync(SeatId seat, CancellationToken cancellationToken);
}
