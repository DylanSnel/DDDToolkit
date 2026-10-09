using Examples.Tenancy.Tenants.Application.History;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Operators.Queries;

/// <summary>
/// The access history of one tenant, for the application's own staff: the rows a holder of
/// <c>tenancy.history.view</c> reads in that tenant, newest first and a page at a time.
/// </summary>
/// <remarks>
/// For operators only. An operator works in no tenant, so the query names the one it reads. A tenant that does
/// not exist has no history: the answer is an empty page, as for a tenant where nothing happened yet.
/// </remarks>
/// <param name="Tenant">The tenant whose history is read.</param>
/// <param name="Paging">Which page: how many rows, and after which marker.</param>
public sealed record TenantAccessHistory(TenantId Tenant, PagingArguments Paging) : IQuery<Page<AccessHistoryEntry>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.RequiresOperator();
}

/// <summary>Answers <see cref="TenantAccessHistory"/> on a context of the read's own.</summary>
/// <param name="reads">Where the history is read.</param>
public sealed class TenantAccessHistoryHandler(ITenancyReads reads) : IQueryHandler<TenantAccessHistory, Page<AccessHistoryEntry>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenants.history.page-size-invalid</c>, with <c>Max</c>; <c>tenancy.cursor-invalid</c> for a marker that is
    /// not this list's.
    /// </exception>
    public async ValueTask<Page<AccessHistoryEntry>> Handle(TenantAccessHistory query, CancellationToken cancellationToken)
        => await reads.HistoryAsync(query.Tenant, HistoryRefusals.Checked(query.Paging), cancellationToken);
}
